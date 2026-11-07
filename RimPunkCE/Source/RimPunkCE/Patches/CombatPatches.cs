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
            {
                CyberEvents.Notify_DamageDealt(attacker, __instance, dinfo, __result.totalDamageDealt);
                if (__instance is Pawn victim) CyberpsychosisUtility.Notify_Harmed(attacker, victim);
            }
        }
    }

    /// <summary>Pawn_HealthTracker.MakeDowned (private): "neutralize" event for whoever downed the pawn.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_Pawn_HealthTracker_MakeDowned
    {
        public class GearSnapshot
        {
            public ThingWithComps primary;
            public List<ThingDefCountClass> inventory;
        }

        /// <summary>Cyberpsycho: snapshot weapon + inventory before vanilla drops them, so they can be picked back up.</summary>
        public static void Prefix(Pawn ___pawn, out GearSnapshot __state)
        {
            __state = null;
            if (!CyberpsychosisUtility.InCyberpsychosis(___pawn)) return;
            __state = new GearSnapshot
            {
                primary = ___pawn.equipment?.Primary,
                inventory = ___pawn.inventory?.innerContainer
                    .GroupBy(t => t.def)
                    .Select(g => new ThingDefCountClass(g.Key, g.Sum(t => t.stackCount)))
                    .ToList(),
            };
        }

        public static void Postfix(Pawn ___pawn, DamageInfo? dinfo, GearSnapshot __state)
        {
            if (__state != null && ___pawn.MentalState is MentalState_Cyberpsychosis psycho) psycho.Notify_GearDropped(__state.primary, __state.inventory);
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
            // cyberpsycho kill count (MaxTac), downed victims included; deaths without a psycho instigator
            // (bled out, DoT, organ failure) are credited to the psycho who last hurt them
            CyberpsychosisUtility.Notify_Death(__instance, dinfo?.Instigator as Pawn);
            if (__instance.Downed) return; // downing already counted
            CyberEvents.LastNeutralizeDinfo = dinfo;
            if (dinfo?.Instigator is Pawn attacker) CyberEvents.Notify_Neutralized(attacker, __instance, killed: true);
        }
    }

    /// <summary>
    /// Verb.Available (Verse/Verb.cs): melee-only effects (berserk) block ranged.
    /// All CE verb overrides call base.Available(), so this covers CE guns too. Abilities are exempt.
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
    /// Verb.TryFindShootLineFromTo: vanilla's TryStartCastOn wants a shoot line before warmup, and CE reroutes this to its own
    /// finder, which stops at walls. Without this a charged tech shot at a target behind a wall never started, whatever
    /// CanHitTargetFrom said. Runs before CE's reroute, and only steps in when there's no normal line. Once the shot goes off,
    /// Verb_ShootTech.KeepBurstOnNoShootLine takes over.
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryFindShootLineFromTo))]
    public static class Patch_Verb_TryFindShootLineFromTo_TechPierce
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref bool __result)
        {
            if (!(__instance is Verb_ShootTech tech) || !tech.CanPierceTo(root, targ)) return true;
            if (tech.TryFindCEShootLineFromTo(root, targ, out resultingLine)) __result = true;
            else
            {
                resultingLine = new ShootLine(root, targ.Cell);
                __result = true;
            }
            return false;
        }
    }

    /// <summary>Thing.TakeDamage: Trauma Team and MaxTac shuttles on the ground can't be shot down.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_Thing_TakeDamage_ProtectShuttles
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            // attacking Trauma Team (an operator or their shuttle) on purpose turns them hostile (TraumaHostility.cs)
            if (GameComponent_RimPunk.IsIntentionalAttack(__instance, dinfo))
                GameComponent_RimPunk.Instance?.Notify_AttackedTraumaTeam(dinfo.Instigator, __instance);
            // a hostile Trauma Team's shuttle is fair game
            if (__instance.def == RPDefOf.RPC_TraumaShuttle && GameComponent_RimPunk.TraumaHostileToPlayer) return true;
            if ((__instance.def != ThingDefOf.Shuttle && __instance.def != RPDefOf.RPC_TraumaShuttle && __instance.def != RPDefOf.RPC_MaxTacShuttle) || !GameComponent_RimPunk.IsProtectedShip(__instance)) return true;
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
            string flags = $"thrown={ThrownWeaponUtility.IsThrown(pawn)} meleeDamageFactor={pawn.GetStatValue(StatDefOf.MeleeDamageFactor):0.##} canMelee={pawn.kindDef.canMeleeAttack}";
            Diag.Event("melee", $"{Diag.Describe(pawn)} has no usable melee verb ({flags}): {string.Join("; ", lines)}");
        }

        private static bool IsExtendedBlade(Verb v) =>
            v?.HediffSource is HediffWithComps h && h.TryGetComp<HediffComp_MantisBlades>() is { extended: true };
    }
}

