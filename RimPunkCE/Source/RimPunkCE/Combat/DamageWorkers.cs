using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>Resistance stat per damage family (Warp Dancer: +30-60% resistance to thermal/chemical/electrical).</summary>
    public class DamageResistanceExtension : DefModExtension
    {
        public StatDef resistanceStat;
    }

    public static class DamageResistanceUtility
    {
        public static float Factor(DamageDef def, Thing victim)
        {
            var stat = def.GetModExtension<DamageResistanceExtension>()?.resistanceStat;
            if (stat == null || victim is not Pawn p || p.GetCyberTracker() == null) return 1f;
            return Mathf.Clamp01(1f - p.GetStatValue(stat));
        }
    }

    /// <summary>
    /// Electrical (terms.md): a gradual effect that does more damage to pawns with more cyberware and to
    /// mechanoids, spread evenly over every body part. Implemented as a damage-over-time hediff
    /// (RPC_Electrified) whose severity is the damage still to deal.
    /// </summary>
    public class DamageWorker_Electrical : DamageWorker
    {
        public const float PerCyberwareBonus = 0.1f;
        public const float MechanoidFactor = 1.5f;

        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            var result = new DamageResult();
            float amount = dinfo.Amount * DamageResistanceUtility.Factor(def, victim);
            if (victim is not Pawn pawn)
            {
                // buildings / items: plain damage
                result.totalDamageDealt = Mathf.Min(victim.HitPoints, amount);
                victim.HitPoints -= Mathf.RoundToInt(result.totalDamageDealt);
                if (victim.HitPoints <= 0) victim.Destroy(DestroyMode.KillFinalize);
                return result;
            }
            if (pawn.RaceProps.IsMechanoid) amount *= MechanoidFactor;
            int cyberCount = pawn.GetCyberTracker()?.Cyberware.Count ?? 0;
            amount *= 1f + PerCyberwareBonus * cyberCount;
            DamageOverTimeUtility.AddDamageOverTime(pawn, RPHediffDefOf.RPC_Electrified, amount, dinfo.Instigator);
            result.totalDamageDealt = amount;
            return result;
        }
    }

    /// <summary>Toxic (terms.md): toxic buildup plus steady flat damage to kidneys and lungs (RPC_ToxicBurn DOT).</summary>
    public class DamageWorker_Toxic : DamageWorker
    {
        public const float BuildupPerDamage = 0.01f;

        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            var result = new DamageResult();
            if (victim is not Pawn pawn || pawn.RaceProps.IsMechanoid) return result;
            float amount = dinfo.Amount * DamageResistanceUtility.Factor(def, victim);
            HealthUtility.AdjustSeverity(pawn, HediffDefOf.ToxicBuildup, amount * BuildupPerDamage);
            DamageOverTimeUtility.AddDamageOverTime(pawn, RPHediffDefOf.RPC_ToxicBurn, amount, dinfo.Instigator);
            result.totalDamageDealt = amount;
            return result;
        }
    }

    /// <summary>Thermal (terms.md): sets the target on fire and applies flat burn damage on hit.</summary>
    public class DamageWorker_Thermal : DamageWorker_AddInjury
    {
        public float fireSize = 0.5f;

        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            dinfo.SetAmount(dinfo.Amount * DamageResistanceUtility.Factor(def, victim));
            var result = base.Apply(dinfo, victim);
            if (!victim.Destroyed && victim.Spawned) victim.TryAttachFire(fireSize, dinfo.Instigator);
            return result;
        }
    }

    /// <summary>Shock (terms.md): stun for humanlikes, EMP for mechanoids. Amount = stun seconds x 10.</summary>
    public class DamageWorker_Shock : DamageWorker
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            var result = new DamageResult();
            float amount = dinfo.Amount * DamageResistanceUtility.Factor(def, victim);
            if (victim is Pawn pawn && pawn.RaceProps.IsMechanoid)
            {
                return victim.TakeDamage(new DamageInfo(DamageDefOf.EMP, amount, 0f, -1f, dinfo.Instigator));
            }
            if (victim is Pawn p && p.stances?.stunner != null)
                p.stances.stunner.StunFor(CyberUtility.SecondsToTicks(amount / 10f), dinfo.Instigator);
            return result;
        }
    }

    /// <summary>
    /// Damage over time: severity = damage still to deal. Every interval it deals damagePerTick of
    /// damageDef, either spread evenly over all outside parts (electrical) or on the listed parts (toxic).
    /// </summary>
    public class HediffCompProperties_DamageOverTime : HediffCompProperties
    {
        public DamageDef damageDef;
        public int intervalTicks = 60;
        public float damagePerTick = 2f;
        public bool spreadAcrossParts;
        public List<BodyPartDef> parts;

        public HediffCompProperties_DamageOverTime() => compClass = typeof(HediffComp_DamageOverTime);
    }

    public class HediffComp_DamageOverTime : HediffComp
    {
        public Thing instigator;

        public HediffCompProperties_DamageOverTime Props => (HediffCompProperties_DamageOverTime)props;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            if (!Pawn.IsHashIntervalTick(Props.intervalTicks, delta) || Pawn.Dead) return;
            float dmg = Mathf.Min(parent.Severity, Props.damagePerTick);
            if (dmg <= 0f) return;
            severityAdjustment -= dmg;

            var targets = Targets().ToList();
            if (targets.Count == 0)
            {
                Pawn.TakeDamage(new DamageInfo(Props.damageDef, dmg, 999f, -1f, instigator));
                return;
            }
            float each = dmg / targets.Count;
            foreach (var part in targets)
            {
                if (Pawn.Dead) break;
                Pawn.TakeDamage(new DamageInfo(Props.damageDef, each, 999f, -1f, instigator, part));
            }
        }

        private IEnumerable<BodyPartRecord> Targets()
        {
            var set = Pawn.health.hediffSet;
            if (Props.spreadAcrossParts)
                return set.GetNotMissingParts(depth: BodyPartDepth.Outside).Where(p => p.depth == BodyPartDepth.Outside && p.coverageAbs > 0f);
            if (!Props.parts.NullOrEmpty())
                return set.GetNotMissingParts().Where(p => Props.parts.Contains(p.def));
            return Enumerable.Empty<BodyPartRecord>();
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref instigator, "instigator");
        }
    }

    public static class DamageOverTimeUtility
    {
        public static void AddDamageOverTime(Pawn pawn, HediffDef def, float amount, Thing instigator)
        {
            if (amount <= 0f) return;
            var h = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (h == null)
            {
                h = HediffMaker.MakeHediff(def, pawn);
                h.Severity = amount;
                pawn.health.AddHediff(h);
            }
            else h.Severity += amount;
            var comp = (h as HediffWithComps)?.TryGetComp<HediffComp_DamageOverTime>();
            if (comp != null && instigator != null) comp.instigator = instigator;
        }
    }

    [DefOf]
    public static class RPHediffDefOf
    {
        public static HediffDef RPC_Electrified;
        public static HediffDef RPC_ToxicBurn;

        static RPHediffDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RPHediffDefOf));
    }
}
