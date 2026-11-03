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
    ///  - Quickhack target widget: icon + upload progress bar above the target.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapComponent_CyberVisuals : MapComponent
    {
        public const int SnapshotIntervalTicks = 6;   // 0.1 s
        public const int SnapshotLifetimeTicks = 60;  // 1 s
        public const float StartAlpha = 0.55f;
        private const int AlphaSteps = 10;

        private static readonly Color AfterimageColor = new Color(0.35f, 1f, 0.75f);
        private static readonly Texture2D QuickhackIcon = ContentFinder<Texture2D>.Get("UI/RimPunk/Quickhack_Target", false) ?? BaseContent.BadTex;
        private static readonly Texture2D BarFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.75f, 1f));
        private static readonly Texture2D BarEmpty = SolidColorMaterials.NewSolidColorTexture(new Color(0f, 0f, 0f, 0.6f));

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

        public override void MapComponentOnGUI()
        {
            if (Find.CurrentMap != map) return;
            var pawns = map.mapPawns.AllHumanlikeSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (!Verb_Quickhack.IsUploading(pawns[i], out var stance) || !stance.focusTarg.HasThing) continue;
                var target = stance.focusTarg.Thing;
                if (!target.Spawned || target.Map != map) continue;
                Vector2 screen = GenMapUI.LabelDrawPosFor(target, 1.1f);
                var icon = new Rect(screen.x - 12f, screen.y - 34f, 24f, 24f);
                GUI.DrawTexture(icon, QuickhackIcon);
                var bar = new Rect(screen.x - 24f, screen.y - 8f, 48f, 6f);
                Widgets.FillableBar(bar, Verb_Quickhack.UploadProgress(stance), BarFill, BarEmpty, false);
            }
        }
    }
}
