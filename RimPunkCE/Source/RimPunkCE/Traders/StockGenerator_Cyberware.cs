using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Trader stock of cyberware. Normal traders: T1-T2, capacity cost 0-20. Exotic traders: T3-Iconic, any
    /// capacity (Patches/Traders.xml). Tier is rolled (seeded via Verse.Rand) within both the trader's range
    /// and the item's own range; items whose range doesn't overlap the trader's are skipped.
    /// </summary>
    public class StockGenerator_Cyberware : StockGenerator
    {
        public CyberTier minTier = CyberTier.T1;
        public CyberTier maxTier = CyberTier.Iconic;
        public float maxCapacityCost = 9999f;

        private bool Allowed(CyberwareExtension ext) =>
            ext?.item != null && ext.capacityCost <= maxCapacityCost && ext.minTier <= maxTier && ext.maxTier >= minTier;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            var candidates = CyberwareDefCache.AllCyberware.Select(h => h.Ext()).Where(Allowed).ToList();
            if (candidates.Count == 0) yield break;
            int count = countRange.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                var ext = candidates.RandomElement();
                if (ext.item.techLevel > maxTechLevelGenerate) continue;
                var thing = ThingMaker.MakeThing(ext.item);
                var lo = ext.minTier > minTier ? ext.minTier : minTier;
                var hi = ext.maxTier < maxTier ? ext.maxTier : maxTier;
                thing.TryGetComp<CompCyberwareTier>()?.SetTier((CyberTier)Rand.RangeInclusive((int)lo, (int)hi));
                yield return thing;
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef) => Allowed(CyberwareDefCache.HediffFor(thingDef)?.Ext());
    }
}
