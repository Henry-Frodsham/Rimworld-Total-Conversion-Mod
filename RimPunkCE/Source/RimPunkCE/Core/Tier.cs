using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>Cyberware quality tiers (prompts/cyberware/layout.md). Stored as an int in saves.</summary>
    public enum CyberTier : byte
    {
        T1 = 0,
        T2 = 1,
        T3 = 2,
        T4 = 3,
        T5 = 4,
        Iconic = 5,
    }

    public static class TierUtility
    {
        public const int Count = 6;

        public static string Label(this CyberTier tier) => ("RPC_Tier_" + tier).Translate();

        public static CyberTier Clamp(CyberTier tier, CyberTier min, CyberTier max)
        {
            if (tier < min) return min;
            if (tier > max) return max;
            return tier;
        }

        /// <summary>
        /// Position of <paramref name="tier"/> inside [min, max] as 0..1.
        /// "+5-+10% across tiers 1-iconic" means T1 = 5, Iconic = 10, linear in between (layout.md).
        /// </summary>
        public static float Fraction(CyberTier tier, CyberTier min, CyberTier max)
        {
            if (max <= min) return 1f;
            return Mathf.Clamp01((float)(tier - min) / (max - min));
        }
    }

    /// <summary>
    /// A number that scales with tier. XML: &lt;min&gt;/&lt;max&gt; (linear across the item's tier range),
    /// or &lt;perTier&gt; with exactly 6 entries (T1..Iconic) for irregular values such as "+5/8/12".
    /// </summary>
    public class TieredFloat
    {
        public float min;
        public float max;
        public List<float> perTier;

        public TieredFloat() { }

        public TieredFloat(float min, float max)
        {
            this.min = min;
            this.max = max;
        }

        public float Evaluate(CyberTier tier, CyberTier minTier, CyberTier maxTier)
        {
            if (perTier != null && perTier.Count == TierUtility.Count) return perTier[(int)tier];
            return Mathf.Lerp(min, max, TierUtility.Fraction(tier, minTier, maxTier));
        }

        public IEnumerable<string> ConfigErrors(string owner)
        {
            if (perTier != null && perTier.Count != TierUtility.Count)
                yield return $"{owner}: perTier must have exactly {TierUtility.Count} entries (T1..Iconic), has {perTier.Count}";
        }
    }

    public class TieredStatModifier
    {
        public StatDef stat;
        public TieredFloat value;

        public IEnumerable<string> ConfigErrors(string owner)
        {
            if (stat == null) yield return owner + ": tiered stat modifier with null stat";
            if (value == null) yield return owner + ": tiered stat modifier for " + stat + " has no value";
            else foreach (var e in value.ConfigErrors(owner)) yield return e;
        }
    }

    public class TieredCapacityModifier
    {
        public PawnCapacityDef capacity;
        public TieredFloat offset;
        public TieredFloat postFactor;
    }

    /// <summary>A named tiered number read by C# (durations, cooldowns, chances...).</summary>
    public class TieredNamedValue
    {
        public string key;
        public TieredFloat value;
    }
}
