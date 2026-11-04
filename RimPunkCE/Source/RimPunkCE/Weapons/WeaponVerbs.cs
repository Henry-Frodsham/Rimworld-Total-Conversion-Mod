using CombatExtended;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Tech weapon projectile. When fired in CE's aimed mode (the "charged" shot, weapons/overview.md) it
    /// passes through walls and other full-cover buildings instead of stopping on them.
    /// </summary>
    public class BulletCE_Tech : BulletCE
    {
        public bool pierceWalls;

        protected override bool CanCollideWith(Thing thing, out float dist)
        {
            if (pierceWalls && thing is Building b && b.def.Fillage == FillCategory.Full)
            {
                dist = -1f;
                return false;
            }
            return base.CanCollideWith(thing, out dist);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pierceWalls, "pierceWalls");
        }
    }

    /// <summary>Tech weapons: the aimed (charged) fire mode makes the shot pierce walls.</summary>
    public class Verb_ShootTech : Verb_ShootCE
    {
        public bool Charged => CompFireModes?.CurrentAimMode == AimMode.AimedShot;

        protected override ProjectileCE SpawnProjectile()
        {
            var proj = base.SpawnProjectile();
            if (proj is BulletCE_Tech tech) tech.pierceWalls = Charged;
            return proj;
        }
    }

    /// <summary>
    /// Smart weapons. With a smart link (Smart Link cyberware or the Smart Imprint tattoo), the guidance
    /// system cancels shooter and weapon inaccuracy: in the open every shot lands. Cover, occlusion, light,
    /// weather and smoke are untouched, so they still cause misses. Without a smart link: a normal gun.
    /// Rounds never seek targets on their own.
    /// </summary>
    public class Verb_ShootSmart : Verb_ShootCE
    {
        public bool SmartLinked => CasterIsPawn && SmartLinkUtility.HasSmartLink(CasterPawn);

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
