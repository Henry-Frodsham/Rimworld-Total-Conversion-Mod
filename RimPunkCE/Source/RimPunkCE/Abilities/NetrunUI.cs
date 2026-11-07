using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    public enum NetrunResult
    {
        Uploaded,  // the hack landed
        Resisted,  // the target's Self-ICE blocked it
        Immune,    // this hack can't affect this target (mechanoid, already affected, ...)
    }

    /// <summary>
    /// What's being uploaded on this map, and the UI for it: netrunner -> target lines, an upload progress box over the
    /// target, and a floating result word (UPLOADED / RESISTED / IMMUNE).
    /// Multiplayer: the list is rebuilt on the tick from pawn stances (which are already sim state) and nothing is saved.
    /// The sim never reads it back except as a plain query of who is uploading what, and it never touches Verse.Rand.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapComponent_Netrun : MapComponent
    {
        private const int ResultLifetimeTicks = 120;
        private const int ScanIntervalTicks = 5;

        private static readonly Color LineColor = new Color(0.2f, 0.75f, 1f, 0.35f);
        private static readonly Color PacketColor = new Color(0.45f, 0.9f, 1f, 0.95f);
        private static readonly Material LineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, LineColor);
        private static readonly Material PacketMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, PacketColor);
        private static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/RimPunk/Quickhack_Target", false) ?? BaseContent.BadTex;
        private static readonly Texture2D BarFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.75f, 1f));
        private static readonly Texture2D BarEmpty = SolidColorMaterials.NewSolidColorTexture(new Color(0f, 0f, 0f, 0.6f));
        private static readonly Color BoxColor = new Color(0.03f, 0.08f, 0.12f, 0.85f);

        private struct Upload
        {
            public Pawn caster;
            public Thing target;
            public AbilityDef hack;
        }

        private struct ResultPopup
        {
            public Thing target;
            public NetrunResult result;
            public int tick;
        }

        private readonly int[] resultCounts = new int[3];
        private readonly List<Upload> uploads = new List<Upload>();
        private readonly List<ResultPopup> results = new List<ResultPopup>();
        private readonly HashSet<Pawn> known = new HashSet<Pawn>();
        private readonly HashSet<Pawn> seen = new HashSet<Pawn>();
        private readonly Dictionary<Thing, int> boxStack = new Dictionary<Thing, int>();

        public MapComponent_Netrun(Map map) : base(map) { }

        public static MapComponent_Netrun For(Map map) => map?.GetComponent<MapComponent_Netrun>();

        /// <summary>From the sim (quickhack verb / core comp), or locally when a targeting click is refused.</summary>
        public static void Notify_Result(Thing target, NetrunResult result)
        {
            var comp = For(target?.MapHeld);
            comp?.results.Add(new ResultPopup { target = target, result = result, tick = Find.TickManager.TicksGame });
            if (comp != null) comp.resultCounts[(int)result]++;
            Diag.Count("netrun-" + result);
        }

        /// <summary>How many of this result this session (diagnostics and the self-test).</summary>
        public int ResultCount(NetrunResult result) => resultCounts[(int)result];

        /// <summary>AI: someone is already uploading this hack onto this target.</summary>
        public bool IsUploading(AbilityDef hack, Thing target)
        {
            for (int i = 0; i < uploads.Count; i++)
                if (uploads[i].hack == hack && uploads[i].target == target) return true;
            return false;
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            results.RemoveAll(r => now - r.tick > ResultLifetimeTicks);
            if (now % ScanIntervalTicks != 0) return;

            uploads.Clear();
            seen.Clear();
            var pawns = map.mapPawns.AllHumanlikeSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (!Verb_Quickhack.IsUploading(p, out var stance) || !stance.focusTarg.HasThing) continue;
                var target = stance.focusTarg.Thing;
                if (!target.Spawned || target.Map != map) continue;
                uploads.Add(new Upload { caster = p, target = target, hack = (stance.verb as Verb_Quickhack)?.ability?.def });
                seen.Add(p);
                if (known.Add(p))
                {
                    BarkUtility.Say(p, BarkKeys.NetrunUploading);
                    if (target is Pawn victim) BarkUtility.Say(victim, BarkKeys.NetrunHacked);
                }
            }
            known.IntersectWith(seen);
        }

        // ---------------------------------------------------------------------------------- draw (no sim state touched)

        private bool Visible(Thing t) => t.Spawned && t.Map == map && !t.Position.Fogged(map);

        private static bool StillUploading(Upload u) =>
            u.caster.Spawned && Verb_Quickhack.IsUploading(u.caster, out var stance) && stance.focusTarg.Thing == u.target;

        public override void MapComponentUpdate()
        {
            if (!RimPunkMod.Settings.visualEffects || Find.CurrentMap != map) return;
            float y = AltitudeLayer.MetaOverlays.AltitudeFor();
            for (int i = 0; i < uploads.Count; i++)
            {
                var u = uploads[i];
                if (!StillUploading(u) || !Visible(u.target)) continue;
                Verb_Quickhack.IsUploading(u.caster, out var stance);
                var a = u.caster.DrawPos;
                var b = u.target.DrawPos;
                a.y = y;
                b.y = y;
                GenDraw.DrawLineBetween(a, b, LineMat, 0.12f);
                // the filled part runs from netrunner to target as the upload progresses
                var mid = Vector3.Lerp(a, b, Verb_Quickhack.UploadProgress(stance));
                mid.y = y;
                GenDraw.DrawLineBetween(a, mid, PacketMat, 0.2f);
            }
        }

        public override void MapComponentOnGUI()
        {
            if (!RimPunkMod.Settings.visualEffects || Find.CurrentMap != map) return;
            if (Event.current.type != EventType.Repaint) return;
            boxStack.Clear();
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            for (int i = 0; i < uploads.Count; i++)
            {
                var u = uploads[i];
                if (!StillUploading(u) || !Visible(u.target)) continue;
                boxStack.TryGetValue(u.target, out int stacked);
                boxStack[u.target] = stacked + 1;
                DrawProgressBox(u, stacked);
            }
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.target == null || r.target.Destroyed || !Visible(r.target)) continue;
                DrawResult(r, now);
            }
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;
        }

        private static void DrawProgressBox(Upload u, int stacked)
        {
            Verb_Quickhack.IsUploading(u.caster, out var stance);
            float progress = Verb_Quickhack.UploadProgress(stance);
            var screen = GenMapUI.LabelDrawPosFor(u.target, 1.1f);
            var box = new Rect(screen.x - 72f, screen.y - 40f - stacked * 36f, 144f, 34f);
            Widgets.DrawBoxSolid(box, BoxColor);
            GUI.color = new Color(0.2f, 0.75f, 1f);
            Widgets.DrawBox(box);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(box.x + 3f, box.y + 5f, 24f, 24f), Icon);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            string label = u.hack?.LabelCap ?? "RPC_Netrun_Uploading".Translate();
            Widgets.Label(new Rect(box.x + 30f, box.y + 2f, box.width - 32f, 18f), label.Truncate(box.width - 32f));
            var bar = new Rect(box.x + 30f, box.y + 20f, box.width - 62f, 9f);
            Widgets.FillableBar(bar, progress, BarFill, BarEmpty, false);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(bar.xMax, box.y + 15f, 30f, 18f), progress.ToStringPercent("0"));
        }

        private static void DrawResult(ResultPopup r, int now)
        {
            float t = Mathf.Clamp01((now - r.tick) / (float)ResultLifetimeTicks);
            var screen = GenMapUI.LabelDrawPosFor(r.target, 1.3f);
            screen.y -= 22f * t; // rises as it fades
            var color = ResultColor(r.result);
            color.a = 1f - t * t;
            Text.Font = GameFont.Small;
            GenMapUI.DrawThingLabel(screen, ("RPC_Netrun_" + r.result).Translate(), color);
        }

        private static Color ResultColor(NetrunResult r)
        {
            switch (r)
            {
                case NetrunResult.Uploaded: return new Color(0.35f, 1f, 0.75f);
                case NetrunResult.Resisted: return new Color(1f, 0.8f, 0.25f);
                default: return new Color(1f, 0.35f, 0.35f);
            }
        }
    }

    /// <summary>Right-click any pawn with a netrunner selected: one entry per learned quickhack. Needs a cyberdeck.</summary>
    public class FloatMenuOptionProvider_Quickhack : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context) =>
            base.SelectedPawnValid(pawn, context) && pawn.GetCyberTracker()?.HasCyberdeck == true && pawn.abilities != null;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            var caster = context.FirstSelectedPawn;
            if (caster?.abilities == null || clickedPawn == caster) yield break;
            var hacks = caster.abilities.AllAbilitiesForReading.OfType<QuickhackAbility>()
                .Where(a => a.Ext != null)
                .OrderBy(a => a.Ext.category).ThenBy(a => a.def.label).ToList();
            foreach (var hack in hacks)
                yield return Option(hack, caster, clickedPawn);
        }

        private static FloatMenuOption Option(QuickhackAbility hack, Pawn caster, Pawn target)
        {
            string label = "RPC_Netrun_MenuOption".Translate(hack.def.LabelCap, hack.Tier.Label(), hack.RamCost.ToString("0.#"), target.LabelShort);
            string why = QuickhackUtility.InvalidReason(hack, target)?.Translate().Resolve();
            if (why == null && hack.GizmoDisabled(out string reason)) why = reason.NullOrEmpty() ? "RPC_Netrun_Unavailable".Translate().Resolve() : reason;
            if (why != null) return new FloatMenuOption(label + ": " + why.TrimEnd('.'), null);
            return new FloatMenuOption(label, () => NetrunUtility.QueueUpload(caster, hack.def, target));
        }
    }

    public static class NetrunUtility
    {
        /// <summary>Player order from the context menu.</summary>
        [SyncMethod]
        public static void QueueUpload(Pawn caster, AbilityDef hack, Pawn target)
        {
            var ability = caster?.abilities?.GetAbility(hack);
            if (ability == null || target == null || target.Dead) return;
            ability.QueueCastingJob(target, target);
        }
    }
}
