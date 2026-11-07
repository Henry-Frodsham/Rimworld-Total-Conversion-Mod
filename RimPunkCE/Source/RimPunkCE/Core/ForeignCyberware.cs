using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Optional, on another mod's HediffDef (via a patch): exact cyberware capacity cost, skipping the estimate.
    /// Also makes RimPunk count a hediff it wouldn't treat as an implant on its own.
    /// </summary>
    public class ForeignCyberwareCost : DefModExtension
    {
        public float cost = -1f;
    }

    /// <summary>
    /// Other mods' implants (and vanilla bionics) use cyberware capacity too. Their cost is estimated from what they do,
    /// on the same scale as RimPunk cyberware (base capacity 50-150; a bionic-grade arm ~8, an OS ~44):
    ///   2 for any chrome
    ///   + 20 per +1.0 part efficiency above 100%          (bionic arm 125% -> +5)
    ///   + 30 per +1.0 positive capacity offset / factor  (consciousness +10% -> +3)
    ///   + 15 per +1.0 of a buff relative to the stat's base (stat offsets / factors, direction-aware)
    ///   + (1 - painFactor) * 10, +3 per disease immunity
    ///   + melee attacks: power * 0.3 each, + 8 per ranged verb, + 8 per granted ability
    /// Drawbacks never lower the cost. Clamped to 1..50. Cached per def + stage; each new def is logged once to
    /// diag.log with its breakdown for tuning.
    /// </summary>
    public static class ForeignCyberwareUtility
    {
        private const float MaxCost = 50f;

        private static readonly Dictionary<(HediffDef, int), float> cache = new Dictionary<(HediffDef, int), float>();
        private static readonly HashSet<HediffDef> logged = new HashSet<HediffDef>();

        // stats where a lower value is the buff (vanilla has no direction flag)
        private static readonly HashSet<string> LowerIsBetter = new HashSet<string>
        {
            "AimingDelayFactor", "MentalBreakThreshold", "RestFallRateFactor", "HungerRateMultiplier", "Flammability",
            "FoodPoisonChanceFixedHuman", "CertaintyLossFactor",
        };

        public static bool IsForeignImplant(Hediff h)
        {
            if (h == null || h is Hediff_Cyberware || h.def == null) return false;
            if (h.def.HasModExtension<ForeignCyberwareCost>()) return true;
            if (h is Hediff_Psylink) return false;          // psychic, not chrome
            if (h.def.organicAddedBodypart) return false;   // transplants, fleshmass
            return h is Hediff_AddedPart || h is Hediff_Implant || h.def.countsAsAddedPartOrImplant;
        }

        public static float CostOf(Hediff h)
        {
            var key = (h.def, h.CurStageIndex);
            if (cache.TryGetValue(key, out float c)) return c;
            var sb = Diag.Enabled && !logged.Contains(h.def) ? new StringBuilder() : null;
            c = Compute(h.def, h.CurStage, sb);
            cache[key] = c;
            if (sb != null)
            {
                logged.Add(h.def);
                Diag.Event("capacity", $"foreign implant {h.def.defName} ({h.def.modContentPack?.Name ?? "?"}) costs {c:0.#}: {sb}");
            }
            return c;
        }

        /// <summary>Dev tools: estimate for a def at its first stage, with the breakdown.</summary>
        public static float CostOfDef(HediffDef def, out string breakdown)
        {
            var sb = new StringBuilder();
            float c = Compute(def, def.stages?.FirstOrDefault(), sb);
            breakdown = sb.ToString();
            return c;
        }

        /// <summary>Def-level version of IsForeignImplant (no hediff instance), for listing.</summary>
        public static bool IsForeignImplantDef(HediffDef d)
        {
            if (d == null || typeof(Hediff_Cyberware).IsAssignableFrom(d.hediffClass)) return false;
            if (d.HasModExtension<ForeignCyberwareCost>()) return true;
            if (typeof(Hediff_Psylink).IsAssignableFrom(d.hediffClass) || d.organicAddedBodypart) return false;
            return typeof(Hediff_AddedPart).IsAssignableFrom(d.hediffClass) || typeof(Hediff_Implant).IsAssignableFrom(d.hediffClass) || d.countsAsAddedPartOrImplant;
        }

        private static float Compute(HediffDef def, HediffStage stage, StringBuilder why)
        {
            var ext = def.GetModExtension<ForeignCyberwareCost>();
            if (ext != null && ext.cost >= 0f)
            {
                why?.Append("set by ForeignCyberwareCost");
                return ext.cost;
            }

            float c = 2f;
            why?.Append("base 2");
            void Add(string what, float v)
            {
                if (v <= 0.05f) return;
                c += v;
                why?.Append($", {what} +{v:0.#}");
            }

            var ap = def.addedPartProps;
            if (ap != null && ap.partEfficiency > 1f) Add($"part {ap.partEfficiency:P0}", (ap.partEfficiency - 1f) * 20f);

            if (stage != null)
            {
                if (stage.capMods != null)
                    foreach (var cm in stage.capMods)
                    {
                        if (cm?.capacity == null) continue;
                        if (cm.offset > 0f) Add(cm.capacity.defName, cm.offset * 30f);
                        if (cm.postFactor > 1f) Add(cm.capacity.defName + " x", (cm.postFactor - 1f) * 30f);
                    }
                if (stage.statOffsets != null)
                    foreach (var so in stage.statOffsets)
                    {
                        if (so?.stat == null) continue;
                        float buff = LowerIsBetter.Contains(so.stat.defName) ? -so.value : so.value;
                        if (buff > 0f) Add(so.stat.defName, buff / Mathf.Max(Mathf.Abs(so.stat.defaultBaseValue), 1f) * 15f);
                    }
                if (stage.statFactors != null)
                    foreach (var sf in stage.statFactors)
                    {
                        if (sf?.stat == null) continue;
                        float buff = LowerIsBetter.Contains(sf.stat.defName) ? 1f - sf.value : sf.value - 1f;
                        if (buff > 0f) Add(sf.stat.defName + " x", buff * 15f);
                    }
                if (stage.painFactor < 1f) Add("pain", (1f - stage.painFactor) * 10f);
                if (stage.makeImmuneTo != null) Add("immunities", stage.makeImmuneTo.Count * 3f);
            }

            if (def.comps != null)
                foreach (var cp in def.comps)
                {
                    if (!(cp is HediffCompProperties_VerbGiver vg)) continue;
                    if (vg.tools != null) foreach (var t in vg.tools) if (t != null) Add("melee " + t.label, t.power * 0.3f);
                    if (vg.verbs != null) Add("ranged verbs", vg.verbs.Count * 8f);
                }
            if (def.abilities != null) Add("abilities", def.abilities.Count * 8f);

            return Mathf.Round(Mathf.Clamp(c, 1f, MaxCost) * 10f) / 10f;
        }
    }
}
