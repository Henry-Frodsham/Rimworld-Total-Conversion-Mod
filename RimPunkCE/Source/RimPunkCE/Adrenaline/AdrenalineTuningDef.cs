using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Adrenaline numbers (RPC_AdrenalineTuning in Defs/Core.xml). The level itself lives on CompCyberTracker: it builds on
    /// damage dealt and taken (CyberEvents), bursts when a pawn enters combat, decays out of combat, is spent by sandevistan
    /// and berserk (CompProperties_AbilityCyberBuff.adrenalineCost) and gives the surge hediff above surgeFraction.
    /// </summary>
    public class AdrenalineTuningDef : Def
    {
        /// <summary>Gain per point of damage dealt to a hostile pawn.</summary>
        public float perDamageDealt = 0.6f;

        /// <summary>Gain per point of damage taken.</summary>
        public float perDamageTaken = 1f;

        /// <summary>Flat gain on entering combat: pays for one sandevistan before a shot lands.</summary>
        public float enteredCombatBurst = 25f;

        /// <summary>Lost per second out of combat, times RPC_AdrenalineDecay.</summary>
        public float decayPerSecond = 4f;

        /// <summary>No shooting or being shot at for this long = out of combat (terms.md: 10 s).</summary>
        public int outOfCombatTicks = 600;

        /// <summary>RPC_AdrenalineSurge is on the pawn from this fraction up.</summary>
        public float surgeFraction = 0.5f;
    }
}
