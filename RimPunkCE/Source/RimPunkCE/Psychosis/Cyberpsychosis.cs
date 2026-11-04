using System.Collections.Generic;
using RimWorld;
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

        public static bool IsImmune(Pawn pawn) => CyberUtility.HasDeathless(pawn) || IsMaxTac(pawn);

        /// <summary>MaxTac members (by faction, or by pawn kind if they've changed faction) are immune to cyberpsychosis.</summary>
        public static bool IsMaxTac(Pawn pawn) =>
            pawn?.Faction?.Ext()?.kind == CyberFactionKind.MaxTac
            || pawn?.kindDef?.defaultFactionDef?.GetModExtension<CyberFactionExtension>()?.kind == CyberFactionKind.MaxTac;

        /// <summary>From Patch_Pawn_Kill: a cyberpsycho's humanlike kills count towards calling MaxTac.</summary>
        public static void Notify_Kill(Pawn killer, Pawn victim)
        {
            if (killer == null || victim == null || killer == victim || victim.RaceProps?.Humanlike != true) return;
            if (!(killer.MentalState is MentalState_Cyberpsychosis ms)) return;
            ms.kills++;
            int need = Tuning?.maxTacKills ?? 5;
            Diag.Event("psychosis", $"{Diag.Describe(killer)} killed {Diag.Describe(victim)} ({ms.kills}/{need} before MaxTac)");
            if (ms.kills < need || ms.maxTacCalled) return;
            ms.maxTacCalled = true;
            GameComponent_RimPunk.Instance?.CallMaxTac(killer, "RPC_MaxTacReasonKills".Translate(ms.kills));
        }

        public static bool InCyberpsychosis(Pawn pawn) => pawn?.MentalStateDef == RPDefOf.RPC_Cyberpsychosis;

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
            if (IsImmune(pawn) || pawn.Dead) return false;
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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref kills, "rpcKills");
            Scribe_Values.Look(ref maxTacCalled, "rpcMaxTacCalled");
        }

        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            Diag.Event("psychosis", $"{Diag.Describe(pawn)} started cyberpsychosis ({reason}), usage {pawn.GetCyberTracker()?.UsageRatio:P0}");
            if (!pawn.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisRage)) pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisRage);
        }

        public override void PostEnd()
        {
            base.PostEnd();
            Diag.Event("psychosis", $"{Diag.Describe(pawn)} cyberpsychosis ended, usage {pawn.GetCyberTracker()?.UsageRatio:P0}");
            var rage = pawn.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_CyberpsychosisRage);
            if (rage != null) pawn.health.RemoveHediff(rage);
            // burnout is the scar of a break the pawn survived: added when the episode ends, not while it runs
            if (!pawn.Dead && !pawn.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisBurnout)) pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisBurnout);
        }

        public override void MentalStateTick(int delta)
        {
            // no time-based recovery (base would end it after maxTicksBeforeRecovery)
            if (!pawn.IsHashIntervalTick(30, delta)) return;
            age += 30;
            var tracker = pawn.GetCyberTracker();
            if (tracker == null || tracker.UsageRatio < RPDefOf.RPC_CyberpsychosisTuning.safeUsage || pawn.Dead)
                RecoverFromState();
        }
    }
}
