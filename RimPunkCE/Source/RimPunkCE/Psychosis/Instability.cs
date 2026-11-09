using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    public enum InstabilityRecovery { Sleep, Drugs, Recreation, MedicalCare }

    public static partial class CyberpsychosisUtility
    {
        public static float ChromeLoad(Pawn pawn)
        {
            var tracker = pawn?.GetCyberTracker();
            if (tracker == null) return 0f;
            return tracker.Cyberware.Sum(c => 1f + (int)c.tier * 0.2f) + tracker.ForeignImplants.Count;
        }

        public static bool PermanentEligible(Pawn pawn) => !IsImmune(pawn) && ChromeLoad(pawn) >= (Tuning?.permanentMinLoad ?? 12f)
            && pawn.GetCyberTracker().Cyberware.Count(c => c.tier >= CyberTier.T4) >= 3;

        public static void AddInstability(Pawn pawn, float amount)
        {
            if (pawn == null || pawn.Dead || IsImmune(pawn) || amount <= 0f || ChromeLoad(pawn) <= 0f) return;
            var tracker = pawn.GetCyberTracker();
            tracker.instability = Mathf.Clamp(tracker.instability + amount, 0f, 100f);
            if (tracker.instability < 100f || !pawn.Spawned || pawn.InMentalState || Find.TickManager.TicksGame < tracker.nextPsychosisTick) return;
            if (TryStart(pawn, "RPC_Psychosis_InstabilityReason".Translate()))
            {
                if (PermanentEligible(pawn) && Rand.Chance(Mathf.Clamp01((Tuning?.permanentChance ?? 0.03f) * (RimPunkMod.Settings?.cyberpsychosisChanceMultiplier ?? 1f))))
                    pawn.health.AddHediff(RPDefOf.RPC_CyberpsychosisPermanent);
            }
        }

        public static void RecoverInstability(Pawn pawn, InstabilityRecovery action, float amount)
        {
            var tracker = pawn?.GetCyberTracker();
            if (tracker == null || pawn.Dead || amount <= 0f) return;
            tracker.instability = Mathf.Max(0f, tracker.instability - amount);
            Diag.Count("psychosis-recovery-" + action);
        }

        public static void TickInstability(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || IsImmune(pawn)) return;
            // Volume raises instability even below capacity; time never reduces it.
            AddInstability(pawn, ChromeLoad(pawn) * (Tuning?.baselinePerLoadSecond ?? 0.002f)
                * (RimPunkMod.Settings?.cyberpsychosisChanceMultiplier ?? 1f));
        }
    }

    [HarmonyPatch(typeof(Need_Rest), nameof(Need_Rest.TickResting))]
    public static class Patch_InstabilitySleep
    {
        public static void Postfix(Pawn ___pawn, float restEffectiveness)
        {
            if (restEffectiveness > 0f && !___pawn.Awake())
                CyberpsychosisUtility.RecoverInstability(___pawn, InstabilityRecovery.Sleep,
                    (RPDefOf.RPC_CyberpsychosisTuning?.sleepPerSecond ?? 0.08f) * ___pawn.UpdateRateTicks / 60f);
        }
    }

    [HarmonyPatch(typeof(Need_Joy), nameof(Need_Joy.GainJoy))]
    public static class Patch_InstabilityRecreation
    {
        public static void Prefix(Need_Joy __instance, out float __state) => __state = __instance.CurLevel;
        public static void Postfix(Need_Joy __instance, Pawn ___pawn, float __state)
        {
            float gain = __instance.CurLevel - __state;
            if (gain > 0f) CyberpsychosisUtility.RecoverInstability(___pawn, InstabilityRecovery.Recreation,
                gain * (RPDefOf.RPC_CyberpsychosisTuning?.recreationPerLevel ?? 40f));
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Patch_InstabilityDrug
    {
        public static void Prefix(Thing __instance, out bool __state) => __state = __instance.def.IsDrug;
        public static void Postfix(Pawn ingester, bool __state)
        {
            if (__state) CyberpsychosisUtility.RecoverInstability(ingester, InstabilityRecovery.Drugs,
                RPDefOf.RPC_CyberpsychosisTuning?.drugRecovery ?? 12f);
        }
    }

    [HarmonyPatch(typeof(HediffComp_TendDuration), nameof(HediffComp_TendDuration.CompTended))]
    public static class Patch_InstabilityMedicalCare
    {
        public static void Postfix(HediffComp_TendDuration __instance, float quality, int batchPosition)
        {
            // One benefit per treatment batch, not once for each tiny wound.
            if (batchPosition == 0) CyberpsychosisUtility.RecoverInstability(__instance.Pawn, InstabilityRecovery.MedicalCare,
                Mathf.Clamp01(quality) * (RPDefOf.RPC_CyberpsychosisTuning?.medicalRecovery ?? 15f));
        }
    }
}
