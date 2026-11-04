using HarmonyLib;
using RimWorld;
using Verse;

namespace RimPunkCE.Patches
{
    /// <summary>PawnGenerator.GeneratePawn(request): faction cyberware loadouts and street names.</summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    public static class Patch_PawnGenerator_GeneratePawn
    {
        public static void Postfix(Pawn __result) => PawnLoadoutUtility.OnPawnGenerated(__result);
    }

    /// <summary>
    /// Faction.TryAffectGoodwillWith:
    ///  - Trauma Team and MaxTac goodwill with the player never drops (they're never hostile);
    ///  - gaining goodwill with a corporation (gifts, quests, paying) angers its rival corporations;
    ///  - when the player angers a corporation enough to make it hostile, it sends a retaliation strike team.
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Patch_Faction_TryAffectGoodwillWith
    {
        private static bool reentrant;

        public static bool Prefix(Faction __instance, Faction other, int goodwillChange, ref bool __result)
        {
            if (goodwillChange < 0 && ((__instance.IsNeverHostile() && other.IsPlayer) || (other.IsNeverHostile() && __instance.IsPlayer)))
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static void Postfix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, bool __result)
        {
            if (!__result || reentrant || goodwillChange == 0) return;
            Faction corp = __instance.IsPlayer ? other : other.IsPlayer ? __instance : null;
            if (corp == null || !corp.IsCorporation()) return;
            var ext = corp.Ext();
            reentrant = true;
            try
            {
                if (goodwillChange > 0)
                {
                    foreach (var rival in Find.FactionManager.AllFactionsListForReading)
                        if (rival != corp && ext.rivals.Contains(rival.def) && !rival.defeated)
                            rival.TryAffectGoodwillWith(Faction.OfPlayer, -(goodwillChange + 1) / 2, canSendMessage: true, canSendHostilityLetter: true);
                }
                else if (reason != null && corp.HostileTo(Faction.OfPlayer))
                {
                    QueueRetaliation(corp, ext);
                }
            }
            finally
            {
                reentrant = false;
            }
        }

        public static void QueueRetaliation(Faction corp, CyberFactionExtension ext)
        {
            var map = Find.AnyPlayerHomeMap;
            if (map == null) return;
            var parms = new IncidentParms
            {
                target = map,
                faction = corp,
                points = StorytellerUtility.DefaultThreatPointsNow(map) * ext.retaliationPointsFactor,
                forced = true,
            };
            int delay = (int)(ext.retaliationDelayDays.RandomInRange * 60000f);
            Find.Storyteller.incidentQueue.Add(IncidentDefOf.RaidEnemy, Find.TickManager.TicksGame + delay, parms, 60000);
            Messages.Message("RPC_StrikeTeamWarning".Translate(corp.Name), MessageTypeDefOf.ThreatBig);
            Diag.Event("factions", $"retaliation from {corp.Name} queued in {delay} ticks, {parms.points:0} points");
        }
    }

    /// <summary>
    /// WorldPawns.GetSituation: a patient off-map with Trauma Team counts as borrowed, not free. Vanilla hands free
    /// world-pawn colonists to a random NPC faction (Pawn.Notify_PassedToWorld) and may garbage-collect free pawns.
    /// </summary>
    [HarmonyPatch(typeof(RimWorld.Planet.WorldPawns), nameof(RimWorld.Planet.WorldPawns.GetSituation))]
    public static class Patch_WorldPawns_GetSituation
    {
        public static void Postfix(Pawn p, ref RimWorld.Planet.WorldPawnSituation __result)
        {
            if (__result == RimWorld.Planet.WorldPawnSituation.Free && GameComponent_RimPunk.IsAwayWithTraumaTeam(p))
                __result = RimWorld.Planet.WorldPawnSituation.Borrowed;
        }
    }
}
