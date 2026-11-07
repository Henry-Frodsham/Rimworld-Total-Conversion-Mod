using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Draw-only visuals (never saved, never touches Verse.Rand, so it can't desync multiplayer):
    ///  - Sandevistan afterimages: while an afterimage effect is active and the pawn moves, a tinted, mildly
    ///    transparent copy of the pawn is left every 0.1 s; each fades out over 1 s (visual_effects.md).
    /// Quickhack upload visuals are in MapComponent_Netrun.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapComponent_CyberVisuals : MapComponent
    {
        public const int SnapshotIntervalTicks = 6;   // 0.1 s
        public const int SnapshotLifetimeTicks = 60;  // 1 s
        public const float StartAlpha = 0.55f;
        private const int AlphaSteps = 10;

        private static readonly Color AfterimageColor = new Color(0.35f, 1f, 0.75f);

        private struct Snapshot
        {
            public Pawn pawn;
            public Vector3 pos;
            public Rot4 rot;
            public int tick;
        }

        private readonly List<Snapshot> snapshots = new List<Snapshot>();
        private readonly Dictionary<(Texture, int), Material> matCache = new Dictionary<(Texture, int), Material>();

        public MapComponent_CyberVisuals(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (!RimPunkMod.Settings.visualEffects) return;
            int now = Find.TickManager.TicksGame;
            snapshots.RemoveAll(s => now - s.tick > SnapshotLifetimeTicks || s.pawn.Destroyed);
            if (now % SnapshotIntervalTicks != 0) return;

            var pawns = map.mapPawns.AllHumanlikeSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p.pather == null || !p.pather.Moving || !HasAfterimages(p)) continue;
                snapshots.Add(new Snapshot { pawn = p, pos = p.DrawPos, rot = p.Rotation, tick = now });
            }
        }

        private static bool HasAfterimages(Pawn p)
        {
            foreach (var a in p.ActiveEffects())
                if (a.Ext?.afterimages == true) return true;
            return false;
        }

        public override void MapComponentUpdate()
        {
            if (!RimPunkMod.Settings.visualEffects || snapshots.Count == 0 || Find.CurrentMap != map) return;
            int now = Find.TickManager.TicksGame;
            foreach (var s in snapshots)
            {
                float t = Mathf.Clamp01((now - s.tick) / (float)SnapshotLifetimeTicks);
                int step = Mathf.Clamp(Mathf.RoundToInt((1f - t) * AlphaSteps), 0, AlphaSteps);
                if (step == 0) continue;
                DrawGhost(s, step);
            }
        }

        private void DrawGhost(Snapshot s, int alphaStep)
        {
            var renderer = s.pawn.Drawer?.renderer;
            if (renderer == null) return;
            var pos = s.pos;
            pos.y = AltitudeLayer.MoteLow.AltitudeFor();

            var body = renderer.BodyGraphic;
            if (body != null)
            {
                var mesh = HumanlikeMeshPoolUtility.GetHumanlikeBodySetForPawn(s.pawn).MeshAt(s.rot);
                Graphics.DrawMesh(mesh, pos, Quaternion.identity, Tinted(body.MatAt(s.rot), alphaStep), 0);
            }
            var head = renderer.HeadGraphic;
            if (head != null)
            {
                var mesh = HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(s.pawn).MeshAt(s.rot);
                var headPos = pos + renderer.BaseHeadOffsetAt(s.rot);
                headPos.y += 0.01f;
                Graphics.DrawMesh(mesh, headPos, Quaternion.identity, Tinted(head.MatAt(s.rot), alphaStep), 0);
            }
        }

        private Material Tinted(Material source, int alphaStep)
        {
            var tex = source.mainTexture;
            if (!matCache.TryGetValue((tex, alphaStep), out var mat))
            {
                var c = AfterimageColor;
                c.a = StartAlpha * alphaStep / AlphaSteps;
                mat = MaterialPool.MatFrom((Texture2D)tex, ShaderDatabase.Transparent, c);
                matCache[(tex, alphaStep)] = mat;
            }
            return mat;
        }
    }
}
