using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Stat / capacity effects whose values scale with tier. Turned into a vanilla <see cref="HediffStage"/>
    /// per tier, so the normal stat and capacity systems pick them up with no extra patches.
    /// </summary>
    public class TieredEffects
    {
        public List<TieredStatModifier> statOffsets;
        public List<TieredStatModifier> statFactors;
        public List<TieredCapacityModifier> capMods;
        public TieredFloat painFactor;
        public TieredFloat painOffset;

        public HediffStage BuildStage(CyberTier tier, CyberTier minTier, CyberTier maxTier, float effectMultiplier, HediffStage template)
        {
            var stage = new HediffStage
            {
                becomeVisible = template?.becomeVisible ?? true,
                label = template?.label,
            };
            if (template?.capMods != null) stage.capMods.AddRange(template.capMods);

            if (!statOffsets.NullOrEmpty())
            {
                stage.statOffsets = new List<StatModifier>();
                foreach (var m in statOffsets)
                    stage.statOffsets.Add(new StatModifier { stat = m.stat, value = m.value.Evaluate(tier, minTier, maxTier) * effectMultiplier });
            }
            if (template?.statOffsets != null)
            {
                stage.statOffsets ??= new List<StatModifier>();
                stage.statOffsets.AddRange(template.statOffsets);
            }

            if (!statFactors.NullOrEmpty())
            {
                stage.statFactors = new List<StatModifier>();
                foreach (var m in statFactors)
                {
                    float v = m.value.Evaluate(tier, minTier, maxTier);
                    // doubling a factor doubles its deviation from 1 (x1.2 -> x1.4)
                    stage.statFactors.Add(new StatModifier { stat = m.stat, value = 1f + (v - 1f) * effectMultiplier });
                }
            }
            if (template?.statFactors != null)
            {
                stage.statFactors ??= new List<StatModifier>();
                stage.statFactors.AddRange(template.statFactors);
            }

            if (!capMods.NullOrEmpty())
            {
                foreach (var c in capMods)
                {
                    var mod = new PawnCapacityModifier { capacity = c.capacity };
                    if (c.offset != null) mod.offset = c.offset.Evaluate(tier, minTier, maxTier) * effectMultiplier;
                    if (c.postFactor != null) mod.postFactor = 1f + (c.postFactor.Evaluate(tier, minTier, maxTier) - 1f) * effectMultiplier;
                    stage.capMods.Add(mod);
                }
            }

            stage.painFactor = painFactor != null ? painFactor.Evaluate(tier, minTier, maxTier) : (template?.painFactor ?? 1f);
            stage.painOffset = painOffset != null ? painOffset.Evaluate(tier, minTier, maxTier) * effectMultiplier : (template?.painOffset ?? 0f);
            return stage;
        }

        public IEnumerable<string> ConfigErrors(string owner)
        {
            if (statOffsets != null) foreach (var m in statOffsets) foreach (var e in m.ConfigErrors(owner)) yield return e;
            if (statFactors != null) foreach (var m in statFactors) foreach (var e in m.ConfigErrors(owner)) yield return e;
            if (capMods != null)
                foreach (var c in capMods)
                    if (c.capacity == null) yield return owner + ": capMod with null capacity";
        }
    }

    /// <summary>Caches one built stage per (tier, multiplier) so CurStage never allocates during play.</summary>
    public class TieredStageCache
    {
        private const int Qualities = 7;
        private readonly HediffStage[] normal = new HediffStage[TierUtility.Count * Qualities];
        private readonly HediffStage[] doubled = new HediffStage[TierUtility.Count * Qualities];

        /// <summary>quality scales the effects (Quality Bionics compat, see <see cref="QualityBionicsCompat"/>); Normal = unchanged.</summary>
        public HediffStage Get(TieredEffects effects, CyberTier tier, CyberTier minTier, CyberTier maxTier, bool isDoubled, HediffStage template, QualityCategory quality = QualityCategory.Normal)
        {
            var arr = isDoubled ? doubled : normal;
            int i = (int)tier * Qualities + (int)quality;
            return arr[i] ??= effects == null
                ? template ?? new HediffStage()
                : effects.BuildStage(tier, minTier, maxTier, (isDoubled ? 2f : 1f) * QualityBionicsCompat.Multiplier(quality), template);
        }

        public void Clear()
        {
            for (int i = 0; i < normal.Length; i++) normal[i] = doubled[i] = null;
        }
    }
}
