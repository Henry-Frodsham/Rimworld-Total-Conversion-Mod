using System.Linq;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// NetWatch hunts netrunners who use high-tier quickhacks / cyberdecks (prompts/factions.md):
    /// fires only if a colonist with a tier 4+ cyberdeck uploaded a quickhack in the last few days.
    /// NetWatch turns hostile and raids.
    /// </summary>
    public class IncidentWorker_NetWatchHunt : IncidentWorker
    {
        public const int RecentQuickhackTicks = 60000 * 5;

        /// <summary>Below this NetWatch's cheapest team doesn't fit, and the raid fails on an early colony.</summary>
        private const float MinRaidPoints = 400f;

        private static Faction NetWatch => CyberFactionUtility.FirstOfKind(CyberFactionKind.NetWatch);

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (parms.target is not Map map || NetWatch == null) return false;
            int now = Find.TickManager.TicksGame;
            return map.mapPawns.FreeColonistsSpawned.Any(p => now - (p.GetCyberTracker()?.lastHighTierQuickhackTick ?? -999999) < RecentQuickhackTicks);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var nw = NetWatch;
            if (nw == null) return false;
            if (!nw.HostileTo(Faction.OfPlayer)) CyberFactionUtility.ForceRelation(nw, Faction.OfPlayer, FactionRelationKind.Hostile, "RPC_NetWatchReason".Translate());
            var raid = new IncidentParms
            {
                target = parms.target,
                faction = nw,
                points = UnityEngine.Mathf.Max(MinRaidPoints, parms.points > 0 ? parms.points : StorytellerUtility.DefaultThreatPointsNow(parms.target)),
                forced = true,
            };
            bool ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(raid);
            Diag.Event("events", $"NetWatch hunt fired: {ok}, {raid.points:0} points");
            return ok;
        }
    }

    /// <summary>
    /// Rare hard event: a rogue cyberpsycho, chromed to the teeth, wanders onto the map. Hostile to everyone.
    /// Their chrome is set so usage stays well above 50%, so the episode never ends on its own.
    /// </summary>
    public class IncidentWorker_RogueCyberpsycho : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => parms.target is Map && def.pawnKind != null;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var map = (Map)parms.target;
            if (!CellFinder.TryFindRandomEdgeCellWith(c => map.reachability.CanReachColony(c) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Hostile, out var cell))
                return false;

            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(def.pawnKind, null, mustBeCapableOfViolence: true));
            GenSpawn.Spawn(pawn, cell, map);
            pawn.mindState.mentalStateHandler.TryStartMentalState(RPDefOf.RPC_Cyberpsychosis, "RPC_RogueReason".Translate(), forced: true, forceWake: true, transitionSilently: true);
            Diag.Event("events", $"rogue cyberpsycho {Diag.Describe(pawn)}: usage {pawn.GetCyberTracker()?.UsageRatio:P0}, in state {pawn.MentalStateDef?.defName}");
            SendStandardLetter("RPC_RogueLetter".Translate(), "RPC_RogueLetterText".Translate(pawn.LabelShort), LetterDefOf.ThreatBig, parms, pawn);
            return true;
        }
    }
}
