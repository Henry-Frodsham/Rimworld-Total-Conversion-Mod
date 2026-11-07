using HarmonyLib;
using RimWorld;
using Verse;

namespace RimPunkCE.Patches
{
    /// <summary>Verb_MeleeAttack.SoundDodge (private, called by vanilla and CE melee only on a dodge): Kerenzikov, Nano-plating.</summary>
    [HarmonyPatch(typeof(Verb_MeleeAttack), "SoundDodge")]
    public static class Patch_Verb_MeleeAttack_SoundDodge
    {
        public static void Postfix(Thing target)
        {
            if (target is Pawn p && p.GetCyberTracker() != null) CyberEvents.Notify_Dodged(p);
        }
    }

    /// <summary>Verb.TryStartCastOn: throwing a grenade (Handle Wrap).</summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn),
        new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_Grenade
    {
        public static void Postfix(Verb __instance, bool __result)
        {
            if (!__result || __instance.caster is not Pawn pawn || pawn.GetCyberTracker() == null) return;
            if (__instance is CombatExtended.Verb_ThrowGrenade || WeaponClassUtility.IsGrenade(__instance.EquipmentSource))
                CyberEvents.Notify_UsedGrenade(pawn);
        }
    }

    /// <summary>Pawn.Kill: Second Heart saves the pawn instead (violent deaths only).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_SecondHeart
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance.Dead || dinfo == null || !dinfo.Value.Def.ExternalViolenceFor(__instance)) return true;
            return !HediffComp_SecondHeart.TryProc(__instance);
        }
    }

    /// <summary>CompAmmoUser.LoadAmmo: reloading an empty magazine charges the Microgenerator.</summary>
    [HarmonyPatch(typeof(CombatExtended.CompAmmoUser), nameof(CombatExtended.CompAmmoUser.LoadAmmo))]
    public static class Patch_CompAmmoUser_LoadAmmo
    {
        public static void Prefix(CombatExtended.CompAmmoUser __instance, out int __state) => __state = __instance.CurMagCount;

        public static void Postfix(CombatExtended.CompAmmoUser __instance, int __state)
        {
            if (__state > 0 || __instance.Holder is not Pawn pawn || pawn.health?.hediffSet == null) return;
            foreach (var h in pawn.health.hediffSet.hediffs)
                if (h is HediffWithComps hwc && hwc.TryGetComp<HediffComp_Microgenerator>() is HediffComp_Microgenerator c)
                    c.charge = __instance.CurMagCount;
        }
    }

    /// <summary>Verb_LaunchProjectileCE.RecoilAmount: RPC_RecoilFactor (Shock Absorber, Immovable Force).</summary>
    [HarmonyPatch(typeof(CombatExtended.Verb_LaunchProjectileCE), nameof(CombatExtended.Verb_LaunchProjectileCE.RecoilAmount), MethodType.Getter)]
    public static class Patch_Verb_LaunchProjectileCE_RecoilAmount
    {
        public static void Postfix(CombatExtended.Verb_LaunchProjectileCE __instance, ref float __result)
        {
            if (__instance.CasterPawn is Pawn p && p.GetCyberTracker() != null) __result *= p.GetStatValue(RPStatDefOf.RPC_RecoilFactor);
        }
    }

    /// <summary>Verb_LaunchProjectileCE.ShiftVecReportFor: RPC_SpreadFactor (Immovable Force).</summary>
    [HarmonyPatch(typeof(CombatExtended.Verb_LaunchProjectileCE), nameof(CombatExtended.Verb_LaunchProjectileCE.ShiftVecReportFor), new[] { typeof(LocalTargetInfo), typeof(IntVec3) })]
    public static class Patch_Verb_LaunchProjectileCE_ShiftVecReportFor
    {
        public static void Postfix(CombatExtended.Verb_LaunchProjectileCE __instance, CombatExtended.ShiftVecReport __result)
        {
            if (__result != null && __instance.CasterPawn is Pawn p && p.GetCyberTracker() != null)
                __result.spreadDegrees *= p.GetStatValue(RPStatDefOf.RPC_SpreadFactor);
        }
    }

    /// <summary>
    /// JobDriver.SetupToils (internal): CE's JobDriver_Reload fails on any mental state except vanilla Berserk, so a
    /// cyberpsycho's reload was cancelled the moment it started and the AI re-issued it forever (reload loop).
    /// Drop that one fail condition for cyberpsychos.
    /// </summary>
    [HarmonyPatch(typeof(Verse.AI.JobDriver), "SetupToils")]
    public static class Patch_JobDriver_SetupToils_ReloadInPsychosis
    {
        public static void Postfix(Verse.AI.JobDriver __instance)
        {
            if (__instance is not CombatExtended.JobDriver_Reload || !CyberpsychosisUtility.InCyberpsychosis(__instance.pawn)) return;
            __instance.globalFailConditions.RemoveAll(f => f.Method.Name.Contains("FailOnMentalState"));
        }
    }
}

namespace RimPunkCE.Patches
{
    /// <summary>
    /// Pawn_HealthTracker.AddHediff / RemoveHediff: other mods' implants use cyberware capacity (ForeignCyberwareUtility),
    /// so the tracker's cached usage is refreshed when one comes or goes. Installing one on a pawn on a map rolls the same
    /// cyberpsychosis event as installing RimPunk cyberware (pawn generation and world pawns don't roll).
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult))]
    public static class Patch_Pawn_HealthTracker_AddHediff_Foreign
    {
        public static void Postfix(Pawn ___pawn, Hediff hediff)
        {
            if (!ForeignCyberwareUtility.IsForeignImplant(hediff)) return;
            ___pawn.GetCyberTracker()?.Notify_CyberwareChanged();
            if (___pawn.Spawned && !___pawn.Dead) CyberpsychosisUtility.Notify_Event(___pawn, CyberpsychosisEvent.CyberwareInstalled);
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    public static class Patch_Pawn_HealthTracker_RemoveHediff_Foreign
    {
        public static void Postfix(Pawn ___pawn, Hediff hediff)
        {
            if (ForeignCyberwareUtility.IsForeignImplant(hediff)) ___pawn.GetCyberTracker()?.Notify_CyberwareChanged();
        }
    }
}
