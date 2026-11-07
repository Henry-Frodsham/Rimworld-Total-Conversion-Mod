using System.Collections.Generic;
using HarmonyLib;
using CombatExtended;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Tech weapon projectile. When fired in CE's aimed mode (the "charged" shot, weapons/overview.md) it
    /// passes through walls and other full-cover buildings instead of stopping on them, up to
    /// <see cref="Verb_ShootTech.MaxWallsPierced"/> of them; the next one stops it.
    /// </summary>
    public class BulletCE_Tech : BulletCE
    {
        public bool pierceWalls;
        private int wallsPierced;
        private HashSet<int> pierced;

        protected override bool CanCollideWith(Thing thing, out float dist)
        {
            if (pierceWalls && Verb_ShootTech.IsWall(thing))
            {
                pierced ??= new HashSet<int>();
                if (pierced.Contains(thing.thingIDNumber) || wallsPierced < Verb_ShootTech.MaxWallsPierced)
                {
                    if (pierced.Add(thing.thingIDNumber)) wallsPierced++;
                    dist = -1f;
                    return false;
                }
            }
            return base.CanCollideWith(thing, out dist);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pierceWalls, "pierceWalls");
            Scribe_Values.Look(ref wallsPierced, "wallsPierced");
        }
    }

    /// <summary>
    /// Tech weapons do their own firing checks: a charged (aimed mode) shot goes through walls, so a target behind
    /// up to <see cref="MaxWallsPierced"/> walls can be designated and fired at even without line of sight. Every
    /// other CE check (range, min range, violence, shields, roofs) still applies; uncharged shots behave like any gun.
    /// </summary>
    public class Verb_ShootTech : Verb_ShootCE
    {
        /// <summary>Wall cells a charged shot can pass through (thicker than this: mountains, bunkers).</summary>
        public const int MaxWallsPierced = 3;

        public bool Charged => CompFireModes?.CurrentAimMode == AimMode.AimedShot;

        private static readonly System.Reflection.FieldInfo LastExactPos = AccessTools.Field(typeof(Verb_LaunchProjectileCE), "lastExactPos");

        public static bool IsWall(Thing thing) => thing is Building b && b.def.Fillage == FillCategory.Full;

        /// <summary>Full-cover walls between the two cells (endpoints excluded).</summary>
        public static int WallsBetween(Map map, IntVec3 from, IntVec3 to)
        {
            int n = 0;
            foreach (var c in GenSight.PointsOnLineOfSight(from, to))
            {
                if (c == from || c == to || !c.InBounds(map)) continue;
                var e = c.GetEdifice(map);
                if (e != null && e.def.Fillage == FillCategory.Full) n++;
            }
            return n;
        }

        public bool CanPierceTo(IntVec3 root, LocalTargetInfo targ) =>
            Charged && caster?.Map != null && targ.IsValid && targ.Cell.InBounds(caster.Map) && WallsBetween(caster.Map, root, targ.Cell) <= MaxWallsPierced;

        public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo targ, out string report)
        {
            if (base.CanHitTargetFrom(root, targ, out report)) return true;
            // only "no line of sight" is overridden: range, violence, shield and roof failures stand
            if (report != "CE_NoLoS".Translate().ToString() || !CanPierceTo(root, targ)) return false;
            report = "";
            return true;
        }

        /// <summary>No clear shot line: a charged shot fires straight through the walls instead of giving up.</summary>
        protected override bool KeepBurstOnNoShootLine(bool suppressing, out ShootLine shootLine)
        {
            if (currentTarget.IsValid && !currentTarget.ThingDestroyed && CanPierceTo(caster.Position, currentTarget))
            {
                shootLine = new ShootLine(caster.Position, currentTarget.Cell);
                lastShootLine = shootLine;
                SetLastExactPos(currentTarget);
                return true;
            }
            return base.KeepBurstOnNoShootLine(suppressing, out shootLine);
        }

        /// <summary>CE aims at lastExactPos (x, height, z). Set by reflection: CE's Vector3 is UnityEngine.CoreModule's, ours the stubs'.</summary>
        private void SetLastExactPos(LocalTargetInfo target)
        {
            if (LastExactPos == null) return;
            var pos = target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();
            float height = target.Thing is Pawn || target.Thing is Building ? new CollisionVertical(target.Thing).MiddleHeight : 0f;
            try { LastExactPos.SetValue(this, System.Activator.CreateInstance(LastExactPos.FieldType, pos.x, height, pos.z)); }
            catch (System.Exception e) { Diag.Check("weapons", $"tech pierce aim failed: {e.GetType().Name}: {e.Message}", "techaim"); }
        }

        protected override ProjectileCE SpawnProjectile()
        {
            var proj = base.SpawnProjectile();
            if (proj is BulletCE_Tech tech) tech.pierceWalls = Charged;
            return proj;
        }
    }

    /// <summary>
    /// Smart weapons. With a smart link (Smart Link cyberware or the Smart Imprint tattoo), the guidance
    /// system cancels shooter and weapon inaccuracy, and each round launches off to the side and curves onto the
    /// target (CE homing trajectory, BulletCE_Smart; Edgerunners look). Cover, occlusion, light, weather and smoke
    /// are untouched, so they still cause misses. Without a smart link: a normal gun firing straight blue rounds.
    /// </summary>
    public class Verb_ShootSmart : Verb_ShootCE
    {
        /// <summary>Max turn per tick (radians) once homing is fully ramped (CE: from flight tick 3, full by tick 11).</summary>
        private const float HomingAcceleration = 0.2f;

        private static readonly AccessTools.FieldRef<ProjectileCE, BaseTrajectoryWorker> ForcedTrajectoryWorker =
            AccessTools.FieldRefAccess<ProjectileCE, BaseTrajectoryWorker>("forcedTrajectoryWorker");

        protected override ProjectileCE SpawnProjectile()
        {
            var proj = base.SpawnProjectile();
            if (proj is BulletCE_Smart smart && SmartLinked)
            {
                smart.homingAcceleration = HomingAcceleration;
                ForcedTrajectoryWorker(smart) = HomingBulletTrajectoryWorker.Instance;
                // keep CE's legacy flag in step with the forced (ballistic) worker, or CE warns "lerpPosition value changed"
                smart.lerpPosition = false;
                smart.curveDegrees = CurveFor(caster.Position.DistanceTo(currentTarget.Cell));
            }
            return proj;
        }

        /// <summary>
        /// Sideways launch angle. Close targets get little or none: homing only starts on flight tick 3, so a wide
        /// swing at short range would overshoot (tuned by simulation at speed 30, 0.2 rad per tick).
        /// </summary>
        private static float CurveFor(float dist)
        {
            float max = dist < 5f ? 0f : dist < 7f ? 12f : dist < 9f ? 25f : 35f;
            if (max <= 0f) return 0f;
            return (Rand.Bool ? 1f : -1f) * Rand.Range(0.5f, 1f) * max;
        }

        // Weapon Glitch quickhack switches smart targeting off
        public bool SmartLinked => CasterIsPawn && SmartLinkUtility.HasSmartLink(CasterPawn) && !Hediff_Quickhack.TargetHas(CasterPawn, x => x.disablesSmartWeapons);

        // the shot itself uses the (target, cell) overload; the single-argument one forwards to it
        public override ShiftVecReport ShiftVecReportFor(LocalTargetInfo target, IntVec3 targetCell)
        {
            var report = base.ShiftVecReportFor(target, targetCell);
            if (report != null && SmartLinked)
            {
                report.aimingAccuracy = 1f;
                report.sightsEfficiency = 1f;
                report.circularMissRadius = 0f;
                report.spreadDegrees = 0f;
                report.swayDegrees = 0f;
            }
            return report;
        }
    }

    /// <summary>On cyberware HediffDefs that enable smart-targeting (Smart Link, Smart Imprint tattoo).</summary>
    public class SmartLinkExtension : DefModExtension { }

    public static class SmartLinkUtility
    {
        public static bool HasSmartLink(Pawn pawn)
        {
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return false;
            for (int i = 0; i < hediffs.Count; i++)
                if (hediffs[i].def.HasModExtension<SmartLinkExtension>()) return true;
            return false;
        }
    }
}
