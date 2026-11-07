using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    public enum CombatRole
    {
        Neutral,
        /// <summary>Built to hit things up close (gorilla arms, berserk running): closes in even with a gun in hand.</summary>
        Melee,
        /// <summary>Built to shoot or hack (smart link, optics crit, cyberdeck): backs off when someone gets close.</summary>
        Ranged,
    }

    /// <summary>When the AI pops a self-buff (CompProperties_AbilityCyberBuff.aiRule).</summary>
    public enum CyberBuffAiRule
    {
        /// <summary>Enemy within aiMaxEnemyDistance.</summary>
        Engaged,
        /// <summary>Sandevistan: melee and hybrid builds to close a gap, ranged builds to break away.</summary>
        CloseDistance,
        /// <summary>Berserk: pinned by suppression, or hurt with the enemy on top of them.</summary>
        Pinned,
    }

    /// <summary>
    /// Fight behaviour for NPCs and cyberpsychos, on top of vanilla (Patch_JobGiver_AIFightEnemy_TryGiveJob). Player-controlled
    /// colonists are never touched. All randomness free, so the same on every multiplayer client.
    /// </summary>
    public static class ContextualCombat
    {
        private const float ChargeRadius = 30f;
        private const float KeepDistance = 7f;
        private const int KiteCooldownTicks = 300;
        private const int HackThinkTicks = 120;
        private const float EngageRadius = 45f;

        public static CombatRole RoleOf(Pawn pawn)
        {
            var tracker = pawn.GetCyberTracker();
            if (tracker == null) return CombatRole.Neutral;
            foreach (var a in pawn.ActiveEffects())
                if (a.Ext?.meleeOnly == true) return CombatRole.Melee;
            var chrome = tracker.Cyberware;
            for (int i = 0; i < chrome.Count; i++)
                if (chrome[i].Ext?.combatRole == CombatRole.Melee) return CombatRole.Melee;
            if (pawn.equipment?.Primary?.def.IsRangedWeapon != true && !tracker.HasCyberdeck) return CombatRole.Neutral;
            if (tracker.HasCyberdeck || SmartLinkUtility.HasSmartLink(pawn) || pawn.GetStatValue(RPStatDefOf.RPC_RangedCritChance) > 0f) return CombatRole.Ranged;
            for (int i = 0; i < chrome.Count; i++)
                if (chrome[i].GetValue("distanceCrit") > 0f) return CombatRole.Ranged;
            return CombatRole.Neutral;
        }

        /// <summary>NPCs and anyone in a mental state. Never a colonist under the player's control.</summary>
        public static bool Applies(Pawn pawn) => pawn.Spawned && !pawn.Downed && (!pawn.IsColonist || pawn.InMentalState) && !pawn.Drafted;

        public static bool IsPinned(Pawn pawn)
        {
            var s = pawn.TryGetComp<CompSuppressable>();
            return s != null && (s.isSuppressed || s.IsHunkering);
        }

        public static bool IgnoresSuppression(Pawn pawn)
        {
            foreach (var a in pawn.ActiveEffects())
                if (a.Ext?.meleeOnly == true) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------ buff rules

        public static bool WantsBuff(Pawn pawn, CompProperties_AbilityCyberBuff props, Thing enemy)
        {
            float dist = pawn.Position.DistanceTo(enemy.Position);
            float health = pawn.health.summaryHealth.SummaryHealthPercent;
            switch (props.aiRule)
            {
                case CyberBuffAiRule.CloseDistance:
                    if (RoleOf(pawn) == CombatRole.Ranged) return dist <= 12f || health < 0.5f;
                    return dist > 6f && dist <= props.aiMaxEnemyDistance;
                case CyberBuffAiRule.Pinned:
                    if (IsPinned(pawn)) return true;
                    return dist <= props.aiMaxEnemyDistance && (RoleOf(pawn) == CombatRole.Melee || health < 0.6f);
                default:
                    return dist <= props.aiMaxEnemyDistance;
            }
        }

        // ------------------------------------------------------------------------------------ fight job

        /// <summary>Vanilla has picked a job; change it if this pawn's build calls for something else.</summary>
        public static Job AdjustFightJob(Pawn pawn, Job vanilla)
        {
            var enemy = pawn.mindState?.enemyTarget;
            var tracker = pawn.GetCyberTracker();
            if (enemy == null || tracker == null || !enemy.Spawned || enemy.Map != pawn.Map) return vanilla;
            float dist = pawn.Position.DistanceTo(enemy.Position);

            // being in a fight starts at spotting the enemy, not at the first shot: sandevistan can open it
            if (dist <= EngageRadius && tracker.HasCyberware)
            {
                bool wasInCombat = tracker.InCombat;
                tracker.Notify_CombatAction();
                // vanilla looked at abilities before that first rush arrived: look again, so a sandevistan can open the fight
                if (!wasInCombat && vanilla?.ability == null)
                {
                    var opener = JobGiver_AIFightEnemy.GetAbilityJob(pawn, enemy);
                    if (opener != null) return opener;
                }
            }

            var hack = TryPriorityHack(pawn, tracker);
            if (hack != null) return hack;
            if (vanilla?.ability != null) return vanilla;

            var role = RoleOf(pawn);
            if (role == CombatRole.Melee && enemy is Pawn && vanilla?.def != JobDefOf.AttackMelee && dist <= ChargeRadius && pawn.CanReach(enemy, PathEndMode.Touch, Danger.Deadly))
            {
                Diag.Count("ai-charge");
                var job = JobMaker.MakeJob(JobDefOf.AttackMelee, enemy);
                job.expiryInterval = 400;
                job.checkOverrideOnExpire = true;
                job.expireRequiresEnemiesNearby = true;
                return job;
            }
            if (role == CombatRole.Ranged && dist < KeepDistance && Find.TickManager.TicksGame - tracker.lastKiteTick >= KiteCooldownTicks && TryFindKiteCell(pawn, enemy, dist, out var cell))
            {
                tracker.lastKiteTick = Find.TickManager.TicksGame;
                Diag.Count("ai-kite");
                var job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                job.expiryInterval = 150;
                job.checkOverrideOnExpire = true;
                job.locomotionUrgency = LocomotionUrgency.Sprint;
                return job;
            }
            return vanilla;
        }

        /// <summary>A reachable standing spot further from the enemy that can still see them, nearest first. Fixed scan, no Rand.</summary>
        private static bool TryFindKiteCell(Pawn pawn, Thing enemy, float dist, out IntVec3 best)
        {
            best = IntVec3.Invalid;
            var map = pawn.Map;
            float bestScore = float.MinValue;
            int count = Mathf.Min(GenRadial.NumCellsInRadius(10f), GenRadial.RadialPattern.Length);
            for (int i = GenRadial.NumCellsInRadius(3f); i < count; i += 3)
            {
                var c = pawn.Position + GenRadial.RadialPattern[i];
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) continue;
                float d = c.DistanceTo(enemy.Position);
                if (d < dist + 3f) continue;
                if (!GenSight.LineOfSight(c, enemy.Position, map, skipFirstCell: true)) continue;
                if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                float score = d - c.DistanceTo(pawn.Position) * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best.IsValid;
        }

        // ------------------------------------------------------------------------------------ netrunner targeting

        /// <summary>
        /// Vanilla only considers hacking whoever the pawn is shooting at. Netrunners look past that: snipers and rival
        /// netrunners first, with control hacks that take them out of the fight ahead of plain damage.
        /// </summary>
        private static Job TryPriorityHack(Pawn pawn, CompCyberTracker tracker)
        {
            if (!tracker.HasCyberdeck || pawn.abilities == null) return null;
            int now = Find.TickManager.TicksGame;
            if (now - tracker.lastHackThinkTick < HackThinkTicks || Verb_Quickhack.IsUploading(pawn, out _)) return null;
            tracker.lastHackThinkTick = now;

            var hacks = pawn.abilities.AllAbilitiesForReading.OfType<QuickhackAbility>().Where(a => a.Ext != null && !a.Ext.aiNever && a.CanCast).ToList();
            if (hacks.Count == 0) return null;
            float range = hacks[0].verb.verbProps.range;

            Pawn best = null;
            float bestScore = 0f;
            var pawns = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == pawn || p.Downed || !p.HostileTo(pawn) || !p.Position.InHorDistOf(pawn.Position, range) || p.IsPsychologicallyInvisible()) continue;
                float score = HackTargetScore(p, pawn);
                if (score > bestScore && GenSight.LineOfSight(pawn.Position, p.Position, pawn.Map, skipFirstCell: true))
                {
                    bestScore = score;
                    best = p;
                }
            }
            if (best == null) return null;

            bool highValue = bestScore >= 3f;
            var netrun = MapComponent_Netrun.For(pawn.Map);
            foreach (var hack in hacks.OrderBy(a => HackPreference(a.Ext.category, highValue)))
            {
                if (netrun != null && netrun.IsUploading(hack.def, best)) continue;
                if (!hack.AICanTargetNow(best) || !hack.verb.CanHitTarget(best)) continue;
                if (highValue) Diag.Event("ai", $"{Diag.Describe(pawn)} hacks priority target {Diag.Describe(best)} with {hack.def.defName}");
                else Diag.Count("ai-hack");
                return hack.GetJob(best, best);
            }
            return null;
        }

        /// <summary>Snipers and netrunners outrank anyone else; closer is a tie-break.</summary>
        private static float HackTargetScore(Pawn target, Pawn from)
        {
            float score = 1f;
            var gun = target.equipment?.Primary?.def;
            if (gun != null && gun.IsRangedWeapon)
            {
                float reach = gun.Verbs.FirstOrDefault()?.range ?? 0f;
                if (reach >= 40f) score += 3f;
                else if (reach >= 25f) score += 1f;
            }
            if (target.GetCyberTracker()?.HasCyberdeck == true) score += 2f;
            return score + 0.5f * (1f - Mathf.Clamp01(target.Position.DistanceTo(from.Position) / 60f));
        }

        private static int HackPreference(QuickhackCategory category, bool highValue)
        {
            switch (category)
            {
                case QuickhackCategory.Control: return highValue ? 0 : 1;
                case QuickhackCategory.Combat: return highValue ? 1 : 0;
                case QuickhackCategory.Ultimate: return highValue ? 2 : 3;
                default: return 4;
            }
        }

        // ------------------------------------------------------------------------------------ periodic check

        /// <summary>
        /// From the tracker, once a second. Three things can't wait for the think tree: CE hunkers a suppressed pawn before the
        /// fight job giver runs, so berserk has to fire from here; a charge runs for about 7 s, too long to wait to pop a
        /// sandevistan; and Wait_Combat runs for up to 8 s, too long to notice someone walking up to a sniper or a brawler
        /// still holding a gun.
        /// </summary>
        public static void TickInterval(Pawn pawn, CompCyberTracker tracker)
        {
            if (!Applies(pawn) || !tracker.HasCyberware || pawn.mindState == null) return;
            if (IsPinned(pawn) && TryPopPinnedBuff(pawn)) return;

            var enemy = pawn.mindState.enemyTarget;
            if (enemy == null || !enemy.Spawned) return;
            TryPopSandevistan(pawn, enemy);
            if (pawn.CurJobDef != JobDefOf.Wait_Combat) return;
            var role = RoleOf(pawn);
            if (role == CombatRole.Neutral) return;
            float dist = pawn.Position.DistanceTo(enemy.Position);
            bool rethink = role == CombatRole.Melee ? dist <= ChargeRadius : dist < KeepDistance;
            if (rethink) pawn.jobs.CheckForJobOverride();
        }

        /// <summary>No warmup and no target, so it just activates: the pawn keeps charging or shooting while it starts.</summary>
        private static void TryPopSandevistan(Pawn pawn, Thing enemy)
        {
            if (pawn.abilities == null || pawn.CurJob?.ability != null) return;
            var list = pawn.abilities.AllAbilitiesForReading;
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is CyberwareAbility a)) continue;
                var buff = a.CompOfType<CompAbilityEffect_CyberBuff>();
                if (buff == null || buff.Props.aiRule != CyberBuffAiRule.CloseDistance || buff.IsActive || !a.CanCast) continue;
                if (!WantsBuff(pawn, buff.Props, enemy)) continue;
                if (a.Activate(new LocalTargetInfo(pawn), new LocalTargetInfo(pawn))) Diag.Count("ai-sandevistan");
                return;
            }
        }

        private static bool TryPopPinnedBuff(Pawn pawn)
        {
            if (pawn.abilities == null || pawn.CurJob?.ability != null) return false;
            var list = pawn.abilities.AllAbilitiesForReading;
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is CyberwareAbility a) || !a.CanCast) continue;
                var buff = a.CompOfType<CompAbilityEffect_CyberBuff>();
                if (buff == null || buff.Props.aiRule != CyberBuffAiRule.Pinned || buff.IsActive) continue;
                pawn.jobs.StartJob(a.GetJob(pawn, pawn), JobCondition.InterruptForced);
                Diag.Event("ai", $"{Diag.Describe(pawn)} pinned: {a.def.defName}");
                return true;
            }
            return false;
        }
    }
}
