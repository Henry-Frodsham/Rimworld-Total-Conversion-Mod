using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimPunkCE.Patches
{
    /// <summary>
    /// JobGiver_AIFightEnemy.TryGiveJob: the base of every "fight" job giver, cyberpsychos included
    /// (JobGiver_CyberpsychoFight ends up here). Keeps the vanilla job unless the pawn's chrome calls for something else:
    /// melee builds close in, ranged builds back off, netrunners pick their own targets (ContextualCombat).
    /// Runs even when vanilla found nothing to do at range, as long as it has a target. Colonists are left alone.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
    public static class Patch_JobGiver_AIFightEnemy_TryGiveJob
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (!RimPunkMod.Settings.contextualAI || !ContextualCombat.Applies(pawn) || pawn.RaceProps?.Humanlike != true) return;
            __result = ContextualCombat.AdjustFightJob(pawn, __result);
        }
    }
}
