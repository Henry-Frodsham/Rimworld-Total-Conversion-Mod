using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    // =====================================================================================================
    // Generic data-driven cyberware behaviour, so most of the manifest entries are XML only:
    //   - HediffComp_CyberTriggers:     "when X happens, do Y" (neutralize, damage, dodge, health drops ...)
    //   - HediffComp_CyberConditionals: "while X, gain Y" (standing still, low health, no enemies near ...)
    //   - HediffComp_CyberOnHit:        extra effects on hits with this cyberware's own weapon (arms)
    // Every tiered number is read from the cyberware's <values> by key (CyberwareExtension.GetValue).
    // =====================================================================================================

    public enum CyberTriggerKind
    {
        Neutralized,          // downed or killed an enemy
        DealtDamage,          // hit a hostile pawn
        TookDamage,
        HealthBelow,          // summary health crossed below thresholdKey (fraction)
        HealthLostWithin,     // lost thresholdKey (fraction) of health within windowKey seconds
        EnteredCombat,
        Dodged,               // dodged a melee attack
        ActiveEnded,          // activeDef effect ended (Kerenzikov -> Defenzikov)
        RamBelow,             // available RAM fell to thresholdKey (fraction of max)
        UsedGrenade,          // threw a grenade
        OtherAbilityUsed,     // another cyberware ability went on cooldown (Quantum Tuner)
    }

    public enum CyberWeaponFilter
    {
        Any,
        Melee,
        Ranged,
        Tech,
        Power,
        Smart,
        Blade,
        Grenade,
    }

    public enum CyberEffectKind
    {
        ApplyActive,          // timed effect hediff (durationKey seconds)
        HealFraction,         // heal each injury by valueKey of its severity ("+x% health")
        HealFlat,             // heal valueKey HP spread over injuries
        Adrenaline,           // +valueKey adrenaline (fraction of max)
        RamFraction,          // +valueKey of max RAM
        RamFlat,              // +valueKey RAM
        RamPerDamage,         // +valueKey x damage taken as RAM
        ReduceCooldowns,      // reduce all cyberware ability cooldowns by valueKey (fraction)
        Electroshock,         // valueKey electrical damage to hostiles within radius of the pawn (or the victim)
        RestoreOtherCooldown, // Quantum Tuner: take up to valueKey seconds off the ability that was just used
    }

    public class CyberTriggerEffect
    {
        public CyberEffectKind kind;
        public HediffDef hediff;
        public string valueKey;
        public float value;
        public string durationKey = "duration";
        public float radius = 4f;
        /// <summary>Electroshock: centre on the victim instead of the pawn.</summary>
        public bool atVictim;
    }

    public class CyberTrigger
    {
        public CyberTriggerKind on;
        public CyberWeaponFilter weapon = CyberWeaponFilter.Any;
        public string chanceKey;
        public float chance = 1f;
        public string thresholdKey;
        public float threshold;
        public string windowKey;
        public string cooldownKey;
        public float cooldown;
        public HediffDef activeDef;
        public List<CyberTriggerEffect> effects = new List<CyberTriggerEffect>();
    }

    /// <summary>What happened, for the trigger filters and effects.</summary>
    public struct CyberTriggerContext
    {
        public Pawn victim;
        public DamageInfo? dinfo;
        public float amount;
        public HediffDef activeDef;
        public CyberwareAbility ability;

        public static CyberTriggerContext None => default;
    }

    public class HediffCompProperties_CyberTriggers : HediffCompProperties
    {
        public List<CyberTrigger> triggers = new List<CyberTrigger>();

        public HediffCompProperties_CyberTriggers() => compClass = typeof(HediffComp_CyberTriggers);
    }

    public class HediffComp_CyberTriggers : HediffComp, ICyberEventReceiver
    {
        private List<int> lastFired = new List<int>();
        private List<bool> armed = new List<bool>();
        // recent health losses for HealthLostWithin: (tick, fraction of summary health)
        private readonly List<(int tick, float lost)> recentLoss = new List<(int, float)>();

        public HediffCompProperties_CyberTriggers Props => (HediffCompProperties_CyberTriggers)props;

        private Hediff_Cyberware Cyber => parent as Hediff_Cyberware;

        private float Value(string key, float fallback) => key.NullOrEmpty() ? fallback : (Cyber?.GetValue(key, fallback) ?? fallback);

        private void EnsureLists()
        {
            int n = Props.triggers.Count;
            while (lastFired.Count < n) lastFired.Add(-999999);
            while (armed.Count < n) armed.Add(true);
        }

        public void Notify_DealtDamage(Thing victim, DamageInfo dinfo, float damageDealt)
        {
            if (victim is not Pawn vp || !vp.HostileTo(Pawn)) return;
            Fire(CyberTriggerKind.DealtDamage, new CyberTriggerContext { victim = vp, dinfo = dinfo, amount = damageDealt });
        }

        public void Notify_Neutralized(Pawn victim, bool killed) =>
            Fire(CyberTriggerKind.Neutralized, new CyberTriggerContext { victim = victim, dinfo = CyberEvents.LastNeutralizeDinfo });

        public void Notify_TookDamage(DamageInfo dinfo, float damageTaken)
        {
            var hs = Pawn.health?.summaryHealth;
            if (hs != null && damageTaken > 0f)
            {
                // summary health drop is not directly the damage amount: approximate with damage / a human-sized pool
                recentLoss.Add((Find.TickManager.TicksGame, damageTaken / Mathf.Max(1f, 100f * Pawn.HealthScale)));
            }
            Fire(CyberTriggerKind.TookDamage, new CyberTriggerContext { dinfo = dinfo, amount = damageTaken });
            CheckHealthTriggers();
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            if (!Pawn.IsHashIntervalTick(30, delta) || Pawn.Dead) return;
            CheckHealthTriggers();
            CheckRamTriggers();
        }

        private void CheckHealthTriggers()
        {
            EnsureLists();
            float health = Pawn.health.summaryHealth.SummaryHealthPercent;
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < Props.triggers.Count; i++)
            {
                var t = Props.triggers[i];
                if (t.on == CyberTriggerKind.HealthBelow)
                {
                    float threshold = Value(t.thresholdKey, t.threshold);
                    if (health >= threshold) armed[i] = true;
                    else if (armed[i] && TryFireAt(i, CyberTriggerContext.None)) armed[i] = false;
                }
                else if (t.on == CyberTriggerKind.HealthLostWithin)
                {
                    int window = CyberUtility.SecondsToTicks(Value(t.windowKey, 30f));
                    recentLoss.RemoveAll(l => now - l.tick > window);
                    float lost = 0f;
                    foreach (var l in recentLoss) lost += l.lost;
                    if (lost >= Value(t.thresholdKey, t.threshold) && TryFireAt(i, CyberTriggerContext.None)) recentLoss.Clear();
                }
            }
        }

        private void CheckRamTriggers()
        {
            var tracker = Pawn.GetCyberTracker();
            if (tracker == null) return;
            float max = tracker.RamMax;
            if (max <= 0f) return;
            EnsureLists();
            for (int i = 0; i < Props.triggers.Count; i++)
            {
                var t = Props.triggers[i];
                if (t.on != CyberTriggerKind.RamBelow) continue;
                float frac = tracker.Ram / max;
                if (frac > Value(t.thresholdKey, t.threshold)) armed[i] = true;
                else if (armed[i] && TryFireAt(i, CyberTriggerContext.None)) armed[i] = false;
            }
        }

        /// <summary>Fire every trigger of this kind whose filters pass.</summary>
        public void Fire(CyberTriggerKind kind, CyberTriggerContext ctx)
        {
            EnsureLists();
            for (int i = 0; i < Props.triggers.Count; i++)
            {
                var t = Props.triggers[i];
                if (t.on != kind) continue;
                if (t.activeDef != null && ctx.activeDef != null && t.activeDef != ctx.activeDef) continue;
                if (kind == CyberTriggerKind.OtherAbilityUsed && ctx.ability?.Source == parent) continue;
                if (!WeaponMatches(t.weapon, ctx)) continue;
                TryFireAt(i, ctx);
            }
        }

        private static bool WeaponMatches(CyberWeaponFilter filter, CyberTriggerContext ctx)
        {
            if (filter == CyberWeaponFilter.Any) return true;
            if (ctx.dinfo == null) return false;
            return WeaponClassUtility.Matches(filter, ctx.dinfo.Value);
        }

        private bool TryFireAt(int i, CyberTriggerContext ctx)
        {
            var t = Props.triggers[i];
            int now = Find.TickManager.TicksGame;
            float cd = Value(t.cooldownKey, t.cooldown);
            if (cd > 0f && now - lastFired[i] < CyberUtility.SecondsToTicks(cd * Pawn.GetStatValue(RPStatDefOf.RPC_CyberwareCooldownFactor))) return false;
            float chance = Value(t.chanceKey, t.chance);
            if (chance < 1f && !Rand.Chance(chance)) return false;
            lastFired[i] = now;
            foreach (var e in t.effects) Apply(e, ctx);
            Diag.Count("trigger-" + t.on);
            return true;
        }

        public bool OnCooldown(int triggerIndex)
        {
            EnsureLists();
            var t = Props.triggers[triggerIndex];
            float cd = Value(t.cooldownKey, t.cooldown);
            return cd > 0f && Find.TickManager.TicksGame - lastFired[triggerIndex] < CyberUtility.SecondsToTicks(cd);
        }

        private void Apply(CyberTriggerEffect e, CyberTriggerContext ctx)
        {
            var pawn = Pawn;
            var tracker = pawn.GetCyberTracker();
            float v = Value(e.valueKey, e.value);
            switch (e.kind)
            {
                case CyberEffectKind.ApplyActive:
                    if (e.hediff != null && Cyber != null)
                        CyberActiveUtility.Apply(pawn, e.hediff, Cyber, CyberUtility.SecondsToTicks(Value(e.durationKey, 10f)));
                    break;
                case CyberEffectKind.HealFraction:
                    CyberUtility.HealFraction(pawn, v);
                    break;
                case CyberEffectKind.HealFlat:
                    CyberUtility.HealFlat(pawn, v);
                    break;
                case CyberEffectKind.Adrenaline:
                    tracker?.GainAdrenalineFraction(v);
                    break;
                case CyberEffectKind.RamFraction:
                    tracker?.GainRam(v * tracker.RamMax);
                    break;
                case CyberEffectKind.RamFlat:
                    tracker?.GainRam(v);
                    break;
                case CyberEffectKind.RamPerDamage:
                    tracker?.GainRam(v * ctx.amount);
                    break;
                case CyberEffectKind.ReduceCooldowns:
                    foreach (var a in pawn.CyberAbilities()) a.ReduceCooldownFraction(v);
                    break;
                case CyberEffectKind.Electroshock:
                {
                    var center = e.atVictim && ctx.victim != null && ctx.victim.Spawned ? ctx.victim.Position : pawn.Position;
                    CyberUtility.Electroshock(pawn, center, pawn.Map, e.radius, v);
                    break;
                }
                case CyberEffectKind.RestoreOtherCooldown:
                    ctx.ability?.ReduceCooldownTicks(CyberUtility.SecondsToTicks(v));
                    break;
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref lastFired, "rpcLastFired", LookMode.Value);
            Scribe_Collections.Look(ref armed, "rpcArmed", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                lastFired ??= new List<int>();
                armed ??= new List<bool>();
            }
        }
    }

    // ----------------------------------------------------------------------------------------- conditionals

    public enum CyberConditionKind
    {
        HealthBelow,      // summary health below threshold (fraction)
        StandingStill,
        NoEnemiesWithin,  // no hostile pawn within threshold tiles
        AdrenalineAbove,  // adrenaline above threshold (fraction)
        RamBelow,         // available RAM below threshold (flat)
        ActivePresent,    // activeDef effect running (Kerenzikov Boost System)
        WieldingBlade,
        MovingFresh,      // moving for less than threshold seconds (recovers at the same rate when still)
        Always,
    }

    public class CyberConditional
    {
        public CyberConditionKind when;
        public string thresholdKey;
        public float threshold;
        public HediffDef activeDef;
        /// <summary>Effect hediff (RPC_CyberActiveBase) kept on the pawn while the condition holds.</summary>
        public HediffDef effect;
    }

    public class HediffCompProperties_CyberConditionals : HediffCompProperties
    {
        public List<CyberConditional> conditionals = new List<CyberConditional>();

        public HediffCompProperties_CyberConditionals() => compClass = typeof(HediffComp_CyberConditionals);
    }

    public class HediffComp_CyberConditionals : HediffComp
    {
        private const int Interval = 30;
        private int movingTicks;

        public HediffCompProperties_CyberConditionals Props => (HediffCompProperties_CyberConditionals)props;

        private Hediff_Cyberware Cyber => parent as Hediff_Cyberware;

        private float Value(string key, float fallback) => key.NullOrEmpty() ? fallback : (Cyber?.GetValue(key, fallback) ?? fallback);

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            if (!Pawn.IsHashIntervalTick(Interval, delta) || Pawn.Dead || Cyber == null) return;
            if (Pawn.pather != null && Pawn.pather.Moving) movingTicks += Interval;
            else movingTicks = Mathf.Max(0, movingTicks - Interval);
            foreach (var c in Props.conditionals)
            {
                if (c.effect == null || !Holds(c)) continue;
                // refreshed every check: the effect lapses ~1s after the condition stops holding
                CyberActiveUtility.Apply(Pawn, c.effect, Cyber, Interval * 2 + 5, refreshOnly: true);
            }
        }

        private bool Holds(CyberConditional c)
        {
            var pawn = Pawn;
            float th = Value(c.thresholdKey, c.threshold);
            switch (c.when)
            {
                case CyberConditionKind.Always: return true;
                case CyberConditionKind.HealthBelow: return pawn.health.summaryHealth.SummaryHealthPercent < th;
                case CyberConditionKind.StandingStill: return pawn.Spawned && (pawn.pather == null || !pawn.pather.Moving);
                case CyberConditionKind.NoEnemiesWithin: return pawn.Spawned && !CyberUtility.AnyHostileWithin(pawn, th);
                case CyberConditionKind.AdrenalineAbove:
                {
                    var a = pawn.GetCyberTracker();
                    return a != null && a.AdrenalineFraction > th;
                }
                case CyberConditionKind.RamBelow:
                {
                    var t = pawn.GetCyberTracker();
                    return t != null && t.RamMax > 0f && t.Ram < th;
                }
                case CyberConditionKind.ActivePresent: return c.activeDef != null && pawn.health.hediffSet.HasHediff(c.activeDef);
                case CyberConditionKind.WieldingBlade: return WeaponClassUtility.IsBlade(pawn.equipment?.Primary);
                case CyberConditionKind.MovingFresh: return pawn.pather != null && pawn.pather.Moving && movingTicks < CyberUtility.SecondsToTicks(th);
            }
            return false;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref movingTicks, "rpcMovingTicks");
        }
    }

    // ---------------------------------------------------------------------------------------------- on-hit

    /// <summary>
    /// Extra effects when this cyberware's own weapon (arm cyberware tools, monowire lash) hits:
    /// a guaranteed rider of damage type <see cref="riderDamage"/> (fraction of the hit), plus chances to
    /// shock, ignite, poison or open a bleeding wound. Chances are value keys (fractions).
    /// </summary>
    public class HediffCompProperties_CyberOnHit : HediffCompProperties
    {
        public DamageDef riderDamage;
        public float riderFraction = 0.3f;
        public string shockChanceKey;
        public string burnChanceKey;
        public string poisonChanceKey;
        public string bleedChanceKey;

        public HediffCompProperties_CyberOnHit() => compClass = typeof(HediffComp_CyberOnHit);
    }

    public class HediffComp_CyberOnHit : HediffComp, ICyberEventReceiver
    {
        public HediffCompProperties_CyberOnHit Props => (HediffCompProperties_CyberOnHit)props;

        public void Notify_DealtDamage(Thing victim, DamageInfo dinfo, float damageDealt)
        {
            if (dinfo.WeaponLinkedHediff != parent.def || CyberOnHitUtility.Applying) return;
            CyberOnHitUtility.ApplyOnHit(Pawn, victim, damageDealt, Props, parent as Hediff_Cyberware);
        }

        public void Notify_Neutralized(Pawn victim, bool killed) { }

        public void Notify_TookDamage(DamageInfo dinfo, float damageTaken) { }
    }

    public static class CyberOnHitUtility
    {
        /// <summary>Re-entrancy guard: rider damage must not trigger more riders.</summary>
        public static bool Applying;

        public static void ApplyOnHit(Pawn attacker, Thing victim, float damageDealt, HediffCompProperties_CyberOnHit props, Hediff_Cyberware source)
        {
            if (victim == null || victim.Destroyed) return;
            float Val(string key) => key.NullOrEmpty() || source == null ? 0f : source.GetValue(key);
            Applying = true;
            try
            {
                if (props.riderDamage != null && damageDealt > 0f)
                    victim.TakeDamage(new DamageInfo(props.riderDamage, Mathf.Max(1f, damageDealt * props.riderFraction), 0f, -1f, attacker));
                if (victim is Pawn vp && !vp.Dead)
                {
                    if (Rand.Chance(Val(props.shockChanceKey)))
                        vp.TakeDamage(new DamageInfo(RPDefOf.RPC_Shock, 10f, 0f, -1f, attacker));
                    if (Rand.Chance(Val(props.burnChanceKey)))
                        vp.TryAttachFire(0.4f, attacker);
                    if (Rand.Chance(Val(props.poisonChanceKey)))
                        vp.TakeDamage(new DamageInfo(RPDefOf.RPC_Toxic, 6f, 0f, -1f, attacker));
                    if (Rand.Chance(Val(props.bleedChanceKey)) && !vp.Dead)
                        vp.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f, 99f, -1f, attacker));
                }
            }
            finally
            {
                Applying = false;
            }
        }
    }

    // ------------------------------------------------------------------------------------- active helpers

    public static class CyberActiveUtility
    {
        /// <summary>
        /// Add (or refresh) a timed effect hediff at the source cyberware's tier. refreshOnly: never shorten an
        /// existing effect, just make sure at least <paramref name="ticks"/> remain (conditionals).
        /// </summary>
        public static Hediff_CyberActive Apply(Pawn pawn, HediffDef def, Hediff_Cyberware source, int ticks, bool refreshOnly = false)
        {
            var ext = source.Ext;
            if (pawn.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_CyberActive existing)
            {
                if (refreshOnly) { if (existing.ticksLeft < ticks) existing.ticksLeft = ticks; }
                else existing.Setup(source.tier, ext?.minTier ?? CyberTier.T1, ext?.maxTier ?? CyberTier.Iconic, ticks, source.effectsDoubled);
                return existing;
            }
            var h = (Hediff_CyberActive)HediffMaker.MakeHediff(def, pawn);
            h.Setup(source.tier, ext?.minTier ?? CyberTier.T1, ext?.maxTier ?? CyberTier.Iconic, ticks, source.effectsDoubled);
            pawn.health.AddHediff(h);
            return h;
        }
    }

    // ------------------------------------------------------------------------------------ weapon classes

    public static class WeaponClassUtility
    {
        public static bool HasTag(Thing weapon, string tag) => weapon?.def?.weaponTags != null && weapon.def.weaponTags.Contains(tag);

        public static bool IsTech(Thing w) => HasTag(w, "RPC_Type_tech");
        public static bool IsSmart(Thing w) => HasTag(w, "RPC_Type_smart");
        public static bool IsGrenade(Thing w) => w != null && (HasTag(w, "GrenadeDestructive") || HasTag(w, "GrenadeEMP") || HasTag(w, "RPC_Grenade")
                                                             || (w.def.thingCategories?.Any(c => c.defName.Contains("Grenade")) ?? false));
        public static bool IsPower(Thing w) => w != null && w.def.IsRangedWeapon && !IsTech(w) && !IsSmart(w) && !IsGrenade(w);

        public static bool IsBlade(Thing w)
        {
            if (w == null || !w.def.IsMeleeWeapon || w.def.tools == null) return false;
            foreach (var t in w.def.tools)
                if (t.capacities != null && t.capacities.Any(c => c.defName == "Cut" || c.defName == "Stab")) return true;
            return false;
        }

        /// <summary>The weapon a hit came from: equipment, or the hediff weapon (arm cyberware) when melee.</summary>
        public static bool Matches(CyberWeaponFilter filter, DamageInfo dinfo)
        {
            var defThing = dinfo.Weapon;
            bool melee = defThing == null ? dinfo.WeaponLinkedHediff != null || dinfo.Def.isRanged == false : defThing.IsMeleeWeapon;
            switch (filter)
            {
                case CyberWeaponFilter.Any: return true;
                case CyberWeaponFilter.Melee: return melee && !(dinfo.Def.isExplosive);
                case CyberWeaponFilter.Ranged: return defThing != null && defThing.IsRangedWeapon;
                case CyberWeaponFilter.Tech: return defThing?.weaponTags?.Contains("RPC_Type_tech") == true;
                case CyberWeaponFilter.Smart: return defThing?.weaponTags?.Contains("RPC_Type_smart") == true;
                case CyberWeaponFilter.Power: return defThing != null && defThing.IsRangedWeapon && defThing.weaponTags?.Contains("RPC_Type_tech") != true && defThing.weaponTags?.Contains("RPC_Type_smart") != true;
                case CyberWeaponFilter.Blade: return (defThing != null && defThing.IsMeleeWeapon && defThing.tools != null && defThing.tools.Any(t => t.capacities != null && t.capacities.Any(c => c.defName == "Cut" || c.defName == "Stab")))
                                                     || (dinfo.WeaponLinkedHediff != null && (dinfo.Def == DamageDefOf.Cut || dinfo.Def == DamageDefOf.Stab));
                case CyberWeaponFilter.Grenade: return dinfo.Def.isExplosive && defThing != null && (defThing.weaponTags?.Any(t => t.Contains("Grenade")) == true);
            }
            return false;
        }
    }
}
