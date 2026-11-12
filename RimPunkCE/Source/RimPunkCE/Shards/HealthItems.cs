using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Healing for the cycle 4 health items (Defs/Drugs/HealthItems.xml): Bounce Back injectors and MaxDOC inhalers, five tiers each.
    /// "N% of a part" is N% of that part's maximum health, spent on the injuries that sit on it, worst first.
    /// </summary>
    public static class PartHealing
    {
        /// <summary>Heals every injured part by up to <paramref name="fraction"/> of its max health. Returns the health restored.</summary>
        public static float HealPartFraction(Pawn pawn, float fraction)
        {
            if (fraction <= 0f || pawn?.health?.hediffSet == null) return 0f;
            var injuries = new List<Hediff_Injury>();
            pawn.health.hediffSet.GetHediffs(ref injuries, h => h.Part != null && h.Severity > 0f && (h.CanHealNaturally() || h.CanHealFromTending()));
            float total = 0f;
            foreach (var group in injuries.GroupBy(i => i.Part))
            {
                float budget = group.Key.def.GetMaxHealth(pawn) * fraction;
                foreach (var inj in group.OrderByDescending(i => i.Severity))
                {
                    if (budget <= 0f) break;
                    float take = Mathf.Min(budget, inj.Severity);
                    inj.Heal(take);
                    budget -= take;
                    total += take;
                }
            }
            return total;
        }

        /// <summary>Stops the bleeding on every wound by tending it at <paramref name="quality"/>.</summary>
        public static int SealWounds(Pawn pawn, float quality)
        {
            if (pawn?.health?.hediffSet == null) return 0;
            int sealedCount = 0;
            foreach (var h in pawn.health.hediffSet.hediffs.ToList())
            {
                if (!h.Bleeding || !h.TendableNow(true)) continue;
                h.Tended(quality, quality);
                sealedCount++;
            }
            return sealedCount;
        }
    }

    /// <summary>MaxDOC: instantly restores a fraction of every body part's health.</summary>
    public class IngestionOutcomeDoer_HealParts : IngestionOutcomeDoer
    {
        public float fraction;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            float healed = PartHealing.HealPartFraction(pawn, fraction * ingestedCount);
            Diag.Event("health", $"{Diag.Describe(pawn)} healed {healed:0.#} HP from {ingested?.def.defName}");
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(ThingDef parentDef)
        {
            yield return new StatDrawEntry(StatCategoryDefOf.Basics, "RPC_HealthItem_Instant".Translate(), fraction.ToStringPercent(),
                "RPC_HealthItem_InstantDesc".Translate(fraction.ToStringPercent()), 4000);
        }
    }

    /// <summary>Seals (tends) every bleeding wound.</summary>
    public class IngestionOutcomeDoer_SealWounds : IngestionOutcomeDoer
    {
        public float quality = 0.5f;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            int n = PartHealing.SealWounds(pawn, quality);
            Diag.Event("health", $"{Diag.Describe(pawn)} sealed {n} wounds with {ingested?.def.defName}");
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(ThingDef parentDef)
        {
            yield return new StatDrawEntry(StatCategoryDefOf.Basics, "RPC_HealthItem_Seal".Translate(), quality.ToStringPercent(),
                "RPC_HealthItem_SealDesc".Translate(), 3990);
        }
    }

    /// <summary>Bounce Back: a rush of adrenaline (RimPunk adrenaline, a fraction of the maximum).</summary>
    public class IngestionOutcomeDoer_Adrenaline : IngestionOutcomeDoer
    {
        public float fraction;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount) =>
            pawn.GetCyberTracker()?.GainAdrenalineFraction(fraction);

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(ThingDef parentDef)
        {
            yield return new StatDrawEntry(StatCategoryDefOf.Basics, "RPC_HealthItem_Adrenaline".Translate(), fraction.ToStringPercent(),
                "RPC_HealthItem_AdrenalineDesc".Translate(), 3980);
        }
    }

    /// <summary>Bounce Back's regeneration: heals <see cref="totalFraction"/> of every part's max health spread over <see cref="durationTicks"/>.</summary>
    public class HediffCompProperties_PartRegen : HediffCompProperties
    {
        public float totalFraction;
        public int durationTicks = 900;
        public HediffCompProperties_PartRegen() => compClass = typeof(HediffComp_PartRegen);
    }

    public class HediffComp_PartRegen : HediffComp
    {
        private HediffCompProperties_PartRegen Props => (HediffCompProperties_PartRegen)props;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            var pawn = Pawn;
            if (pawn == null || pawn.Dead) return;
            PartHealing.HealPartFraction(pawn, Props.totalFraction * delta / Mathf.Max(1, Props.durationTicks));
        }

        public override string CompTipStringExtra => "RPC_HealthItem_RegenTip".Translate(Props.totalFraction.ToStringPercent());
    }
}
