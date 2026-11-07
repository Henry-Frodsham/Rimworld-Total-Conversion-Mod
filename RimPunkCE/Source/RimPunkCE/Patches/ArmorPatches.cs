using CombatExtended;
using HarmonyLib;
using RimWorld;
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
            __result += ChromePlatingUtility.ArmorFor(pawn, stat, part);
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
    /// CE CompSuppressable.AddSuppression: berserk ignores suppression. That's the point of popping it when pinned
    /// (ContextualCombat), and a suppressed pawn would otherwise hunker instead of charging with its melee-only lock.
    /// </summary>
    [HarmonyPatch(typeof(CompSuppressable), nameof(CompSuppressable.AddSuppression))]
    public static class Patch_CompSuppressable_AddSuppression
    {
        public static bool Prefix(CompSuppressable __instance) => !(__instance.parent is Pawn p && ContextualCombat.IgnoresSuppression(p));
    }
}
