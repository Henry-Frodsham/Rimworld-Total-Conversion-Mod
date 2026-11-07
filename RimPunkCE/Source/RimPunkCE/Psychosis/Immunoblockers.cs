using RimWorld.Planet;
using Verse;

namespace RimPunkCE
{
    /// <summary>Numbers for one immunoblocker hediff (Defs/Drugs/Immunoblockers.xml).</summary>
    public class ImmunoblockerExtension : DefModExtension
    {
        public float capacityBonus;

        /// <summary>David's last stand: permanent cyberpsychosis the moment the hediff ends.</summary>
        public bool psychosisOnEnd;
    }

    /// <summary>
    /// A dose of temporary cyberware capacity; the clock is a HediffComp_Disappears (a repeat dose merges and restarts it).
    /// The bonus is applied by StatPart_CyberwareCapacity; it's the only thing that raises a gonk's capacity.
    /// </summary>
    public class Hediff_Immunoblocker : HediffWithComps
    {
        private ImmunoblockerExtension Ext => def.GetModExtension<ImmunoblockerExtension>();

        public float CapacityBonus => Ext?.capacityBonus ?? 0f;

        public override string TipStringExtra =>
            base.TipStringExtra.TrimEndNewlines() + "\n" + "RPC_ImmunoblockerTip".Translate(CapacityBonus);

        public override void PostRemoved()
        {
            base.PostRemoved();
            if (pawn == null || pawn.Dead) return;
            if (Ext?.psychosisOnEnd == true)
            {
                if (!CyberpsychosisUtility.IsPermanent(pawn)) pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisPermanent);
                Hediff_PermanentCyberpsychosis.Enforce(pawn);
                return;
            }
            // other world pawns aren't simulated: nobody would see or stop the episode
            if (!Hediff_PermanentCyberpsychosis.Simulated(pawn)) return;
            // capacity just dropped: burnout's 50% limit applies right away
            var tracker = pawn.GetCyberTracker();
            if (tracker != null) CyberpsychosisUtility.Notify_CapacityChanged(pawn, tracker);
        }
    }

    /// <summary>
    /// The source of truth for David's last stand: MentalState_Cyberpsychosis won't recover while this is on the pawn, and
    /// anything that still clears the episode (despawning, another mental state, a calming psycast) gets it restarted.
    /// Keeps the normal RPC_Cyberpsychosis state, so fight AI, MaxTac kill credit and the rage hediff all work as usual.
    /// </summary>
    public class Hediff_PermanentCyberpsychosis : HediffWithComps
    {
        public override void PostTickInterval(int delta)
        {
            base.PostTickInterval(delta);
            if (pawn.IsHashIntervalTick(60, delta)) Enforce(pawn);
        }

        public static bool Simulated(Pawn pawn) => pawn.SpawnedOrAnyParentSpawned || pawn.IsCaravanMember();

        public static void Enforce(Pawn pawn)
        {
            if (pawn.Dead || !Simulated(pawn) || CyberpsychosisUtility.InCyberpsychosis(pawn)) return;
            bool started = CyberpsychosisUtility.ForceStart(pawn, "RPC_CyberpsychosisReason_LastStand".Translate());
            Diag.Event("psychosis", $"{Diag.Describe(pawn)}: permanent cyberpsychosis enforced, started: {started}");
        }
    }

    public static class ImmunoblockerUtility
    {
        /// <summary>Doses don't stack across drugs: only the strongest active one counts.</summary>
        public static float CapacityBonus(Pawn pawn)
        {
            var hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null) return 0f;
            float best = 0f;
            for (int i = 0; i < hediffs.Count; i++)
                if (hediffs[i] is Hediff_Immunoblocker b && b.CapacityBonus > best) best = b.CapacityBonus;
            return best;
        }
    }
}
