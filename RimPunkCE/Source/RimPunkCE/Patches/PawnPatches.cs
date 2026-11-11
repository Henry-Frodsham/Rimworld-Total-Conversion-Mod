using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimPunkCE.Patches
{
    /// <summary>PawnGenerator.GenerateTraits (private): every humanlike gets a capacity aptitude trait; Netrunner rolled independently.</summary>
    [HarmonyPatch(typeof(PawnGenerator), "GenerateTraits")]
    public static class Patch_PawnGenerator_GenerateTraits
    {
        public static void Postfix(Pawn pawn) => AptitudeUtility.OnPawnGenerated(pawn);
    }

    /// <summary>
    /// HealthAIUtility.WantsToBeRescued: vanilla refuses pawns in a mental state. A downed cyberpsycho must be
    /// rescuable so doctors can remove cyberware until usage is below 50% (the only way to end the episode).
    /// </summary>
    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.WantsToBeRescued))]
    public static class Patch_HealthAIUtility_WantsToBeRescued
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result && pawn.Downed && !pawn.Dead && CyberpsychosisUtility.InCyberpsychosis(pawn)) __result = true;
        }
    }

    /// <summary>HediffSet.PainTotal: a pawn in cyberpsychosis feels no pain (no pain shock, no pain-driven downing or slowdown).</summary>
    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.PainTotal), MethodType.Getter)]
    public static class Patch_HediffSet_PainTotal
    {
        public static void Postfix(HediffSet __instance, ref float __result)
        {
            if (__result > 0f && CyberpsychosisUtility.InCyberpsychosis(__instance.pawn)) __result = 0f;
        }
    }

    /// <summary>
    /// MedicalRecipesUtility.SpawnThingsFromHediffs: our cyberware hediffs have no spawnThingOnRemoved
    /// (vanilla would spawn a tierless item); spawn the item with its tier instead. Covers part removal and
    /// replacing an arm with a bionic one.
    /// </summary>
    [HarmonyPatch(typeof(MedicalRecipesUtility), nameof(MedicalRecipesUtility.SpawnThingsFromHediffs))]
    public static class Patch_MedicalRecipesUtility_SpawnThingsFromHediffs
    {
        public static void Prefix(Pawn pawn, BodyPartRecord part, IntVec3 pos, Map map)
        {
            if (pawn?.health?.hediffSet == null || part == null || map == null) return;
            if (!pawn.health.hediffSet.GetNotMissingParts().Contains(part)) return;
            foreach (var c in pawn.InstalledCyberware())
                if (c.Part == part) // vanilla recurses into child parts itself, so only this part
                    CyberSurgeryUtility.SpawnItemFor(c, pos, map);
        }
    }

    /// <summary>DefGenerator.GenerateImpliedDefs_PreResolve: install / remove / upgrade surgeries for every cyberware def.</summary>
    [HarmonyPatch(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PreResolve))]
    public static class Patch_DefGenerator_GenerateImpliedDefs_PreResolve
    {
        public static void Postfix(bool hotReload)
        {
            foreach (var def in CyberwareRecipeGenerator.ImpliedRecipeDefs())
                DefGenerator.AddImpliedDef(def, hotReload);
        }
    }
}
