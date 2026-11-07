using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>Events that can roll a cyberpsychosis chance. Which ones are used is decided in XML.</summary>
    public enum CyberpsychosisEvent
    {
        TookDamage,
        Downed,
        Killed,
        EnteredCombat,
        UsedCyberwareAbility,
        CyberwareInstalled,
    }

    public class CyberpsychosisEventChance
    {
        public CyberpsychosisEvent eventType;

        /// <summary>Chance per event at exactly the threshold (80% usage).</summary>
        public float chance;

        /// <summary>Extra chance per 10 percentage points of usage above the threshold.</summary>
        public float chancePerTenPercentOver;
    }

    /// <summary>
    /// Cyberpsychosis numbers (Defs/CyberpsychosisTuning.xml). The event table is intentionally empty
    /// until the trigger events are decided (cyberware_overview.md: "which events will be decided later").
    /// </summary>
    public class CyberpsychosisTuningDef : Def
    {
        public float triggerUsage = 0.8f;
        public float safeUsage = 0.5f;
        public List<CyberpsychosisEventChance> events = new List<CyberpsychosisEventChance>();

        /// <summary>MaxTac is called once a cyberpsycho has killed this many people in one episode.</summary>
        public int maxTacKills = 5;
        public PawnKindDef maxTacOperatorKind;
        public PawnKindDef maxTacLeaderKind;
        public IntRange maxTacSquadSize = new IntRange(3, 4);
        /// <summary>MaxTac gives up and leaves after this long on the ground.</summary>
        public int maxTacMaxTicks = 20000;
        public int maxTacBoardTicks = 1500;
    }

    public static class CyberpsychosisUtility
    {
        private static CyberpsychosisTuningDef Tuning => RPDefOf.RPC_CyberpsychosisTuning;

        public static bool IsImmune(Pawn pawn) => CyberUtility.HasDeathless(pawn) || IsMaxTac(pawn) || IsTraumaTeam(pawn);

        /// <summary>Trauma Team operators are heavily chromed but screened and stabilised: immune, like MaxTac.</summary>
        public static bool IsTraumaTeam(Pawn pawn) =>
            pawn?.Faction?.Ext()?.kind == CyberFactionKind.TraumaTeam
            || pawn?.kindDef?.defaultFactionDef?.GetModExtension<CyberFactionExtension>()?.kind == CyberFactionKind.TraumaTeam;

        /// <summary>MaxTac members (by faction, or by pawn kind if they've changed faction) are immune to cyberpsychosis.</summary>
        public static bool IsMaxTac(Pawn pawn) =>
            pawn?.Faction?.Ext()?.kind == CyberFactionKind.MaxTac
            || pawn?.kindDef?.defaultFactionDef?.GetModExtension<CyberFactionExtension>()?.kind == CyberFactionKind.MaxTac;

        /// <summary>A humanlike hurt by a cyberpsycho dying within this long counts as that psycho's kill.</summary>
        private const int KillCreditTicks = 30000;

        /// <summary>victim thingID -> (cyberpsycho, tick) of the last harm. Not saved: worst case a death right after loading isn't credited.</summary>
        private static readonly Dictionary<int, (Pawn psycho, int tick)> lastPsychoHarm = new Dictionary<int, (Pawn, int)>();

        /// <summary>From Patch_Thing_TakeDamage: remember which cyberpsycho last hurt a humanlike.</summary>
        public static void Notify_Harmed(Pawn attacker, Pawn victim)
        {
            if (attacker == victim || victim.RaceProps?.Humanlike != true || !InCyberpsychosis(attacker)) return;
            lastPsychoHarm[victim.thingIDNumber] = (attacker, Find.TickManager.TicksGame);
        }

        /// <summary>From Patch_Pawn_Kill: any humanlike death the psycho caused, directly or not.</summary>
        public static void Notify_Death(Pawn victim, Pawn instigator)
        {
            if (victim == null) return;
            // a death from held-back Painducer damage isn't anyone's kill (no MaxTac credit for the last psycho to hit them)
            if (CompCyberTracker.ApplyingDelayed)
            {
                lastPsychoHarm.Remove(victim.thingIDNumber);
                return;
            }
            Pawn killer = InCyberpsychosis(instigator) ? instigator : null;
            if (lastPsychoHarm.TryGetValue(victim.thingIDNumber, out var rec))
            {
                lastPsychoHarm.Remove(victim.thingIDNumber);
                if (killer == null && InCyberpsychosis(rec.psycho) && !rec.psycho.Dead
                    && Find.TickManager.TicksGame - rec.tick <= KillCreditTicks)
                    killer = rec.psycho;
            }
            if (killer != null) Notify_Kill(killer, victim);
        }

        /// <summary>A cyberpsycho's humanlike kills count towards calling MaxTac.</summary>
        public static void Notify_Kill(Pawn killer, Pawn victim)
        {
            if (killer == null || victim == null || killer == victim || victim.RaceProps?.Humanlike != true) return;
            if (!(killer.MentalState is MentalState_Cyberpsychosis ms) || ms is MentalState_HackedCyberpsychosis) return;
            ms.kills++;
            int need = Tuning?.maxTacKills ?? 5;
            Diag.Event("psychosis", $"{Diag.Describe(killer)} killed {Diag.Describe(victim)} ({ms.kills}/{need} before MaxTac)");
            if (ms.kills < need || ms.maxTacCalled) return;
            ms.maxTacCalled = true;
            GameComponent_RimPunk.Instance?.CallMaxTac(killer, "RPC_MaxTacReasonKills".Translate(ms.kills));
        }

        public static bool InCyberpsychosis(Pawn pawn) => pawn?.MentalStateDef == RPDefOf.RPC_Cyberpsychosis;

        /// <summary>
        /// The episode was wiped without PostEnd (Pawn_MindState.Reset: despawned, passed to the world, e.g. Trauma Team
        /// extraction), leaving the rage hediff behind. Removes it; <paramref name="addBurnout"/> gives the burnout
        /// PostEnd would have.
        /// </summary>
        public static void ClearOrphanedRage(Pawn pawn, bool addBurnout = false)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead || pawn.MentalState is MentalState_Cyberpsychosis) return;
            var rage = pawn.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_CyberpsychosisRage);
            if (rage != null)
            {
                pawn.health.RemoveHediff(rage);
                Diag.Event("psychosis", $"{Diag.Describe(pawn)}: leftover cyberpsychosis rage removed (episode ended without PostEnd)");
            }
            if (addBurnout && !HasBurnout(pawn)) pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisBurnout);
        }

        /// <summary>David's last stand wore off: the episode never recovers (Hediff_PermanentCyberpsychosis).</summary>
        public static bool IsPermanent(Pawn pawn) => pawn?.health?.hediffSet?.HasHediff(RPDefOf.RPC_CyberpsychosisPermanent) == true;

        public static bool HasBurnout(Pawn pawn) => pawn?.health?.hediffSet?.HasHediff(RPDefOf.RPC_CyberpsychosisBurnout) == true;

        /// <summary>Roll the chance for an event (seeded via Verse.Rand). Only possible at >= 80% usage.</summary>
        public static void Notify_Event(Pawn pawn, CyberpsychosisEvent ev)
        {
            var tuning = Tuning;
            if (tuning == null || tuning.events.NullOrEmpty()) return;
            if (pawn == null || pawn.Dead || !pawn.Spawned || InCyberpsychosis(pawn) || IsImmune(pawn)) return;
            var tracker = pawn.GetCyberTracker();
            if (tracker == null || !tracker.HasCyberware) return;
            float usage = tracker.UsageRatio;
            if (usage < tuning.triggerUsage) return;

            for (int i = 0; i < tuning.events.Count; i++)
            {
                var e = tuning.events[i];
                if (e.eventType != ev) continue;
                float chance = (e.chance + e.chancePerTenPercentOver * (usage - tuning.triggerUsage) * 10f)
                               * (RimPunkMod.Settings?.cyberpsychosisChanceMultiplier ?? 1f);
                if (Rand.Chance(chance)) TryStart(pawn, "RPC_CyberpsychosisReason_Event".Translate(ev.ToString()));
                return;
            }
        }

        /// <summary>Burnout: going above 50% usage triggers an episode immediately.</summary>
        public static void Notify_CapacityChanged(Pawn pawn, CompCyberTracker tracker)
        {
            if (pawn.Dead || InCyberpsychosis(pawn) || IsImmune(pawn)) return;
            if (HasBurnout(pawn) && tracker.UsageRatio > Tuning.safeUsage)
                TryStart(pawn, "RPC_CyberpsychosisReason_Burnout".Translate());
        }

        public static bool TryStart(Pawn pawn, string reason)
        {
            if (IsImmune(pawn)) return false;
            return ForceStart(pawn, reason);
        }

        /// <summary>No immunity check: David's last stand gets everyone.</summary>
        public static bool ForceStart(Pawn pawn, string reason)
        {
            if (pawn.Dead) return false;
            return pawn.mindState?.mentalStateHandler?.TryStartMentalState(RPDefOf.RPC_Cyberpsychosis, reason, forced: true, forceWake: true) == true;
        }
    }

    /// <summary>
    /// Berserk rage, except: it doesn't end when downed (MentalStateDef.recoverFromDowned = false),
    /// consciousness is doubled (RPC_CyberpsychosisRage hediff), and it ends only when cyberware usage
    /// drops below 50%. A downed cyberpsycho is not treated as hostile, so it can be rescued and operated on.
    /// </summary>
    public class MentalState_Cyberpsychosis : MentalState_Berserk
    {
        public override bool ForceHostileTo(Thing t) => !pawn.Downed;

        public override bool ForceHostileTo(Faction f) => !pawn.Downed;

        protected override bool CanEndBeforeMaxDurationNow => false;

        /// <summary>Humanlike kills this episode (MaxTac comes at maxTacKills).</summary>
        public int kills;
        public bool maxTacCalled;

        /// <summary>Gear dropped when downed (vanilla drops weapon + inventory), recovered on getting up (JobGiver_CyberpsychoRecoverGear).</summary>
        public Thing droppedWeapon;
        public List<ThingDefCountClass> droppedInventory = new List<ThingDefCountClass>();
        public IntVec3 droppedAt = IntVec3.Invalid;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref kills, "rpcKills");
            Scribe_Values.Look(ref maxTacCalled, "rpcMaxTacCalled");
            Scribe_References.Look(ref droppedWeapon, "rpcDroppedWeapon");
            Scribe_Collections.Look(ref droppedInventory, "rpcDroppedInventory", LookMode.Deep);
            Scribe_Values.Look(ref droppedAt, "rpcDroppedAt", IntVec3.Invalid);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) droppedInventory ??= new List<ThingDefCountClass>();
        }

        /// <summary>Called around MakeDowned: inventory snapshot taken before the drop, weapon read after it.</summary>
        public void Notify_GearDropped(ThingWithComps primaryBefore, List<ThingDefCountClass> inventoryBefore)
        {
            // vanilla's mindState.droppedWeapon came back null in live tests (2026-11-04), so track the weapon itself
            string weaponState = "none";
            if (primaryBefore != null)
            {
                if (pawn.equipment?.Primary == primaryBefore) weaponState = "still equipped";
                else if (primaryBefore.Destroyed) weaponState = "destroyed";
                else if (primaryBefore.Spawned) { weaponState = "on ground"; droppedWeapon = primaryBefore; }
                else weaponState = "held by " + (primaryBefore.ParentHolder?.GetType().Name ?? "nothing");
            }
            droppedWeapon ??= pawn.mindState?.droppedWeapon;
            Diag.Event("psychosis", $"{Diag.Describe(pawn)} downed: weapon {primaryBefore?.def.defName ?? "none"} -> {weaponState}");
            droppedAt = pawn.PositionHeld;
            droppedInventory.Clear();
            if (inventoryBefore != null && pawn.inventory != null)
                foreach (var e in inventoryBefore)
                {
                    // only what actually left the inventory (kept when downed in bed)
                    int left = e.count - pawn.inventory.innerContainer.TotalStackCountOfDef(e.thingDef);
                    if (left > 0) droppedInventory.Add(new ThingDefCountClass(e.thingDef, left));
                }
            Diag.Event("psychosis", $"{Diag.Describe(pawn)} downed: dropped weapon {droppedWeapon?.def.defName ?? "none"}, inventory [{string.Join(", ", droppedInventory.Select(d => d.thingDef.defName + " x" + d.count))}]");
        }

        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            Diag.Event("psychosis", $"{Diag.Describe(pawn)} started cyberpsychosis ({reason}), usage {pawn.GetCyberTracker()?.UsageRatio:P0}");
            if (!pawn.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisRage)) pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisRage);
            BarkUtility.Say(pawn, BarkKeys.PsychosisStart);
        }

        public override void PostEnd()
        {
            base.PostEnd();
            Diag.Event("psychosis", $"{Diag.Describe(pawn)} cyberpsychosis ended, usage {pawn.GetCyberTracker()?.UsageRatio:P0}");
            var rage = pawn.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_CyberpsychosisRage);
            if (rage != null) pawn.health.RemoveHediff(rage);
            // burnout is the scar of a break the pawn survived: added when the episode ends, not while it runs
            if (LeavesBurnout && !pawn.Dead && !pawn.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisBurnout)) pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisBurnout);
            BarkUtility.Say(pawn, BarkKeys.PsychosisEnd);
        }

        /// <summary>A real episode scars the pawn (burnout); a quickhack-induced one doesn't.</summary>
        protected virtual bool LeavesBurnout => true;

        public override void MentalStateTick(int delta)
        {
            // no time-based recovery (base would end it after maxTicksBeforeRecovery)
            if (!pawn.IsHashIntervalTick(30, delta)) return;
            age += 30;
            if (CyberpsychosisUtility.IsPermanent(pawn)) return;
            var tracker = pawn.GetCyberTracker();
            if (tracker == null || tracker.UsageRatio < RPDefOf.RPC_CyberpsychosisTuning.safeUsage || pawn.Dead)
                RecoverFromState();
        }
    }

    /// <summary>
    /// The Cyberpsychosis quickhack: a real cyberpsychotic rage (attacks anyone, doubled consciousness) that ends after
    /// the quickhack's duration, leaves no burnout and doesn't count kills towards MaxTac. t5: +25% duration for each
    /// other quickhack on the pawn when it starts.
    /// </summary>
    public class MentalState_HackedCyberpsychosis : MentalState_Cyberpsychosis
    {
        public int durationTicks = 3600;
        public float bonusPerQuickhack;
        private bool bonusApplied;

        protected override bool LeavesBurnout => false;

        public override void MentalStateTick(int delta)
        {
            if (!bonusApplied)
            {
                bonusApplied = true;
                int others = pawn.health.hediffSet.hediffs.Count(h => h is Hediff_Quickhack);
                durationTicks = Mathf.RoundToInt(durationTicks * (1f + bonusPerQuickhack * others));
            }
            if (!pawn.IsHashIntervalTick(30, delta)) return;
            age += 30;
            if (age >= durationTicks || pawn.Dead) RecoverFromState();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref durationTicks, "rpcDuration", 3600);
            Scribe_Values.Look(ref bonusPerQuickhack, "rpcBonusPerQh");
            Scribe_Values.Look(ref bonusApplied, "rpcBonusApplied");
        }
    }
}
