using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE.Patches
{
    /// <summary>
    /// CE ArmorUtilityCE.PartialStat(Pawn, StatDef, BodyPartRecord): the pawn's natural armor on one part. Implants on that
    /// part add to it (ChromePlatingUtility). CE only asks for parts in the CoveredByNaturalArmor group, which on a human is
    /// everything outside except the eyes.
    /// </summary>
    [HarmonyPatch(typeof(ArmorUtilityCE), nameof(ArmorUtilityCE.PartialStat), new[] { typeof(Pawn), typeof(StatDef), typeof(BodyPartRecord) })]
    public static class Patch_ArmorUtilityCE_PartialStat
    {
        public static void Postfix(Pawn pawn, StatDef stat, BodyPartRecord part, ref float __result)
        {
            if (part == null || pawn?.RaceProps?.Humanlike != true) return;
            // Pawn stat already includes whole-body plating; add only the per-part difference.
            __result += ChromePlatingUtility.ArmorFor(pawn, stat, part) - ChromePlatingUtility.ArmorFor(pawn, stat, null);
        }
    }

    /// <summary>
    /// CE ArmorUtilityCE.GetAfterArmorDamage: every hit passes here with its final part once armor is done, so this is
    /// where part health applies. Dividing the damage gives the part that many more effective hit points.
    /// </summary>
    [HarmonyPatch(typeof(ArmorUtilityCE), nameof(ArmorUtilityCE.GetAfterArmorDamage))]
    public static class Patch_ArmorUtilityCE_GetAfterArmorDamage
    {
        public static void Postfix(Pawn pawn, BodyPartRecord hitPart, ref DamageInfo __result)
        {
            if (__result.Amount <= 0f || pawn?.RaceProps?.Humanlike != true || !__result.Def.ExternalViolenceFor(pawn)) return;
            float factor = ChromePlatingUtility.HealthFactorFor(pawn, __result.HitPart ?? hitPart);
            if (factor <= 1f) return;
            __result.SetAmount(__result.Amount / factor);
            Diag.Count("plating-reduced-hit");
        }
    }

    /// <summary>
    /// HealthCardUtility.GetTooltip (private): the body part hover. Part health bonuses (plating, skeleton) work by dividing
    /// incoming damage, so vanilla's "30 / 30" never changed. Showing both numbers scaled by the same factor is the
    /// equivalent hit point view: (max - damage) * f out of max * f.
    /// </summary>
    [HarmonyPatch(typeof(HealthCardUtility), "GetTooltip")]
    public static class Patch_HealthCardUtility_GetTooltip
    {
        public static bool Prefix(Pawn pawn, BodyPartRecord part, ref string __result)
        {
            if (pawn?.RaceProps?.Humanlike != true || part == null) return true;
            float f = ChromePlatingUtility.HealthFactorFor(pawn, part)
                * Mathf.Max(0.1f, pawn.GetStatValue(RPStatDefOf.RPC_MaxHealthFactor));
            if (Mathf.Abs(f - 1f) < 0.005f) return true;
            var sb = new System.Text.StringBuilder();
            sb.Append(part.LabelCap + ": ");
            float cur = pawn.health.hediffSet.GetPartHealth(part);
            float max = part.def.GetMaxHealth(pawn);
            sb.AppendLine(" " + Mathf.CeilToInt(cur * f) + " / " + Mathf.CeilToInt(max * f));
            float eff = PawnCapacityUtility.CalculatePartEfficiency(pawn.health.hediffSet, part);
            if (eff != 1f) sb.AppendLine("Efficiency".Translate() + ": " + eff.ToStringPercent());
            __result = sb.ToString();
            return false;
        }
    }

    /// <summary>
    /// CE CompSuppressable.AddSuppression: berserk ignores suppression. That's the point of popping it when pinned
    /// (ContextualCombat), and a suppressed pawn would otherwise hunker instead of charging with its melee-only lock.
    /// </summary>
    [HarmonyPatch(typeof(CompSuppressable), nameof(CompSuppressable.AddSuppression))]
    public static class Patch_CompSuppressable_AddSuppression
    {
        public static bool Prefix(CompSuppressable __instance) => !(__instance.parent is Pawn p && ContextualCombat.IgnoresSuppression(p));
    }
}