namespace RimPunkCE.Patches
{
    /// <summary>
    /// Diagnostics only: CE Verb_MeleeAttackCE.ApplyMeleeDamageToTarget threw NREs in DamageInfosToApply during a
    /// cyberpsycho melee (2026-11-04). When it throws, log what the verb was built from (once per verb/target def
    /// pair) so the null can be traced. The exception is passed through untouched.
    /// </summary>
    [HarmonyPatch(typeof(CombatExtended.Verb_MeleeAttackCE), nameof(CombatExtended.Verb_MeleeAttackCE.ApplyMeleeDamageToTarget))]
    public static class Patch_Verb_MeleeAttackCE_ApplyMeleeDamageToTarget
    {
        public static System.Exception Finalizer(System.Exception __exception, CombatExtended.Verb_MeleeAttackCE __instance, LocalTargetInfo target)
        {
            if (__exception == null) return null;
            try
            {
                var v = __instance;
                var caster = v.CasterPawn;
                var gizmo = caster?.TryGetComp<CombatExtended.CompMeleeTargettingGizmo>();
                string info = $"verb={v.GetType().Name} tool={v.tool?.GetType().Name ?? "null"}:{v.tool?.label ?? "-"} " +
                              $"caps=[{string.Join(",", v.tool?.capacities?.Select(c => c?.defName ?? "null") ?? Enumerable.Empty<string>())}] " +
                              $"dmgDef={v.verbProps?.meleeDamageDef?.defName ?? "null"} equip={v.EquipmentSource?.def.defName ?? "none"} " +
                              $"hediff={v.HediffSource?.def.defName ?? "none"} hediffComp={v.HediffCompSource?.Def?.defName ?? "none"} " +
                              $"caster={Diag.Describe(caster)} target={target.Thing?.def.defName ?? "null"} " +
                              $"gizmo={(gizmo == null ? "none" : (gizmo.targetBodyPart?.defName ?? "nullPart"))}";
                Diag.Check("melee", "CE melee damage threw " + __exception.GetType().Name + ": " + info,
                    "meleeNre_" + (v.tool?.label ?? v.GetType().Name) + "_" + (caster?.def.defName) + "_" + target.Thing?.def.defName);
            }
            catch { /* diagnostics must not throw */ }
            return __exception;
        }
    }
}

namespace RimPunkCE.Patches
{
    /// <summary>
    /// CE CompSuppressable.CompTickInterval: a suppressed pawn waiting inside a shuttle / transporter still ticks, and
    /// CE's "call for help" then reads its map (null) and throws every tick (seen with Trauma Team operators boarding
    /// under fire). Suppression means nothing off the map, so it isn't ticked there.
    /// </summary>
    [HarmonyPatch(typeof(CombatExtended.CompSuppressable), nameof(CombatExtended.CompSuppressable.CompTickInterval))]
    public static class Patch_CompSuppressable_SkipUnspawned
    {
        public static bool Prefix(CombatExtended.CompSuppressable __instance) => __instance.parent != null && __instance.parent.Spawned;
    }

    /// <summary>
    /// CE AI_Utility.EdgingCloser (fire-mode choice when an attack starts) reads both pawns' pathers: a target picked up
    /// and carried (Trauma Team extracting a patient the colony was shooting at) has none, and every attacker threw.
    /// Off the map nobody is edging closer.
    /// </summary>
    [HarmonyPatch(typeof(CombatExtended.AI.AI_Utility), nameof(CombatExtended.AI.AI_Utility.EdgingCloser))]
    public static class Patch_AIUtility_EdgingCloser_Unspawned
    {
        public static bool Prefix(Pawn pawn, Pawn other, ref bool __result)
        {
            if (pawn != null && other != null && pawn.Spawned && other.Spawned && pawn.pather != null && other.pather != null) return true;
            __result = false;
            return false;
        }
    }
}
