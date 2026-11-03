using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

// Every Harmony patch in this mod lives in Patches/, one class per target, with the reason next to it.
// Targets were checked against ModdingAPI/source for RimWorld 1.7.4995 and CE 16.7.4.

namespace RimPunkCE.Patches
{
    /// <summary>
    /// Thing.TakeDamage (Verse/Thing.cs): outgoing damage modifiers (vs mechanoids, ranged crit, headshot)
    /// and the "dealt damage" event. CE projectiles and melee both end up here.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_Thing_TakeDamage
    {
        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (dinfo.Instigator is not Pawn attacker || attacker.GetCyberTracker() == null) return;
            if (!dinfo.Def.harmsHealth || dinfo.Amount <= 0f) return;
            float f = CombatDamageUtility.OutgoingDamageFactor(attacker, __instance, dinfo);
            if (f != 1f) dinfo.SetAmount(dinfo.Amount * f);
        }

        public static void Postfix(Thing __instance, DamageInfo dinfo, DamageWorker.DamageResult __result)
        {
            if (dinfo.Instigator is Pawn attacker && __result != null)
                CyberEvents.Notify_DamageDealt(attacker, __instance, dinfo, __result.totalDamageDealt);
        }
    }

    /// <summary>Pawn_HealthTracker.MakeDowned (private): "neutralize" event for whoever downed the pawn.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_Pawn_HealthTracker_MakeDowned
    {
        public static void Postfix(Pawn ___pawn, DamageInfo? dinfo)
        {
            if (dinfo?.Instigator is Pawn attacker) CyberEvents.Notify_Neutralized(attacker, ___pawn, killed: false);
        }
    }

    /// <summary>Pawn.Kill: "neutralize" event for the killer. Prefix so the instigator is read before cleanup.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance.Dead || __instance.Downed) return; // downing already counted
            if (dinfo?.Instigator is Pawn attacker) CyberEvents.Notify_Neutralized(attacker, __instance, killed: true);
        }
    }

    /// <summary>
    /// Verb.Available (Verse/Verb.cs): exhausted pawns can't attack; melee-only effects (berserk) block ranged.
    /// All CE verb overrides call base.Available(), so this covers CE guns and melee too. Abilities are exempt.
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.Available))]
    public static class Patch_Verb_Available
    {
        public static void Postfix(Verb __instance, ref bool __result)
        {
            if (!__result || __instance is Verb_CastAbility || __instance.caster is not Pawn pawn) return;
            var hediffs = pawn.health?.hediffSet;
            if (hediffs == null) return;
            if (RimPunkMod.Settings.staminaEnabled && hediffs.HasHediff(RPDefOf.RPC_Exhausted))
            {
                __result = false;
                return;
            }
            if (!__instance.IsMeleeAttack)
            {
                foreach (var active in pawn.ActiveEffects())
                    if (active.Ext?.meleeOnly == true)
                    {
                        __result = false;
                        return;
                    }
            }
        }
    }

    /// <summary>Verb.IsStillUsableBy: retracted mantis blades can't be used.</summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.IsStillUsableBy))]
    public static class Patch_Verb_IsStillUsableBy
    {
        public static void Postfix(Verb __instance, ref bool __result)
        {
            if (!__result) return;
            if (__instance.HediffSource is HediffWithComps h && h.TryGetComp<HediffComp_MantisBlades>() is { extended: false })
                __result = false;
        }
    }

    /// <summary>
    /// Pawn_MeleeVerbs.GetUpdatedAvailableVerbsList: when mantis blades are extended they are the only
    /// melee option, so a gun-holder melees with the blades instead of the gun butt.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.GetUpdatedAvailableVerbsList))]
    public static class Patch_Pawn_MeleeVerbs_GetUpdatedAvailableVerbsList
    {
        public static void Postfix(List<VerbEntry> __result, bool terrainTools)
        {
            if (terrainTools || __result == null || __result.Count < 2) return;
            bool anyBlade = false;
            for (int i = 0; i < __result.Count; i++)
                if (IsExtendedBlade(__result[i].verb)) { anyBlade = true; break; }
            if (anyBlade) __result.RemoveAll(e => !IsExtendedBlade(e.verb));
        }

        private static bool IsExtendedBlade(Verb v) =>
            v?.HediffSource is HediffWithComps h && h.TryGetComp<HediffComp_MantisBlades>() is { extended: true };
    }
}
