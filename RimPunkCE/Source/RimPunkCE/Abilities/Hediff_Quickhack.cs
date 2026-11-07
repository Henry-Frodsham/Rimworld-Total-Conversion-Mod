using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>Behaviour of a quickhack effect hediff (stat/capacity debuffs come from its ActiveEffectExtension).</summary>
    public class QuickhackHediffExtension : DefModExtension
    {
        /// <summary>Damage over time: <see cref="Hediff_Quickhack.magnitude"/> per second of this damage.</summary>
        public DamageDef dotDamage;
        /// <summary>Spread the DoT over outer parts (true) or hit one random outer part per second.</summary>
        public bool dotWholeBody = true;

        public int maxStacks = 1;
        /// <summary>Max stacks from tier 5 on (Contagion: 2).</summary>
        public int maxStacksT5;
        /// <summary>Incoming damage per stack (Cyberware Malfunction: 0.05 = +5%).</summary>
        public float incomingDamagePerStack;

        public bool blocksCyberwareAbilities;
        public bool blocksMovementAbilities;
        public bool blocksQuickhacks;
        public bool disablesSmartWeapons;

        /// <summary>Uploading again refreshes the duration instead of being blocked.</summary>
        public bool refreshOnReapply;
    }

    /// <summary>
    /// A quickhack's lasting effect on its target: tiered debuffs (via Hediff_CyberActive's stage), countdown, stacks,
    /// damage over time. The instigator is kept so DoT kills credit the netrunner.
    /// </summary>
    public class Hediff_Quickhack : Hediff_CyberActive
    {
        public float magnitude;
        public int stacks = 1;
        public Thing instigator;
        /// <summary>Cyberware Malfunction t5: from 2 stacks the effect never times out.</summary>
        public bool permanent;

        private QuickhackHediffExtension qExtCached;
        public QuickhackHediffExtension QExt => qExtCached ??= def.GetModExtension<QuickhackHediffExtension>() ?? new QuickhackHediffExtension();

        public int MaxStacks => tier >= CyberTier.T5 && QExt.maxStacksT5 > 0 ? QExt.maxStacksT5 : QExt.maxStacks;
        public bool CanStack => stacks < MaxStacks || QExt.refreshOnReapply;

        // permanent (malfunction t5) lasts until the pawn leaves the map
        public override bool ShouldRemove => (permanent ? !pawn.Spawned : ticksLeft <= 0) || pawn.Dead;

        public override string LabelInBrackets
        {
            get
            {
                string s = permanent ? "∞" : (ticksLeft / 60f).ToString("0.0") + "s";
                return stacks > 1 ? $"x{stacks}, {s}" : s;
            }
        }

        public override float IncomingDamageFactor => base.IncomingDamageFactor * (1f + QExt.incomingDamagePerStack * stacks);

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (permanent) ticksLeft = Mathf.Max(ticksLeft, 60);
            var dot = QExt.dotDamage;
            if (dot == null || magnitude <= 0f || !pawn.IsHashIntervalTick(60, delta) || pawn.Dead) return;
            float amount = magnitude * stacks;
            var parts = pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside).Where(p => p.coverageAbs > 0f).ToList();
            if (parts.Count == 0) return;
            if (!QExt.dotWholeBody)
            {
                pawn.TakeDamage(new DamageInfo(dot, amount, 999f, -1f, instigator, parts.RandomElement()));
                return;
            }
            int hits = Mathf.Clamp(Mathf.CeilToInt(amount), 1, 4);
            for (int i = 0; i < hits && !pawn.Dead; i++)
                pawn.TakeDamage(new DamageInfo(dot, amount / hits, 999f, -1f, instigator, parts.RandomElement()));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref magnitude, "magnitude");
            Scribe_Values.Look(ref stacks, "stacks", 1);
            Scribe_Values.Look(ref permanent, "permanent");
            Scribe_References.Look(ref instigator, "instigator");
        }

        /// <summary>Apply or stack a quickhack hediff. Returns it (null if the target can't take it).</summary>
        public static Hediff_Quickhack ApplyTo(Pawn target, HediffDef def, CyberTier tier, CyberTier minTier, int durationTicks, float magnitude, Thing instigator)
        {
            if (target?.health == null || target.Dead) return null;
            if (target.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_Quickhack existing)
            {
                if (existing.stacks < existing.MaxStacks) existing.stacks++;
                existing.ticksLeft = Mathf.Max(existing.ticksLeft, durationTicks);
                existing.magnitude = Mathf.Max(existing.magnitude, magnitude);
                existing.instigator = instigator ?? existing.instigator;
                target.health.Notify_HediffChanged(existing);
                return existing;
            }
            var h = (Hediff_Quickhack)HediffMaker.MakeHediff(def, target);
            h.Setup(tier, minTier, CyberTier.Iconic, durationTicks);
            h.magnitude = magnitude;
            h.instigator = instigator;
            target.health.AddHediff(h);
            return h;
        }

        public static bool TargetHas(Pawn p, System.Func<QuickhackHediffExtension, bool> flag) =>
            p?.health?.hediffSet?.hediffs.Any(h => h is Hediff_Quickhack q && flag(q.QExt)) == true;
    }
}
