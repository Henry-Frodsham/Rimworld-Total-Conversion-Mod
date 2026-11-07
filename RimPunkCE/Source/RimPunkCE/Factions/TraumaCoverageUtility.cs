using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// NPC Trauma Team clients: rare, and only high-value members of factions that can afford it (corporations and
    /// NetWatch). Never gangs, nomads, vanilla factions, or the service factions themselves. Rolled once when the pawn
    /// is generated; the coverage hediff shows on their health tab, so the player can see who's worth killing first.
    /// </summary>
    public static class TraumaCoverageUtility
    {
        public static bool FactionCanBuyCoverage(Faction f) =>
            f != null && !f.IsPlayer && f.Ext()?.kind is CyberFactionKind.Corporation or CyberFactionKind.NetWatch;

        public static void TryGiveNpcCoverage(Pawn pawn)
        {
            if (Current.Game == null || pawn?.RaceProps?.Humanlike != true || pawn.Dead) return;
            if (!FactionCanBuyCoverage(pawn.Faction) || GameComponent_RimPunk.IsCovered(pawn)) return;
            // the faction's own kinds only: a Trauma Team squad contracted to a corp isn't a client
            if (pawn.kindDef?.defaultFactionDef != pawn.Faction.def || CyberpsychosisUtility.IsTraumaTeam(pawn)) return;
            if (CyberFactionUtility.FirstOfKind(CyberFactionKind.TraumaTeam) == null) return;
            var t = GameComponent_RimPunk.Tuning;
            if (t == null) return;
            float chance = pawn.kindDef.factionLeader || pawn.Faction.leader == pawn ? t.npcLeaderChance
                : pawn.kindDef.combatPower >= t.npcEliteMinCombatPower ? t.npcEliteChance : 0f;
            if (!Rand.Chance(chance)) return;
            var h = HediffMaker.MakeHediff(RPDefOf.RPC_TraumaTeamCoverage, pawn);
            var d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.RoundToInt(t.coverageDays * 60000f);
            pawn.health.AddHediff(h);
            Diag.Event("trauma", $"npc coverage for {Diag.Describe(pawn)} ({pawn.kindDef.defName})");
        }

        /// <summary>
        /// At most npcRaidMaxCoveredFraction of a raid (rounded down) keeps its coverage: the most valuable (leader, then
        /// combat power) keep it, the rest lose it. A raid smaller than 1/fraction pawns (34 at 3%) has no clients.
        /// </summary>
        public static void CapRaidCoverage(List<Pawn> pawns)
        {
            if (pawns.NullOrEmpty()) return;
            var t = GameComponent_RimPunk.Tuning;
            if (t == null) return;
            var covered = pawns.Where(GameComponent_RimPunk.IsCovered).ToList();
            if (covered.Count == 0) return;
            int allowed = Mathf.FloorToInt(pawns.Count * t.npcRaidMaxCoveredFraction + 0.0001f);
            if (covered.Count <= allowed) return;
            var drop = covered
                .OrderByDescending(p => p.kindDef.factionLeader || p.Faction?.leader == p)
                .ThenByDescending(p => p.kindDef.combatPower)
                .Skip(allowed).ToList();
            foreach (var p in drop)
            {
                var h = p.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_TraumaTeamCoverage);
                if (h != null) p.health.RemoveHediff(h);
            }
            Diag.Event("trauma", $"raid of {pawns.Count}: {covered.Count} covered, capped to {allowed}");
        }
    }

    /// <summary>IncidentWorker_Raid.TryGenerateRaidInfo: caps Trauma Team clients per raid (TraumaCoverageUtility.CapRaidCoverage).</summary>
    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    public static class Patch_IncidentWorker_Raid_TryGenerateRaidInfo
    {
        public static void Postfix(bool __result, ref List<Pawn> pawns)
        {
            if (__result) TraumaCoverageUtility.CapRaidCoverage(pawns);
        }
    }
}
