using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Dedicated plating supplies armour, capped after multipliers at 10 mm sharp / 25 MPa blunt per part.
    /// General implants keep their previous effective-health contribution but give no flat armour.
    /// Race armour and apparel remain separate. Colonists and NPCs use the same calculation.
    /// </summary>
    public class ChromePlatingTuningDef : Def
    {
        public TieredFloat partSharp;
        public TieredFloat partBlunt;
        /// <summary>Extra part health as a fraction (0.5 = +50%).</summary>
        public TieredFloat partHealth;
        public TieredFloat wholeBodyHealth;

        public List<CyberSlotDef> partSlots = new List<CyberSlotDef>();
        public List<CyberSlotDef> wholeBodySlots = new List<CyberSlotDef>();

        /// <summary>Part health never goes above this multiple (centipede: 4.32).</summary>
        public float maxHealthFactor = 4.3f;

        /// <summary>Implant contribution ceiling, including local plating and multipliers. Excludes race armour and apparel.</summary>
        public float maxPawnSharp = 10f;
        public float maxPawnBlunt = 25f;

        // pawn generation
        /// <summary>Pawns with at least this many implants can get armor implants added.</summary>
        public int aiMinChrome = 3;
        public float aiFirstArmorChance = 0.6f;
        public float aiSecondArmorChance = 0.25f;
        /// <summary>Added armor never takes a pawn above this capacity usage (forced-aptitude kinds like the rogue cyberpsycho ignore it).</summary>
        public float aiMaxUsage = 0.75f;
        public List<HediffDef> aiArmorImplants = new List<HediffDef>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            foreach (var v in new[] { partSharp, partBlunt, partHealth, wholeBodyHealth })
                if (v?.perTier == null) yield return "plating values need a perTier list (T1..Iconic)";
        }
    }

    /// <summary>
    /// Adds the capped whole-body implant contribution. CE's per-part postfix supplies only the local difference.
    /// </summary>
    public class StatPart_ChromeArmorCap : StatPart
    {
        public float cap;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is Pawn pawn && pawn.RaceProps?.Humanlike == true)
                val += ChromePlatingUtility.ArmorFor(pawn, parentStat, null);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (req.Thing is Pawn pawn && pawn.GetCyberTracker()?.HasCyberware == true) return "RPC_ChromeArmorCap".Translate(cap.ToString("0.#"));
            return null;
        }
    }

    /// <summary>Extra plating on any hediff. Fixed numbers: not tiered.</summary>
    public class PartPlatingExtension : DefModExtension
    {
        public float sharp;
        public float blunt;
        public float health;
    }

    public static class ChromePlatingUtility
    {
        private static ChromePlatingTuningDef tuning;

        public static ChromePlatingTuningDef Tuning => tuning ??= DefDatabase<ChromePlatingTuningDef>.GetNamedSilentFail("RPC_ChromePlatingTuning");

        private static float Tiered(TieredFloat v, CyberTier tier) => v?.Evaluate(tier, CyberTier.T1, CyberTier.Iconic) ?? 0f;

        /// <summary>The hediff's part, or one of its parents, is <paramref name="part"/>'s ancestor-or-self.</summary>
        private static bool Covers(BodyPartRecord implantPart, BodyPartRecord part)
        {
            for (var p = part; p != null; p = p.parent)
                if (p == implantPart) return true;
            return false;
        }

        /// <summary>Adds up what's protecting this part. <paramref name="part"/> null = whole-body sources only.</summary>
        public static void Totals(Pawn pawn, BodyPartRecord part, out float sharp, out float blunt, out float healthFactor)
        {
            sharp = 0f;
            blunt = 0f;
            float health = 0f;
            var t = Tuning;
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null || t == null)
            {
                healthFactor = 1f;
                return;
            }
            for (int i = 0; i < hediffs.Count; i++)
            {
                var h = hediffs[i];
                if (h is Hediff_Cyberware c && c.Ext != null)
                {
                    var slot = c.Ext.slot;
                    if (c.Part != null)
                    {
                        if (part != null && t.partSlots.Contains(slot) && Covers(c.Part, part))
                        {
                            sharp += Tiered(t.partSharp, c.tier);
                            blunt += Tiered(t.partBlunt, c.tier);
                            health += Tiered(t.partHealth, c.tier);
                        }
                    }
                    else if (t.wholeBodySlots.Contains(slot)) health += Tiered(t.wholeBodyHealth, c.tier);
                }
                var ext = h.def.GetModExtension<PartPlatingExtension>();
                if (ext != null && (h.Part == null || (part != null && Covers(h.Part, part))))
                {
                    sharp += ext.sharp;
                    blunt += ext.blunt;
                    health += ext.health;
                }
            }
            float sharpFactor = 1f, bluntFactor = 1f;
            foreach (var h in hediffs)
            {
                TieredEffects effects = null;
                CyberTier tier = CyberTier.T1, min = CyberTier.T1, max = CyberTier.Iconic;
                float scale = 1f;
                if (h is Hediff_Cyberware c && c.Ext != null)
                {
                    effects = c.Ext.effects; tier = c.tier; min = c.Ext.minTier; max = c.Ext.maxTier;
                    scale = (c.effectsDoubled ? 2f : 1f) * QualityBionicsCompat.Multiplier(c.Quality);
                }
                else if (h is Hediff_CyberActive a && a.Ext != null)
                { effects = a.Ext.effects; tier = a.tier; min = a.minTier; max = a.maxTier; scale = a.doubled ? 2f : 1f; }
                if (effects == null || (h.Part != null && (part == null || !Covers(h.Part, part)))) continue;
                if (effects.statOffsets != null) foreach (var m in effects.statOffsets)
                {
                    float value = m.value.Evaluate(tier, min, max) * scale;
                    if (m.stat == StatDefOf.ArmorRating_Sharp) sharp += value;
                    if (m.stat == StatDefOf.ArmorRating_Blunt) blunt += value;
                }
                if (effects.statFactors != null) foreach (var m in effects.statFactors)
                {
                    float value = 1f + (m.value.Evaluate(tier, min, max) - 1f) * scale;
                    if (m.stat == StatDefOf.ArmorRating_Sharp) sharpFactor *= value;
                    if (m.stat == StatDefOf.ArmorRating_Blunt) bluntFactor *= value;
                }
            }
            sharp = Mathf.Clamp(sharp * sharpFactor, 0f, t.maxPawnSharp);
            blunt = Mathf.Clamp(blunt * bluntFactor, 0f, t.maxPawnBlunt);
            healthFactor = Mathf.Min(t.maxHealthFactor, 1f + health);
        }

        public static float ArmorFor(Pawn pawn, StatDef stat, BodyPartRecord part)
        {
            if (stat != StatDefOf.ArmorRating_Sharp && stat != StatDefOf.ArmorRating_Blunt) return 0f;
            Totals(pawn, part, out float sharp, out float blunt, out _);
            return stat == StatDefOf.ArmorRating_Sharp ? sharp : blunt;
        }

        public static float HealthFactorFor(Pawn pawn, BodyPartRecord part)
        {
            Totals(pawn, part, out _, out _, out float factor);
            return factor;
        }

        public static void InstallArmorCaps()
        {
            var t = Tuning;
            if (t == null) return;
            foreach (var (stat, cap) in new[] { (StatDefOf.ArmorRating_Sharp, t.maxPawnSharp), (StatDefOf.ArmorRating_Blunt, t.maxPawnBlunt) })
            {
                stat.parts ??= new List<StatPart>();
                stat.parts.Add(new StatPart_ChromeArmorCap { parentStat = stat, cap = cap });
            }
        }

        /// <summary>Line for the implant's tooltip.</summary>
        public static string TipFor(Hediff_Cyberware c)
        {
            var t = Tuning;
            if (t == null || c.Ext == null) return null;
            if (c.Part != null && t.partSlots.Contains(c.Ext.slot))
                return "RPC_PlatingTip".Translate(Tiered(t.partSharp, c.tier).ToString("0.#"), Tiered(t.partBlunt, c.tier).ToString("0.#"), Tiered(t.partHealth, c.tier).ToStringPercent("0"));
            if (c.Part == null && t.wholeBodySlots.Contains(c.Ext.slot))
                return "RPC_PlatingTipWholeBody".Translate(Tiered(t.wholeBodyHealth, c.tier).ToStringPercent("0"));
            return null;
        }

        /// <summary>Pawn generation: chromed NPCs get armor implants too. Tiers stay inside the loadout's range.</summary>
        public static void AddGeneratedArmor(Pawn pawn, CyberwareLoadoutExtension loadout)
        {
            var t = Tuning;
            var tracker = pawn.GetCyberTracker();
            if (t == null || tracker == null || t.aiArmorImplants.NullOrEmpty()) return;
            if (tracker.Cyberware.Count < t.aiMinChrome) return;
            bool ignoreUsage = loadout.forceAptitudeDegree != -99;
            foreach (float chance in new[] { t.aiFirstArmorChance, t.aiSecondArmorChance })
            {
                if (!Rand.Chance(chance)) continue;
                var candidates = t.aiArmorImplants.Where(d => d.Ext() != null
                                                              && d.Ext().minTier <= loadout.maxTier
                                                              && (ignoreUsage || (tracker.CapacityUsed + d.Ext().capacityCost) / tracker.CapacityMax <= t.aiMaxUsage))
                    .InRandomOrder().ToList();
                foreach (var d in candidates)
                    if (PawnLoadoutUtility.TryInstall(pawn, d, loadout.minTier, loadout.maxTier)) break;
            }
        }
    }
}
