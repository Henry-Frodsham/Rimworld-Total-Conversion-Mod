using System.Collections.Generic;
using CombatExtended;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Smart round, styled after Cyberpunk: Edgerunners: a long blue line that curves onto the target.
    /// Fired with a smart link, Verb_ShootSmart gives it CE's homing trajectory and a sideways launch angle
    /// (<see cref="curveDegrees"/>), so it swings out and bends back in. Without a link it flies straight.
    /// Every tick it adds its position to MapComponent_SmartTrails, which draws and fades the line.
    /// </summary>
    public class BulletCE_Smart : BulletCE
    {
        public float curveDegrees;
        private bool curveApplied;

        // CE is compiled against UnityEngine.CoreModule, this mod against Waylith's UnityStubs: the Vector3 types only
        // unify at runtime, so CE's Vector3 field is read and written through reflection
        private static readonly System.Reflection.FieldInfo VelocityField = HarmonyLib.AccessTools.Field(typeof(ProjectileCE), "velocity");

        public override void Tick()
        {
            if (!curveApplied)
            {
                curveApplied = true;
                Diag.Check("weapons", $"smart round fired: {def.defName} class={GetType().Name} curve={curveDegrees:0.#} homing={homingAcceleration:0.##} launcher={(launcher as Pawn)?.LabelShort ?? launcher?.def.defName}", "smartfired");
                // y is height in CE's ballistic space: rotate around it to swing the round out sideways
                if (curveDegrees != 0f)
                {
                    try
                    {
                        var v = (Vector3)VelocityField.GetValue(this);
                        VelocityField.SetValue(this, Quaternion.AngleAxis(curveDegrees, Vector3.up) * v);
                    }
                    catch (System.Exception e)
                    {
                        // still homes, just without the outward swing
                        Diag.Check("weapons", "smart round curve failed: " + e.GetType().Name + ": " + e.Message, "smartcurve");
                    }
                }
            }
            base.Tick();
            if (Spawned) MapComponent_SmartTrails.Record(this, false);
            else if (FlightTicks <= 1 && !landed)
                // e.g. a NaN trajectory (gravityFactor 0 gives CE a 0/0 shot angle): gone before it moved
                Diag.Check("weapons", $"smart round {def.defName} destroyed on its first tick: shotAngle={shotAngle} speed={shotSpeed} gravityFactor={(def.projectile as ProjectilePropertiesCE)?.gravityFactor}", "smartdied");
        }

        public override void Impact(Thing hitThing)
        {
            if (Spawned) MapComponent_SmartTrails.Record(this, true);
            base.Impact(hitThing);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref curveDegrees, "rpcCurveDegrees");
            Scribe_Values.Look(ref curveApplied, "rpcCurveApplied");
        }
    }

    /// <summary>
    /// Draws smart-round trails: a wide soft glow plus a bright thin core along each round's recorded path, brighter
    /// towards the head, fading out FadeTicks after the round stops. Visual only: nothing is saved.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapComponent_SmartTrails : MapComponent
    {
        private const int FadeTicks = 40;
        private const int MaxPoints = 90;
        private const float GlowWidth = 0.16f;
        private const float CoreWidth = 0.05f;

        private class Trail
        {
            public readonly List<Vector3> points = new List<Vector3>();
            public int lastTick;
            public bool done;
        }

        private readonly Dictionary<int, Trail> live = new Dictionary<int, Trail>();
        private readonly List<Trail> trails = new List<Trail>();

        // created lazily on the main thread (MaterialPool is not thread safe; maps can load off-thread)
        private static Material glowMat, coreMat;
        private static bool loggedDraw;

        public MapComponent_SmartTrails(Map map) : base(map) { }

        // CE's own flight position (x, height, z), read by reflection for the same Unity-type reason as BulletCE_Smart
        private static readonly System.Reflection.PropertyInfo ExactPositionProp = HarmonyLib.AccessTools.Property(typeof(ProjectileCE), "ExactPosition");

        public static void Record(ProjectileCE proj, bool final)
        {
            var comp = proj.Map?.GetComponent<MapComponent_SmartTrails>();
            if (comp == null)
            {
                Diag.Check("weapons", $"smart trail: no MapComponent_SmartTrails on {proj.Map}", "smarttrailnocomp");
                return;
            }
            Vector3 pos;
            try
            {
                var exact = (Vector3)ExactPositionProp.GetValue(proj);
                pos = new Vector3(exact.x, 0f, exact.z); // map plane; the drawer sets the altitude
            }
            catch (System.Exception e)
            {
                Diag.Check("weapons", "smart trail: ExactPosition read failed, using DrawPos: " + e.GetType().Name, "smarttrailexact");
                pos = ((Thing)proj).DrawPos;
            }
            comp.Add(proj.thingIDNumber, pos, final);
        }

        private void Add(int id, Vector3 pos, bool final)
        {
            if (!live.TryGetValue(id, out var t))
            {
                t = new Trail();
                live[id] = t;
                trails.Add(t);
            }
            var pts = t.points;
            if (pts.Count == 0 || (pts[pts.Count - 1] - pos).sqrMagnitude > 0.0001f) pts.Add(pos);
            if (pts.Count > MaxPoints) pts.RemoveAt(0);
            t.lastTick = Find.TickManager.TicksGame;
            if (pts.Count == 3) Diag.Check("weapons", $"smart trail recording on {map}: first points {pts[0]} -> {pts[2]}", "smarttrailrec");
            if (final)
            {
                Diag.Check("weapons", $"smart trail finished: {pts.Count} points, {(pts.Count > 1 ? (pts[pts.Count - 1] - pts[0]).magnitude : 0f):0.0} cells long", "smarttrailend");
                t.done = true;
                live.Remove(id);
            }
        }

        public override void MapComponentTick()
        {
            if (trails.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = trails.Count - 1; i >= 0; i--)
            {
                var t = trails[i];
                if (!t.done && now - t.lastTick > 2) t.done = true; // round vanished without Impact (left the map, destroyed)
                if (t.done && now - t.lastTick > FadeTicks) trails.RemoveAt(i);
            }
            if (live.Count > 0 && trails.Count == 0) live.Clear();
        }

        public override void MapComponentUpdate()
        {
            if (trails.Count == 0 || map != Find.CurrentMap) return;
            if (glowMat == null)
            {
                glowMat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Transparent, new Color(0.25f, 0.6f, 1f));
                coreMat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Transparent, new Color(0.75f, 0.92f, 1f));
            }
            float alt = AltitudeLayer.MoteOverhead.AltitudeFor();
            if (!loggedDraw)
            {
                loggedDraw = true;
                Diag.Check("weapons", $"smart trails drawing: {trails.Count} trail(s), first has {trails[0].points.Count} points, alt {alt:0.##}", "smarttraildraw");
            }
            int now = Find.TickManager.TicksGame;
            foreach (var t in trails)
            {
                float life = t.done ? 1f - (now - t.lastTick) / (float)FadeTicks : 1f;
                int n = t.points.Count;
                if (life <= 0f || n < 2) continue;
                for (int i = 1; i < n; i++)
                {
                    float alpha = life * (0.25f + 0.75f * i / (n - 1)); // fainter towards the tail
                    Segment(t.points[i - 1], t.points[i], alt, GlowWidth, glowMat, alpha * 0.5f);
                    Segment(t.points[i - 1], t.points[i], alt, CoreWidth, coreMat, alpha);
                }
            }
        }

        private static void Segment(Vector3 a, Vector3 b, float alt, float width, Material mat, float alpha)
        {
            a.y = alt;
            b.y = alt;
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.001f || alpha <= 0.01f) return;
            // a little longer than the gap so consecutive segments overlap at the joints
            var m = Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(d), new Vector3(width, 1f, len + width));
            Graphics.DrawMesh(MeshPool.plane10, m, FadedMaterialPool.FadedVersionOf(mat, alpha), 0);
        }
    }
}
