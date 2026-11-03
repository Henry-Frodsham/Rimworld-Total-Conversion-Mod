using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimPunkCE
{
    public class CompProperties_CyberwareTier : CompProperties
    {
        public CompProperties_CyberwareTier() => compClass = typeof(CompCyberwareTier);
    }

    /// <summary>
    /// The tier of a cyberware item. Crafted items are the minimum tier; trader stock rolls a tier
    /// (seeded) from the trader's <see cref="StockGenerator_Cyberware"/> range.
    /// </summary>
    public class CompCyberwareTier : ThingComp
    {
        public CyberTier tier = CyberTier.T1;
        private bool initialized;

        public CyberwareExtension Ext => CyberwareDefCache.HediffFor(parent.def)?.Ext();

        public override void PostPostMake()
        {
            base.PostPostMake();
            if (!initialized) SetTier(Ext?.minTier ?? CyberTier.T1);
        }

        public void SetTier(CyberTier t)
        {
            var ext = Ext;
            tier = ext != null ? TierUtility.Clamp(t, ext.minTier, ext.maxTier) : t;
            initialized = true;
        }

        public override string TransformLabel(string label) => label + " (" + tier.Label() + ")";

        public override bool AllowStackWith(Thing other) =>
            other.TryGetComp<CompCyberwareTier>() is CompCyberwareTier o && o.tier == tier;

        public override void PostSplitOff(Thing piece)
        {
            base.PostSplitOff(piece);
            piece.TryGetComp<CompCyberwareTier>()?.SetTier(tier);
        }

        public override string CompInspectStringExtra()
        {
            var ext = Ext;
            if (ext == null) return null;
            return "RPC_CapacityCost".Translate(ext.capacityCost.ToString("0.#")) + "\n" +
                   "RPC_TierRange".Translate(ext.minTier.Label(), ext.maxTier.Label());
        }

        public override float GetStatFactor(StatDef stat)
        {
            // higher tiers are worth more: x1 at min tier, doubling per tier (matches upgrade cost doubling)
            if (stat == StatDefOf.MarketValue)
            {
                var ext = Ext;
                if (ext != null) return 1 << (int)(tier - ext.minTier);
            }
            return 1f;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref tier, "tier", CyberTier.T1);
            Scribe_Values.Look(ref initialized, "tierInitialized");
        }
    }
}
