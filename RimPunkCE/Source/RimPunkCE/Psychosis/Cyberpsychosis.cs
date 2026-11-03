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
    }

    public static class CyberpsychosisUtility
    {
        private static CyberpsychosisTuningDef Tuning => RPDefOf.RPC_CyberpsychosisTuning;

        public static bool IsImmune(Pawn pawn) => CyberUtility.HasDeathless(pawn);

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

        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            if (!pawn.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisRage)) pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisRage);
            if (!pawn.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisBurnout)) pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisBurnout);
        }

        public override void PostEnd()
        {
            base.PostEnd();
            var rage = pawn.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_CyberpsychosisRage);
            if (rage != null) pawn.health.RemoveHediff(rage);
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
