using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimPunkCE
{
    /// <summary>
    /// Trauma Team on the ground, the 2077 way:
    ///  - a flashing red box marks where the AV is coming down while it descends;
    ///  - once landed, a red exclusion zone (3 cells around the AV). Colonists who wander in are warned and walked back out
    ///    (drafted ones are only warned);
    ///  - squad callouts on arrival, contact, warning, patient secured and departure (sounds: RPC_TT_* in Sounds_TraumaTeam.xml).
    /// </summary>
    public partial class GameComponent_RimPunk
    {
        public const int ExclusionMargin = 3;

        public static CellRect ExclusionZone(Thing shuttle) => shuttle.OccupiedRect().ExpandedBy(ExclusionMargin).ClipInsideMap(shuttle.Map);

        /// <summary>Every 250 ticks while the AV is on the ground: colonists inside the zone get warned and moved out.</summary>
        private static void EnforceExclusionZone(TraumaDispatch d)
        {
            var shuttle = d.ship.shipThing;
            var map = shuttle.Map;
            var zone = ExclusionZone(shuttle);
            Pawn speaker = null;
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == d.patient || p.Downed || p.Dead || !zone.Contains(p.Position)) continue;
                if (d.warned.Add(p))
                {
                    speaker ??= Speaker(d);
                    if (speaker != null) Say(RPDefOf.RPC_TT_Warning, speaker, "RPC_TraumaSay_Warning");
                    if (!d.warnedMessage)
                    {
                        d.warnedMessage = true;
                        Messages.Message("RPC_TraumaExclusionMessage".Translate(), shuttle, MessageTypeDefOf.CautionInput, historical: false);
                    }
                }
                if (p.Drafted || p.CurJobDef == JobDefOf.Goto && p.CurJob.playerForced) continue;
                if (CellFinder.TryFindRandomCellNear(zone.CenterCell, map, zone.Width / 2 + 8,
                        c => !zone.Contains(c) && c.Standable(map) && p.CanReach(c, PathEndMode.OnCell, Danger.Some), out var outside))
                {
                    var job = JobMaker.MakeJob(JobDefOf.Goto, outside);
                    job.playerForced = true;
                    job.expiryInterval = 600;
                    p.jobs.StartJob(job, JobCondition.InterruptForced);
                }
            }
        }

        /// <summary>Once per dispatch phase: arrival, contact (repeats at most every ~25 s), patient secured, departure.</summary>
        private static void Callouts(TraumaDispatch d, int now)
        {
            var speaker = Speaker(d);
            if (speaker == null) return;
            if (!d.saidArrive)
            {
                d.saidArrive = true;
                Say(RPDefOf.RPC_TT_Arrive, speaker, "RPC_TraumaSay_Arrive");
                BarkUtility.Say(speaker, BarkKeys.TraumaArrive);
                return;
            }
            if (d.patientAboard && !d.saidSecure)
            {
                d.saidSecure = true;
                Say(RPDefOf.RPC_TT_Secure, speaker, "RPC_TraumaSay_Secure");
                return;
            }
            if (d.boardTick >= 0)
            {
                if (!d.saidDepart)
                {
                    d.saidDepart = true;
                    Say(RPDefOf.RPC_TT_Depart, speaker, "RPC_TraumaSay_Depart");
                }
                return;
            }
            if (d.extractTick < 0 && now - d.lastContactTick > 1500 && AnyHostileInSquadRange(d))
            {
                d.lastContactTick = now;
                Say(RPDefOf.RPC_TT_Contact, speaker, "RPC_TraumaSay_Contact");
            }
        }

        private static Pawn Speaker(TraumaDispatch d) =>
            d.squad.Where(p => p != null && p.Spawned && !p.Dead && !p.Downed).OrderByDescending(p => p.kindDef.factionLeader).FirstOrDefault();

        private static void Say(SoundDef sound, Pawn speaker, string textKey)
        {
            sound?.PlayOneShot(new TargetInfo(speaker.Position, speaker.Map));
            BarkUtility.SayText(speaker, textKey.Translate());
        }
    }

    /// <summary>Draws the landing box (incoming Trauma Team or MaxTac AV) and the exclusion zone (landed Trauma Team AV) on the current map.</summary>
    public class MapComponent_TraumaZone : MapComponent
    {
        private static readonly Color LandingBright = new Color(1f, 0.12f, 0.12f, 0.95f);
        private static readonly Color LandingDim = new Color(1f, 0.12f, 0.12f, 0.35f);
        private static readonly Color Zone = new Color(0.95f, 0.18f, 0.18f, 0.6f);
        private readonly List<IntVec3> cells = new List<IntVec3>();

        public MapComponent_TraumaZone(Map map) : base(map) { }

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || RPDefOf.RPC_TraumaShuttleIncoming == null) return;
            bool on = Time.realtimeSinceStartup * 3f % 1f < 0.5f;   // flashes three times a second
            foreach (var t in map.listerThings.ThingsOfDef(RPDefOf.RPC_TraumaShuttleIncoming))
                Draw(t.OccupiedRect().ClipInsideMap(map), on ? LandingBright : LandingDim);
            foreach (var t in map.listerThings.ThingsOfDef(RPDefOf.RPC_MaxTacShuttleIncoming))
                Draw(t.OccupiedRect().ClipInsideMap(map), on ? LandingBright : LandingDim);
            foreach (var t in map.listerThings.ThingsOfDef(RPDefOf.RPC_TraumaShuttle))
                if (GameComponent_RimPunk.IsProtectedShip(t)) Draw(GameComponent_RimPunk.ExclusionZone(t), Zone);
        }

        private void Draw(CellRect rect, Color colour)
        {
            cells.Clear();
            cells.AddRange(rect.Cells);
            GenDraw.DrawFieldEdges(cells, colour);
        }
    }

    /// <summary>
    /// Drop-in audio. A SoundDef with this extension plays the clips in Sounds/&lt;folder&gt; (any .ogg/.wav/.mp3) when that folder
    /// has any; otherwise it keeps its XML grains (vanilla sound, or silence for callouts).
    /// </summary>
    public class CustomSoundExtension : DefModExtension
    {
        public string folder;
    }

    public static class SoundOverrides
    {
        private static readonly AccessTools.FieldRef<SubSoundDef, List<ResolvedGrain>> Grains =
            AccessTools.FieldRefAccess<SubSoundDef, List<ResolvedGrain>>("resolvedGrains");

        public static void ApplyAll()
        {
            int n = 0;
            foreach (var def in DefDatabase<SoundDef>.AllDefsListForReading)
            {
                var ext = def.GetModExtension<CustomSoundExtension>();
                if (ext?.folder == null) continue;
                var clips = ContentFinder<AudioClip>.GetAllInFolder(ext.folder).ToList();
                if (clips.Count == 0) continue;
                foreach (var sub in def.subSounds)
                    Grains(sub) = clips.Select(c => (ResolvedGrain)new ResolvedGrain_Clip(c)).ToList();
                n++;
            }
            Diag.Event("startup", $"sounds: {n} custom sound folders applied");
        }
    }
}
