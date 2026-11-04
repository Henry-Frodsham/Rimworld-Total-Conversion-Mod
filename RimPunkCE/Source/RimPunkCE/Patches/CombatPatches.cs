using System.Linq;
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
            CyberEvents.LastNeutralizeDinfo = dinfo;
            if (dinfo?.Instigator is Pawn attacker) CyberEvents.Notify_Neutralized(attacker, ___pawn, killed: false);
            if (HediffComp_SecondHeart.TryProc(___pawn)) return; // back on their feet: no Trauma Team call
            GameComponent_RimPunk.Instance?.Notify_PawnDowned(___pawn);
        }
    }

    /// <summary>Pawn.Kill: "neutralize" event for the killer. Prefix so the instigator is read before cleanup.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, bool __runOriginal)
        {
            if (!__runOriginal || __instance.Dead) return; // Second Heart cancelled the kill
            // cyberpsycho kill count (MaxTac), downed victims included
            if (dinfo?.Instigator is Pawn killer) CyberpsychosisUtility.Notify_Kill(killer, __instance);
            if (__instance.Downed) return; // downing already counted
            CyberEvents.LastNeutralizeDinfo = dinfo;
            if (dinfo?.Instigator is Pawn attacker) CyberEvents.Notify_Neutralized(attacker, __instance, killed: true);
        }
    }

    /// <summary>
    /// Verb.Available (Verse/Verb.cs): melee-only effects (berserk) block ranged.
    /// All CE verb overrides call base.Available(), so this covers CE guns too. Abilities are exempt.
    /// Exhaustion is not checked here (every verb unavailable makes vanilla log "no available melee attack"):
    /// see Patch_Verb_TryStartCastOn.
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.Available))]
    public static class Patch_Verb_Available
    {
        public static void Postfix(Verb __instance, ref bool __result)
        {
            if (!__result || __instance is Verb_CastAbility || __instance.caster is not Pawn pawn) return;
            var hediffs = pawn.health?.hediffSet;
            if (hediffs == null) return;
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

    /// <summary>
    /// Verb.TryStartCastOn (Verse/Verb.cs): exhausted pawns can't start an attack (guns or melee; abilities exempt).
    /// The CE overrides (Verb_LaunchProjectileCE, Verb_ThrowGrenade) call base first, so this covers them.
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn),
        new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            if (__instance is Verb_CastAbility || !RimPunkMod.Settings.staminaEnabled || __instance.caster is not Pawn pawn) return true;
            if (pawn.health?.hediffSet?.HasHediff(RPDefOf.RPC_Exhausted) != true) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>Thing.TakeDamage: Trauma Team and MaxTac shuttles on the ground can't be shot down.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_Thing_TakeDamage_ProtectShuttles
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Thing __instance, ref DamageWorker.DamageResult __result)
        {
            if (__instance.def != ThingDefOf.Shuttle || !GameComponent_RimPunk.IsProtectedShip(__instance)) return true;
            __result = new DamageWorker.DamageResult();
            return false;
        }
    }

    /// <summary>Verb.IsStillUsableBy: retracted mantis blades can't be used; a thrown weapon can't melee until it returns.</summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.IsStillUsableBy))]
    public static class Patch_Verb_IsStillUsableBy
    {
        public static void Postfix(Verb __instance, Pawn pawn, ref bool __result)
        {
            if (!__result) return;
            if (__instance.EquipmentSource != null && __instance.IsMeleeAttack && ThrownWeaponUtility.IsThrown(pawn))
            {
                __result = false;
                return;
            }
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
        private static readonly HashSet<int> reportedNoMelee = new HashSet<int>();

        public static void Postfix(List<VerbEntry> __result, bool terrainTools, Pawn ___pawn)
        {
            if (!terrainTools && __result != null && ___pawn != null && (__result.Count == 0 || __result.All(e => e.GetSelectionWeight(null) <= 0f))
                && reportedNoMelee.Add(___pawn.thingIDNumber))
                ReportNoMelee(___pawn);
            if (terrainTools || __result == null || __result.Count < 2) return;
            bool anyBlade = false;
            for (int i = 0; i < __result.Count; i++)
                if (IsExtendedBlade(__result[i].verb)) { anyBlade = true; break; }
            if (!anyBlade) return;
            __result.RemoveAll(e => !IsExtendedBlade(e.verb));
            // the blades may have been weighted as the worst option (weight 0) next to the removed verbs: re-weight
            if (__result.All(e => e.GetSelectionWeight(null) <= 0f))
                for (int i = 0; i < __result.Count; i++) __result[i] = new VerbEntry(__result[i].verb, 1f);
        }

        /// <summary>Diagnostics: vanilla logs "has no available melee attack"; this says why for each melee verb.</summary>
        private static void ReportNoMelee(Pawn pawn)
        {
            var lines = new List<string>();
            void Add(string source, Verb v)
            {
                if (v == null || !v.IsMeleeAttack) return;
                lines.Add($"{source}:{v.tool?.label ?? v.verbProps.label} available={v.Available()} usable={v.IsStillUsableBy(pawn)} dmgFactor={v.verbProps.GetDamageFactorFor(v, pawn):0.##}");
            }
            foreach (var v in pawn.verbTracker.AllVerbs) Add("body", v);
            var primary = pawn.equipment?.Primary;
            var eqVerbs = primary?.TryGetComp<CompEquippable>()?.AllVerbs;
            if (eqVerbs != null) foreach (var v in eqVerbs) Add(primary.def.defName, v);
            string flags = $"thrown={ThrownWeaponUtility.IsThrown(pawn)} exhausted={pawn.health.hediffSet.HasHediff(RPDefOf.RPC_Exhausted)} meleeDamageFactor={pawn.GetStatValue(StatDefOf.MeleeDamageFactor):0.##} canMelee={pawn.kindDef.canMeleeAttack}";
            Diag.Event("melee", $"{Diag.Describe(pawn)} has no usable melee verb ({flags}): {string.Join("; ", lines)}");
        }

        private static bool IsExtendedBlade(Verb v) =>
            v?.HediffSource is HediffWithComps h && h.TryGetComp<HediffComp_MantisBlades>() is { extended: true };
    }
}
