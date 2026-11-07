using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    // ------------------------------------------------------------------------------------------------ damage

    /// <summary>
    /// Instant damage (Short Circuit, Synapse Burnout). magnitude = damage. Options: bonus vs mechanoids, bonus per 1%
    /// missing health, t5 extras (Short Circuit: malfunction-stack bonus, EMP DoT, extend control hacks; Synapse
    /// Burnout: +10% per RAM spent).
    /// </summary>
    public class CompProperties_QuickhackDamage : CompProperties_AbilityEffect
    {
        public DamageDef damageDef;
        public float mechanoidFactor = 1f;
        /// <summary>Extra damage fraction per 1% of health the target is missing (Synapse Burnout: 0.03).</summary>
        public float perMissingHealth;
        public float armorPenetration = 999f;

        public bool t5MalfunctionBonus;
        public bool t5Emp;
        public float t5ExtendControlSeconds;
        public float t5PerRamBonus;
        public float t5PerRamBonusMax = 4f;

        public CompProperties_QuickhackDamage() => compClass = typeof(CompAbilityEffect_QuickhackDamage);
    }

    public class CompAbilityEffect_QuickhackDamage : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackDamage Props => (CompProperties_QuickhackDamage)props;

        protected override void ApplyHack(Thing target)
        {
            float dmg = Eval(Ext.magnitude) * DamageFactor;
            var p = target as Pawn;
            if (p != null && p.RaceProps.IsMechanoid) dmg *= Props.mechanoidFactor;
            if (p != null && Props.perMissingHealth > 0f)
                dmg *= 1f + Props.perMissingHealth * 100f * (1f - p.health.summaryHealth.SummaryHealthPercent);
            bool t5 = Tier >= CyberTier.T5;
            if (t5 && p != null)
            {
                if (Props.t5MalfunctionBonus && p.health.hediffSet.GetFirstHediffOfDef(QuickhackDefOf.RPC_QH_CyberwareMalfunction) is Hediff_Quickhack m)
                    dmg *= 1f + 0.1f * m.stacks;
                if (Props.t5PerRamBonus > 0f)
                    dmg *= 1f + Mathf.Min(Props.t5PerRamBonusMax, Props.t5PerRamBonus * (Core?.ramSpent ?? 0f) * (Hack.Overclocked ? 2f : 1f));
                if (Props.t5ExtendControlSeconds > 0f)
                    foreach (var h in p.health.hediffSet.hediffs.OfType<Hediff_Quickhack>())
                        if (h.QExt.blocksCyberwareAbilities || h.QExt.blocksMovementAbilities || h.QExt.disablesSmartWeapons || h.def == QuickhackDefOf.RPC_QH_RebootOptics)
                            h.ExtendBy(CyberUtility.SecondsToTicks(Props.t5ExtendControlSeconds));
            }
            var part = p?.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside).Where(x => x.coverageAbs > 0f).RandomElementWithFallback();
            target.TakeDamage(new DamageInfo(Props.damageDef ?? RPDefOf.RPC_Electrical, dmg, Props.armorPenetration, -1f, Caster, part));
            if (t5 && Props.t5Emp && p != null && !p.Dead)
            {
                if (p.RaceProps.IsMechanoid) p.stances?.stunner?.StunFor(CyberUtility.SecondsToTicks(3f), Caster);
                DamageOverTimeUtility.AddDamageOverTime(p, RPHediffDefOf.RPC_Electrified, dmg * 0.3f, Caster);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ effect hediff

    /// <summary>
    /// Applies (or stacks) the quickhack's effect hediff for its tiered duration (Overheat, Contagion, Cyberware
    /// Malfunction, Cripple Movement, Reboot Optics, Weapon Glitch, Sonic Shock, Bait marker, Ping marker...).
    /// magnitude = DoT per second where the hediff has one. Optional spread to nearby enemies (Contagion).
    /// </summary>
    public class CompProperties_QuickhackHediff : CompProperties_AbilityEffect
    {
        public HediffDef hediff;
        /// <summary>Overheat: also sets the target on fire.</summary>
        public float igniteSize;
        /// <summary>Contagion t5: +15% DoT for every enemy it spreads to.</summary>
        public float t5SpreadBonus;
        /// <summary>Cyberware Malfunction t5: stack thresholds.</summary>
        public bool malfunctionStacks;
        /// <summary>Sonic Shock t5: knocks out targets already hit by Memory Wipe and Reboot Optics.</summary>
        public bool t5SonicKnockout;
        /// <summary>AI: only on targets running cyberware (Malfunction) / carrying a cyberdeck (Sonic Shock).</summary>
        public bool aiNeedsChrome;
        public bool aiNeedsCyberdeck;
        public bool aiNeedsRangedWeapon;

        public CompProperties_QuickhackHediff() => compClass = typeof(CompAbilityEffect_QuickhackHediff);
    }

    public class CompAbilityEffect_QuickhackHediff : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackHediff Props => (CompProperties_QuickhackHediff)props;

        protected override void ApplyHack(Thing target)
        {
            if (!(target is Pawn p) || Props.hediff == null) return;
            int ticks = DurationTicks(Eval(Ext.duration, 10f));
            float mag = Eval(Ext.magnitude) * DamageFactor;
            var targets = new List<Pawn> { p };
            int spread = Mathf.RoundToInt(Eval(Ext.spread));
            if (spread > 0)
                targets.AddRange(QuickhackUtility.SpreadTargets(Caster, p, SpreadRadius, spread, x => QuickhackUtility.ValidTarget(Hack, x, false)));
            for (int i = 0; i < targets.Count; i++)
            {
                float m = mag * (Tier >= CyberTier.T5 && Props.t5SpreadBonus > 0f ? 1f + Props.t5SpreadBonus * i : 1f);
                var h = Hediff_Quickhack.ApplyTo(targets[i], Props.hediff, Tier, Ext.minTier, ticks, m, Caster);
                if (h == null) continue;
                if (Props.igniteSize > 0f && targets[i].Spawned) targets[i].TryAttachFire(Props.igniteSize, Caster);
                if (Props.malfunctionStacks) MalfunctionThresholds(h);
                if (i > 0 && targets[i].Map != null) FleckMaker.ConnectingLine(p.DrawPos, targets[i].DrawPos, FleckDefOf.LineEMP, targets[i].Map, 0.6f);
            }
            if (Props.t5SonicKnockout && Tier >= CyberTier.T5 && p.health.hediffSet.HasHediff(QuickhackDefOf.RPC_QH_MemoryWiped)
                && p.health.hediffSet.HasHediff(QuickhackDefOf.RPC_QH_RebootOptics))
                Hediff_Quickhack.ApplyTo(p, QuickhackDefOf.RPC_QH_SystemCollapse, Tier, CyberTier.T1, CyberUtility.SecondsToTicks(8.8f), 0f, Caster);
        }

        /// <summary>Cyberware Malfunction t5: 2 stacks permanent, 3+ electrical DoT, 5 overload (61 damage) + immune.</summary>
        private void MalfunctionThresholds(Hediff_Quickhack h)
        {
            if (Tier < CyberTier.T5) return;
            if (h.stacks >= 2) h.permanent = true;
            if (h.stacks >= 3) h.magnitude = 0.5f * (h.stacks - 2) * DamageFactor;
            if (h.stacks >= 5)
            {
                h.pawn.TakeDamage(new DamageInfo(RPDefOf.RPC_Electrical, 61f * DamageFactor, 999f, -1f, Caster));
                if (h.pawn.Map != null) FleckMaker.Static(h.pawn.Position, h.pawn.Map, FleckDefOf.ExplosionFlash, 3f);
            }
        }

        protected override bool AIWants(Pawn target)
        {
            if (Props.aiNeedsChrome && !QuickhackUtility.HasAnyChrome(target) && !target.RaceProps.IsMechanoid) return false;
            if (Props.aiNeedsCyberdeck && target.GetCyberTracker()?.HasCyberdeck != true) return false;
            if (Props.aiNeedsRangedWeapon && target.equipment?.Primary?.def.IsRangedWeapon != true) return false;
            return true;
        }
    }

    /// <summary>Weapon Glitch t5: the target's primary weapon is destroyed (they fall back to fists or a sidearm).</summary>
    public class CompProperties_QuickhackDestroyWeapon : CompProperties_AbilityEffect
    {
        public CyberTier fromTier = CyberTier.T5;
        public CompProperties_QuickhackDestroyWeapon() => compClass = typeof(CompAbilityEffect_QuickhackDestroyWeapon);
    }

    public class CompAbilityEffect_QuickhackDestroyWeapon : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackDestroyWeapon Props => (CompProperties_QuickhackDestroyWeapon)props;

        protected override void ApplyHack(Thing target)
        {
            if (Tier < Props.fromTier || !(target is Pawn p) || p.equipment?.Primary == null) return;
            var w = p.equipment.Primary;
            if (p.Map != null) GenExplosion.DoExplosion(p.Position, p.Map, 0.9f, DamageDefOf.Bomb, Caster, 4, 0.5f);
            if (!w.Destroyed) w.Destroy();
        }

        public override bool AICanTargetNow(LocalTargetInfo target) => true; // vanilla needs every comp to agree: the hediff comp decides
    }

    // ------------------------------------------------------------------------------------------------ mental states

    /// <summary>Memory Wipe (wander, t5 every enemy within radius) and the Cyberpsychosis ultimate (timed).</summary>
    public class CompProperties_QuickhackMentalState : CompProperties_AbilityEffect
    {
        public MentalStateDef state;
        public HediffDef markerHediff;
        public float t5Radius;
        public bool needsChrome;
        public CompProperties_QuickhackMentalState() => compClass = typeof(CompAbilityEffect_QuickhackMentalState);
    }

    public class CompAbilityEffect_QuickhackMentalState : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackMentalState Props => (CompProperties_QuickhackMentalState)props;

        protected override void ApplyHack(Thing target)
        {
            if (!(target is Pawn p)) return;
            var victims = new List<Pawn> { p };
            if (Tier >= CyberTier.T5 && Props.t5Radius > 0f && p.Map != null)
                victims.AddRange(p.Map.mapPawns.AllPawnsSpawned.Where(x => x != p && x.HostileTo(Caster) && !x.Downed && x.Position.InHorDistOf(p.Position, Props.t5Radius)));
            int ticks = DurationTicks(Eval(Ext.duration, 10f));
            foreach (var v in victims)
            {
                if (!CanAffect(v)) continue;
                if (Props.markerHediff != null) Hediff_Quickhack.ApplyTo(v, Props.markerHediff, Tier, Ext.minTier, ticks, 0f, Caster);
                if (v.mindState?.mentalStateHandler?.TryStartMentalState(Props.state, "RPC_QuickhackReason".Translate(Caster.LabelShort), forced: true, forceWake: true) == true)
                {
                    if (v.MentalState is MentalState_HackedCyberpsychosis hacked)
                    {
                        hacked.durationTicks = ticks;
                        hacked.bonusPerQuickhack = Tier >= CyberTier.T5 ? 0.25f : 0f;
                    }
                    else if (v.MentalState is MentalState_TimedWander wander) wander.durationTicks = ticks;
                    v.mindState.enemyTarget = null;
                }
            }
        }

        private bool CanAffect(Pawn v)
        {
            if (v.Dead || v.RaceProps.IsMechanoid || v.InMentalState && CyberpsychosisUtility.InCyberpsychosis(v)) return false;
            if (Props.needsChrome && (!QuickhackUtility.HasAnyChrome(v) || CyberpsychosisUtility.IsMaxTac(v))) return false;
            return true;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages)) return false;
            if (target.Thing is Pawn p && !CanAffect(p))
            {
                if (throwMessages) Messages.Message("RPC_QuickhackCantAffect".Translate(), p, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return true;
        }

        protected override bool AIWants(Pawn target) => CanAffect(target) && !target.InMentalState;
    }

    /// <summary>Memory Wipe: wanders for the quickhack's duration, then snaps back.</summary>
    public class MentalState_TimedWander : MentalState_WanderPsychotic
    {
        public int durationTicks = 600;

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (age >= durationTicks) RecoverFromState();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref durationTicks, "rpcDuration", 600);
        }
    }

    // ------------------------------------------------------------------------------------------------ lures

    /// <summary>Bait: the target walks to the netrunner. Request Backup: nearby allies of the target walk to it.</summary>
    public class CompProperties_QuickhackLure : CompProperties_AbilityEffect
    {
        /// <summary>false = Bait (target -> caster), true = Request Backup (target's allies -> target).</summary>
        public bool backup;
        public float backupRadius = 20f;
        public int backupCount = 1;
        public int backupCountIfBaited = 4;
        public CompProperties_QuickhackLure() => compClass = typeof(CompAbilityEffect_QuickhackLure);
    }

    public class CompAbilityEffect_QuickhackLure : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackLure Props => (CompProperties_QuickhackLure)props;

        protected override void ApplyHack(Thing target)
        {
            if (!(target is Pawn p) || p.Map == null) return;
            int ticks = DurationTicks(Eval(Ext.duration, 15f));
            if (!Props.backup)
            {
                Walk(p, Caster.Position, ticks);
                return;
            }
            int count = p.health.hediffSet.HasHediff(QuickhackDefOf.RPC_QH_Baited) ? Props.backupCountIfBaited : Props.backupCount;
            var allies = p.Map.mapPawns.AllPawnsSpawned
                .Where(x => x != p && !x.Dead && !x.Downed && x.Faction == p.Faction && x.Position.InHorDistOf(p.Position, Props.backupRadius))
                .OrderBy(x => x.Position.DistanceToSquared(p.Position)).Take(count);
            foreach (var a in allies) Walk(a, p.Position, ticks);
        }

        private static void Walk(Pawn p, IntVec3 to, int ticks)
        {
            var cell = CellFinder.StandableCellNear(to, p.Map, 3f);
            if (!cell.IsValid || !p.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return;
            var job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            job.expiryInterval = ticks;
            job.checkOverrideOnExpire = true;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }
    }

    // ------------------------------------------------------------------------------------------------ ultimates

    /// <summary>System Collapse: knocked out (consciousness crushed) for the duration; mechanoids are stunned.</summary>
    public class CompProperties_QuickhackKnockout : CompProperties_AbilityEffect
    {
        public HediffDef hediff;
        public CompProperties_QuickhackKnockout() => compClass = typeof(CompAbilityEffect_QuickhackKnockout);
    }

    public class CompAbilityEffect_QuickhackKnockout : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackKnockout Props => (CompProperties_QuickhackKnockout)props;

        protected override void ApplyHack(Thing target)
        {
            if (!(target is Pawn p)) return;
            int ticks = DurationTicks(Eval(Ext.duration, 8.8f));
            if (p.RaceProps.IsMechanoid) p.stances?.stunner?.StunFor(ticks, Caster);
            else Hediff_Quickhack.ApplyTo(p, Props.hediff, Tier, Ext.minTier, ticks, 0f, Caster);
            // t5: knocking out an enemy netrunner wipes NetWatch trace
            if (Tier >= CyberTier.T5 && p.GetCyberTracker()?.HasCyberdeck == true && Caster.GetCyberTracker() is CompCyberTracker t)
                t.lastHighTierQuickhackTick = -999999;
        }
    }

    /// <summary>Detonate Grenade: the target's grenade (or a frag) explodes in its hands; mechanoids lose a weak spot.</summary>
    public class CompProperties_QuickhackDetonate : CompProperties_AbilityEffect
    {
        public float radius = 2.4f;
        public int damage = 30;
        public int mechanoidDamage = 40;
        public CompProperties_QuickhackDetonate() => compClass = typeof(CompAbilityEffect_QuickhackDetonate);
    }

    public class CompAbilityEffect_QuickhackDetonate : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackDetonate Props => (CompProperties_QuickhackDetonate)props;

        protected override void ApplyHack(Thing target)
        {
            if (!(target is Pawn p) || p.Map == null) return;
            float f = DamageFactor;
            if (p.RaceProps.IsMechanoid)
            {
                p.TakeDamage(new DamageInfo(DamageDefOf.Bomb, Props.mechanoidDamage * f, 2f, -1f, Caster));
                if (p.Map != null) FleckMaker.Static(p.Position, p.Map, FleckDefOf.ExplosionFlash, 4f);
                return;
            }
            // a grenade they carry goes off (consumed); otherwise a frag-sized blast
            var grenade = p.inventory?.innerContainer.FirstOrDefault(t => WeaponClassUtility.IsGrenade(t))
                          ?? (p.equipment?.Primary != null && WeaponClassUtility.IsGrenade(p.equipment.Primary) ? p.equipment.Primary : null);
            if (grenade != null)
            {
                if (grenade.stackCount > 1) grenade.SplitOff(1).Destroy();
                else grenade.Destroy();
            }
            GenExplosion.DoExplosion(p.Position, p.Map, Props.radius, DamageDefOf.Bomb, Caster, Mathf.RoundToInt(Props.damage * f), 1f, intendedTarget: p);
        }
    }

    /// <summary>Suicide: a humanlike with a gun shoots itself; without one it beats itself down.</summary>
    public class CompProperties_QuickhackExecute : CompProperties_AbilityEffect
    {
        public float t5HealOnKill = 0.1f;
        public CompProperties_QuickhackExecute() => compClass = typeof(CompAbilityEffect_QuickhackExecute);
    }

    public class CompAbilityEffect_QuickhackExecute : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackExecute Props => (CompProperties_QuickhackExecute)props;

        protected override void ApplyHack(Thing target)
        {
            if (!(target is Pawn p) || !p.RaceProps.Humanlike) return;
            var gun = p.equipment?.Primary;
            if (gun != null && gun.def.IsRangedWeapon)
            {
                var head = p.health.hediffSet.GetNotMissingParts().FirstOrDefault(x => x.def == BodyPartDefOf.Head);
                p.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 999f, 999f, -1f, Caster, head, gun.def));
                if (!p.Dead) p.Kill(new DamageInfo(DamageDefOf.Bullet, 999f, 999f, -1f, Caster, head, gun.def));
            }
            else if (!p.Downed) HealthUtility.DamageUntilDowned(p, allowBleedingWounds: false);
            if (p.Dead && Tier >= CyberTier.T5 && Props.t5HealOnKill > 0f) CyberUtility.HealFraction(Caster, Props.t5HealOnKill);
        }

        protected override bool AIWants(Pawn target) => target.RaceProps.Humanlike && target.equipment?.Primary?.def.IsRangedWeapon == true;
    }

    /// <summary>Ping: reveals (unfogs) every enemy within the radius of the target and marks them for the duration.</summary>
    public class CompProperties_QuickhackReveal : CompProperties_AbilityEffect
    {
        public float radius = 20f;
        public HediffDef markerHediff;
        public CompProperties_QuickhackReveal() => compClass = typeof(CompAbilityEffect_QuickhackReveal);
    }

    public class CompAbilityEffect_QuickhackReveal : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackReveal Props => (CompProperties_QuickhackReveal)props;

        protected override void ApplyHack(Thing target)
        {
            if (!(target is Pawn p) || p.Map == null) return;
            int ticks = DurationTicks(Eval(Ext.duration, 8f));
            foreach (var e in p.Map.mapPawns.AllPawnsSpawned.Where(x => x.Faction == p.Faction && x.Position.InHorDistOf(p.Position, Props.radius)).ToList())
            {
                if (Props.markerHediff != null) Hediff_Quickhack.ApplyTo(e, Props.markerHediff, Tier, Ext.minTier, ticks, 0f, Caster);
                RevealUtility.UnfogAround(p.Map, e.Position, 2);
            }
        }
    }

    /// <summary>
    /// Blackwall Gateway: lethal damage to the target's nervous system and chrome, then jumps to up to N enemies within
    /// the radius, each jump costing +4 RAM (stops when out of RAM, unless overclocked). Mechanoids are shut down.
    /// </summary>
    public class CompProperties_QuickhackBlackwall : CompProperties_AbilityEffect
    {
        public float damage = 60f;
        public int jumps = 5;
        public float jumpRadius = 20f;
        public float ramPerJump = 4f;
        public float mechStunSeconds = 10f;
        public CompProperties_QuickhackBlackwall() => compClass = typeof(CompAbilityEffect_QuickhackBlackwall);
    }

    public class CompAbilityEffect_QuickhackBlackwall : CompAbilityEffect_QuickhackEffect
    {
        public new CompProperties_QuickhackBlackwall Props => (CompProperties_QuickhackBlackwall)props;

        protected override void ApplyHack(Thing target)
        {
            if (!(target is Pawn first)) return;
            var hit = new HashSet<Pawn>();
            Pawn current = first;
            for (int jump = 0; current != null && jump <= Props.jumps; jump++)
            {
                if (jump > 0)
                {
                    var t = Caster.GetCyberTracker();
                    if (t == null || (t.Ram < Props.ramPerJump && !Hack.Overclocked)) break;
                    QuickhackUtility.SpendRam(Caster, Props.ramPerJump);
                }
                hit.Add(current);
                Strike(current);
                var next = QuickhackUtility.SpreadTargets(Caster, current, Props.jumpRadius, 1, x => !hit.Contains(x)).FirstOrDefault();
                if (next != null && current.Map != null) FleckMaker.ConnectingLine(current.DrawPos, next.DrawPos, FleckDefOf.LineEMP, current.Map, 1f);
                current = next;
            }
        }

        private void Strike(Pawn p)
        {
            if (p.RaceProps.IsMechanoid)
            {
                p.stances?.stunner?.StunFor(CyberUtility.SecondsToTicks(Props.mechStunSeconds), Caster);
                p.TakeDamage(new DamageInfo(DamageDefOf.EMP, 20f, 0f, -1f, Caster));
                return;
            }
            float total = Props.damage * DamageFactor;
            var parts = new List<BodyPartRecord>();
            var brain = p.health.hediffSet.GetBrain();
            if (brain != null) parts.Add(brain);
            parts.AddRange(p.InstalledCyberware().Select(c => c.Part).Where(x => x != null && x != brain).Distinct().Take(3));
            if (parts.Count == 0) parts.Add(null);
            foreach (var part in parts)
            {
                if (p.Dead) break;
                p.TakeDamage(new DamageInfo(RPDefOf.RPC_Electrical, total / parts.Count, 999f, -1f, Caster, part));
            }
            if (p.Map != null) FleckMaker.Static(p.Position, p.Map, FleckDefOf.PsycastAreaEffect, 2f);
        }
    }
}
