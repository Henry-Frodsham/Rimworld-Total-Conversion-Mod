using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimPunkCE.Compat
{
    /// <summary>
    /// Prepare Carefully (optional): OptionsHealth.FindImplantOptionThatAddsHediffDefToBodyPart crashes loading a saved
    /// pawn whenever an implant was saved by hediff only (no recipe) and the same hediff also has a recipe-based option:
    /// recipe options have BodyPartDefs == null, and PC calls o.BodyPartDefs.Contains(part) on them. Seen with FSF AI
    /// modules, Biotech mechlink implants and Anomaly fleshmass parts ("Exception filling window for
    /// DialogLoadColonist", 2026-11-04). Replaced with a null-safe version that also matches recipe options by their
    /// recipe's body parts. Skipped entirely when PC isn't loaded (Prepare returns false).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_PC_FindImplantOptionThatAddsHediffDefToBodyPart
    {
        private static MethodBase target;
        private static PropertyInfo optionsProp, hediffProp, partsProp, recipeProp;

        public static bool Prepare()
        {
            var type = AccessTools.TypeByName("EdB.PrepareCarefully.OptionsHealth");
            var optionType = AccessTools.TypeByName("EdB.PrepareCarefully.ImplantOption");
            if (type == null || optionType == null) return false;
            target = AccessTools.Method(type, "FindImplantOptionThatAddsHediffDefToBodyPart");
            optionsProp = AccessTools.Property(type, "ImplantOptions");
            hediffProp = AccessTools.Property(optionType, "HediffDef");
            partsProp = AccessTools.Property(optionType, "BodyPartDefs");
            recipeProp = AccessTools.Property(optionType, "RecipeDef");
            bool ok = target != null && optionsProp != null && hediffProp != null && partsProp != null;
            if (!ok) Log.Warning("[RimPunk CE] Prepare Carefully found but its implant API changed: load-crash fix not applied.");
            return ok;
        }

        public static MethodBase TargetMethod() => target;

        public static bool Prefix(object __instance, HediffDef hediffDef, BodyPartDef bodyPartDef, ref object __result)
        {
            if (!(optionsProp.GetValue(__instance) is IEnumerable options)) return true;
            object byRecipe = null, anyMatch = null;
            foreach (var o in options)
            {
                if (o == null || hediffProp.GetValue(o) as HediffDef != hediffDef) continue;
                var parts = partsProp.GetValue(o) as ICollection<BodyPartDef>;
                if (bodyPartDef == null ? (parts == null || parts.Count == 0) : parts != null && parts.Contains(bodyPartDef))
                {
                    __result = o; // exact match, same as PC intended
                    return false;
                }
                var recipe = recipeProp?.GetValue(o) as RecipeDef;
                if (byRecipe == null && recipe != null && bodyPartDef != null && recipe.appliedOnFixedBodyParts != null
                    && recipe.appliedOnFixedBodyParts.Contains(bodyPartDef))
                    byRecipe = o;
                anyMatch ??= o;
            }
            __result = byRecipe ?? anyMatch; // may be null: PC then adds the implant without an option, as it did before
            return false;
        }
    }
}
