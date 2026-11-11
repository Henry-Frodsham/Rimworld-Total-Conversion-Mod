using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    public enum CyberAnimKind
    {
        /// <summary>Mantis blades: the blade on the attacking arm extends and swings, a slash arc at the target.</summary>
        BladeSlash,
        /// <summary>Gorilla arms: a fist glow at the target and a shockwave ring.</summary>
        Punch,
        /// <summary>Monowire: a wire whips from the hand to the target.</summary>
        WhipLash,
        /// <summary>Projectile launch system: the barrel is out, muzzle flash, recoil.</summary>
        LauncherFire,
        /// <summary>Mantis dash: a streak along the dash path with both blades out.</summary>
        DashStreak,
    }

    /// <summary>
    /// Cyberware use animations (rudimentary placeholder art in Textures/RimPunk/Anim, drawn by
    /// Source/Tools/gen_anim_textures.py; replace the PNGs with real art, same names). Draw-only: nothing is saved and
    /// Verse.Rand is never touched, so it can't desync multiplayer. Off with the mod's visual effects setting.
    /// Continuous states (blades out while fighting in melee, launcher barrel out while aiming) are read from the pawn
    /// each frame; one-shot animations are queued by <see cref="Play"/>.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapComponent_CyberAnimations : MapComponent
    {
        private static readonly Texture2D BladeTex = Tex("Blade"), ArcTex = Tex("SlashArc"), RingTex = Tex("Ring"),
            GlowTex = Tex("Glow"), BarrelTex = Tex("Barrel"), FlashTex = Tex("Flash");

        private static Texture2D Tex(string name) => ContentFinder<Texture2D>.Get("RimPunk/Anim/" + name, false) ?? BaseContent.WhiteTex;

        public static readonly Color Steel = new Color(0.85f, 0.9f, 1f);
        public static readonly Color Electric = new Color(0.4f, 0.75f, 1f);
        public static readonly Color Thermal = new Color(1f, 0.55f, 0.15f);
        public static readonly Color Toxic = new Color(0.45f, 1f, 0.3f);
        private static readonly Color FlashColor = new Color(1f, 0.85f, 0.5f);

        private class Anim
        {
            public Pawn pawn;
            public CyberAnimKind kind;
            public Thing target;
            public Vector3 from, to;
            public Color color;
            public float side; // -1 left arm, +1 right arm
            public int start, duration;
        }

        private readonly List<Anim> anims = new List<Anim>();
        private static bool loggedDraw;

        private static string Describe(Texture2D t) => t == BaseContent.WhiteTex ? "MISSING" : $"{t.width}x{t.height}";
        private readonly Dictionary<(Texture2D, Color), Material> mats = new Dictionary<(Texture2D, Color), Material>();

        public MapComponent_CyberAnimations(Map map) : base(map) { }

        private static bool Enabled => RimPunkMod.Settings.visualEffects;

        // ================================================================================================ triggers

        /// <summary>Queue a one-shot animation. <paramref name="from"/>/<paramref name="to"/> default to the pawn and target positions.</summary>
        public static void Play(Pawn pawn, CyberAnimKind kind, LocalTargetInfo target, Color color, float side = 1f, Vector3? from = null, Vector3? to = null, int duration = 0)
        {
            if (!Enabled || pawn?.MapHeld == null)
            {
                Diag.Check("visuals", $"animation {kind} skipped: enabled={Enabled} map={(pawn?.MapHeld != null)}", "animskip");
                return;
            }
            var comp = pawn.MapHeld.GetComponent<MapComponent_CyberAnimations>();
            if (comp == null)
            {
                Diag.Check("visuals", "animation skipped: no MapComponent_CyberAnimations on this map", "animnocomp");
                return;
            }
            if (comp.anims.Count > 200) comp.anims.RemoveAt(0);
            comp.anims.Add(new Anim
            {
                pawn = pawn, kind = kind, target = target.Thing, color = color, side = side,
                from = from ?? pawn.DrawPos, to = to ?? (target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted()),
                start = Find.TickManager.TicksGame, duration = duration > 0 ? duration : DefaultDuration(kind),
            });
        }

        private static int DefaultDuration(CyberAnimKind kind) => kind switch
        {
            CyberAnimKind.BladeSlash => 18,
            CyberAnimKind.Punch => 16,
            CyberAnimKind.WhipLash => 16,
            CyberAnimKind.LauncherFire => 24,
            CyberAnimKind.DashStreak => 22,
            _ => 18,
        };

        /// <summary>CompCyberTracker.Notify_UsedVerb: a melee hit with arm cyberware plays that arm's animation.</summary>
        public static void Notify_VerbUsed(Pawn pawn, Verb verb)
        {
            if (!verb.IsMeleeAttack || !(verb.HediffSource is Hediff_Cyberware cw)) return;
            if (!StyleFor(cw.def, out var kind, out var color)) return;
            if (kind == CyberAnimKind.BladeSlash) RPSound.AtThrottled(RPSoundDefOf.RPC_Snd_MantisDeploy, pawn, 180);   // audio doesn't depend on the visuals setting
            if (!Enabled) return;
            Play(pawn, kind, verb.CurrentTarget, color, SideOf(cw.Part));
        }

        /// <summary>Animation style for arm cyberware, from its def name (mantis / gorilla / monowire, variant colour).</summary>
        public static bool StyleFor(HediffDef def, out CyberAnimKind kind, out Color color)
        {
            string n = def.defName;
            color = n.Contains("Electrifying") ? Electric : n.Contains("Thermal") ? Thermal : n.Contains("Toxic") ? Toxic : Steel;
            if (n.Contains("Mantis")) kind = CyberAnimKind.BladeSlash;
            else if (n.Contains("Gorilla")) kind = CyberAnimKind.Punch;
            else if (n.Contains("Monowire")) kind = CyberAnimKind.WhipLash;
            else { kind = default; return false; }
            return true;
        }

        public static float SideOf(BodyPartRecord part)
        {
            string label = part?.customLabel ?? part?.untranslatedCustomLabel ?? "";
            return label.Contains("left") ? -1f : 1f;
        }

        /// <summary>The pawn's mantis blades (colour of the first pair found), or false.</summary>
        public static bool MantisColor(Pawn pawn, out Color color)
        {
            foreach (var c in pawn.InstalledCyberware())
                if (StyleFor(c.def, out var kind, out color) && kind == CyberAnimKind.BladeSlash) return true;
            color = Steel;
            return false;
        }

        // ================================================================================================ update

        public override void MapComponentUpdate()
        {
            if (!Enabled || Find.CurrentMap != map) return;
            int now = Find.TickManager.TicksGame;
            if (anims.Count > 0 && !loggedDraw)
            {
                loggedDraw = true;
                Diag.Check("visuals", $"animations drawing: {anims.Count} queued, first {anims[0].kind} t={(now - anims[0].start)}/{anims[0].duration}, " +
                                      $"textures blade={Describe(BladeTex)} arc={Describe(ArcTex)} barrel={Describe(BarrelTex)}, shader={ShaderDatabase.Transparent?.name ?? "null"}, alt={AltitudeLayer.MoteOverhead.AltitudeFor():0.##}", "animdraw");
            }
            DrawContinuous();
            for (int i = anims.Count - 1; i >= 0; i--)
            {
                var a = anims[i];
                float t = (now - a.start) / (float)a.duration;
                if (t >= 1f || t < 0f) { anims.RemoveAt(i); continue; }
                if (a.pawn.Spawned) a.from = a.pawn.DrawPos;
                if (a.target != null && a.target.Spawned) a.to = a.target.DrawPos;
                Draw(a, t);
            }
        }

        /// <summary>States read every frame: blades out while in melee, launcher barrel out while aiming.</summary>
        private void DrawContinuous()
        {
            var pawns = map.mapPawns.AllHumanlikeSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p.stances?.curStance is Stance_Warmup w && w.verb is Verb_CastAbility va
                    && va.ability?.CompOfType<CompAbilityEffect_Launcher>() != null && w.focusTarg.IsValid)
                {
                    var to = w.focusTarg.HasThing ? w.focusTarg.Thing.DrawPos : w.focusTarg.Cell.ToVector3Shifted();
                    DrawBarrel(p.DrawPos, to, 1f, 0f);
                }
                if (p.CurJobDef == JobDefOf.AttackMelee && MantisColor(p, out var col))
                {
                    var dir = p.Rotation.FacingCell.ToVector3();
                    DrawBlade(p.DrawPos, dir, -1f, 0.75f, 20f, col, 0.9f);
                    DrawBlade(p.DrawPos, dir, 1f, 0.75f, 20f, col, 0.9f);
                }
            }
        }

        private void Draw(Anim a, float t)
        {
            var dir = a.to - a.from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = a.pawn.Rotation.FacingCell.ToVector3();
            dir.Normalize();
            switch (a.kind)
            {
                case CyberAnimKind.BladeSlash:
                {
                    // extend (first 30%), swing across the target (-55 -> +55 degrees), slash arc mid-swing
                    float extend = Mathf.Clamp01(t / 0.3f);
                    float swing = Mathf.Lerp(-55f, 55f, Mathf.SmoothStep(0f, 1f, t)) * a.side;
                    DrawBlade(a.from, dir, a.side, 0.4f + 0.55f * extend, swing, a.color, 1f - Mathf.Clamp01((t - 0.8f) / 0.2f));
                    if (t > 0.35f)
                    {
                        float k = (t - 0.35f) / 0.65f;
                        Quad(ArcTex, a.color, a.to - dir * 0.15f, dir, 1.3f + 0.3f * k, 1.3f + 0.3f * k, 1f - k, AltitudeLayer.MoteOverhead);
                    }
                    break;
                }
                case CyberAnimKind.Punch:
                {
                    var hit = a.to - dir * 0.3f;
                    Quad(GlowTex, a.color, Vector3.Lerp(a.from, hit, Mathf.Clamp01(t * 3f)), dir, 0.55f, 0.55f, 1f - t, AltitudeLayer.MoteOverhead);
                    if (t > 0.25f)
                    {
                        float k = (t - 0.25f) / 0.75f;
                        Quad(RingTex, a.color, hit, dir, 0.4f + 1.4f * k, 0.4f + 1.4f * k, 1f - k, AltitudeLayer.MoteOverhead);
                    }
                    break;
                }
                case CyberAnimKind.WhipLash:
                {
                    // a bezier from the hand to the target, the middle swinging sideways and settling
                    var perp = new Vector3(dir.z, 0f, -dir.x) * a.side;
                    var hand = a.from + perp * 0.2f;
                    float reach = Mathf.Clamp01(t / 0.35f);
                    var tip = Vector3.Lerp(hand, a.to, reach);
                    var mid = (hand + tip) * 0.5f + perp * Mathf.Sin(t * Mathf.PI * 2f) * 0.8f * (1f - t);
                    float alpha = 1f - Mathf.Clamp01((t - 0.6f) / 0.4f);
                    var prev = hand;
                    for (int i = 1; i <= 12; i++)
                    {
                        float u = i / 12f;
                        var pt = (1 - u) * (1 - u) * hand + 2 * (1 - u) * u * mid + u * u * tip;
                        Line(prev, pt, 0.12f, a.color, alpha * 0.4f);
                        Line(prev, pt, 0.04f, Color.white, alpha);
                        prev = pt;
                    }
                    if (reach >= 1f) Quad(GlowTex, a.color, a.to, dir, 0.6f, 0.6f, alpha, AltitudeLayer.MoteOverhead);
                    break;
                }
                case CyberAnimKind.LauncherFire:
                {
                    // barrel out, kicks back on the shot and settles; flash for the first quarter
                    float recoil = t < 0.15f ? t / 0.15f : 1f - Mathf.Clamp01((t - 0.15f) / 0.5f);
                    DrawBarrel(a.from, a.to, 1f - Mathf.Clamp01((t - 0.75f) / 0.25f), recoil * 0.12f);
                    if (t < 0.25f)
                    {
                        var muzzle = a.from + dir * 0.75f;
                        float s = 0.7f + t * 2f;
                        Quad(FlashTex, FlashColor, muzzle, dir, s, s, 1f - t / 0.25f, AltitudeLayer.MoteOverhead);
                    }
                    break;
                }
                case CyberAnimKind.DashStreak:
                {
                    // a fading streak along the path; blades out at the head of it
                    float head = Mathf.Clamp01(t / 0.6f);
                    var front = Vector3.Lerp(a.from, a.to, head);
                    float alpha = 1f - Mathf.Clamp01((t - 0.5f) / 0.5f);
                    Line(a.from, front, 0.5f, a.color, alpha * 0.35f);
                    Line(a.from, front, 0.15f, Color.white, alpha * 0.8f);
                    DrawBlade(front, dir, -1f, 0.9f, 25f, a.color, alpha);
                    DrawBlade(front, dir, 1f, 0.9f, 25f, a.color, alpha);
                    break;
                }
            }
        }

        // ================================================================================================ primitives

        /// <summary>A blade from the pawn's arm (side -1 left / +1 right), angled <paramref name="spread"/> degrees outwards from <paramref name="dir"/>.</summary>
        private void DrawBlade(Vector3 pawnPos, Vector3 dir, float side, float length, float spread, Color color, float alpha)
        {
            var perp = new Vector3(dir.z, 0f, -dir.x);
            var bladeDir = Quaternion.AngleAxis(spread * side, Vector3.up) * dir;
            var root = pawnPos + perp * 0.22f * side + dir * 0.1f;
            Quad(BladeTex, color, root + bladeDir * (length * 0.5f), bladeDir, 0.22f, length, alpha, AltitudeLayer.MoteOverhead);
        }

        private void DrawBarrel(Vector3 pawnPos, Vector3 target, float alpha, float recoil)
        {
            var dir = target - pawnPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            var perp = new Vector3(dir.z, 0f, -dir.x);
            var pos = pawnPos + perp * 0.18f + dir * (0.42f - recoil);
            Quad(BarrelTex, Color.white, pos, dir, 0.22f, 0.6f, alpha, AltitudeLayer.MoteOverhead);
        }

        private void Line(Vector3 a, Vector3 b, float width, Color color, float alpha)
        {
            var d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.001f || alpha <= 0.01f) return;
            Quad(BaseContent.WhiteTex, color, (a + b) * 0.5f, d / len, width, len + width * 0.5f, alpha, AltitudeLayer.MoteOverhead);
        }

        /// <summary>Draws a texture centred on <paramref name="pos"/>, its top pointing along <paramref name="dir"/>.</summary>
        private void Quad(Texture2D tex, Color color, Vector3 pos, Vector3 dir, float width, float length, float alpha, AltitudeLayer layer)
        {
            if (alpha <= 0.01f) return;
            pos.y = layer.AltitudeFor();
            var rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            var m = Matrix4x4.TRS(pos, rot, new Vector3(width, 1f, length));
            Graphics.DrawMesh(MeshPool.plane10, m, FadedMaterialPool.FadedVersionOf(Mat(tex, color), Mathf.Clamp01(alpha)), 0);
        }

        private Material Mat(Texture2D tex, Color color)
        {
            if (!mats.TryGetValue((tex, color), out var m))
                mats[(tex, color)] = m = MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, color);
            return m;
        }
    }
}
