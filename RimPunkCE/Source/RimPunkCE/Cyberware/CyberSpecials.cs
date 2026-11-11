using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    // =====================================================================================================
    // One-off cyberware behaviours that don't fit the generic triggers / conditionals (CyberSystems.cs).
    // =====================================================================================================

    /// <summary>Incoming damage filters on cyberware comps (Nano-plating). Called from CombatDamageUtility.</summary>
    public interface ICyberIncomingFilter
    {
        /// <summary>Return true to block the hit entirely.</summary>
        bool TryBlock(DamageInfo dinfo);
    }

    // ------------------------------------------------------------------------------------- Second Heart

    /// <summary>Second Heart: when the pawn would die or be downed, fully heal them instead (cooldownKey seconds).</summary>
    public class HediffCompProperties_SecondHeart : HediffCompProperties
    {
        public string cooldownKey = "cooldown";

        public HediffCompProperties_SecondHeart() => compClass = typeof(HediffComp_SecondHeart);
    }

    public class HediffComp_SecondHeart : HediffComp
    {
        private int lastUsedTick = -9999999;

        public HediffCompProperties_SecondHeart Props => (HediffCompProperties_SecondHeart)props;

        public bool Ready
        {
            get
            {
                float cd = (parent as Hediff_Cyberware)?.GetValue(Props.cooldownKey, 600f) ?? 600f;
                return Find.TickManager.TicksGame - lastUsedTick >= CyberUtility.SecondsToTicks(cd * Pawn.GetStatValue(RPStatDefOf.RPC_CyberwareCooldownFactor));
            }
        }

        public override string CompLabelInBracketsExtra => Ready ? null : "RPC_Recharging".Translate();

        /// <summary>From the Pawn.Kill prefix and the MakeDowned postfix. True if the pawn was saved.</summary>
        public static bool TryProc(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead) return false;
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h is not HediffWithComps hwc || hwc.TryGetComp<HediffComp_SecondHeart>() is not HediffComp_SecondHeart c || !c.Ready) continue;
                c.lastUsedTick = Find.TickManager.TicksGame;
                FullHeal(pawn);
                Diag.Event("cyberware", $"{Diag.Describe(pawn)} saved by Second Heart");
                if (pawn.Spawned)
                {
                    FleckMaker.ThrowLightningGlow(pawn.DrawPos, pawn.Map, 1.5f);
                    Messages.Message("RPC_SecondHeartProc".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.PositiveEvent, false);
                }
                return true;
            }
            return false;
        }

        private static void FullHeal(Pawn pawn)
        {
            var hs = pawn.health.hediffSet;
            foreach (var inj in hs.hediffs.OfType<Hediff_Injury>().ToList()) pawn.health.RemoveHediff(inj);
            foreach (var h in hs.hediffs.Where(h => h.def == HediffDefOf.BloodLoss).ToList()) pawn.health.RemoveHediff(h);
            // missing natural parts grow back (a destroyed heart or brain is what kills)
            foreach (var m in hs.hediffs.OfType<Hediff_MissingPart>().ToList())
                if (m.Part != null && !hs.PartOrAnyAncestorHasDirectlyAddedParts(m.Part) && hs.hediffs.Contains(m))
                    pawn.health.RestorePart(m.Part, checkStateChange: false);
            pawn.health.CheckForStateChange(null, null);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastUsedTick, "rpcLastUsed", -9999999);
        }
    }

    // ------------------------------------------------------------------------------------- Nano-plating

    /// <summary>
    /// Nano-plating: blockChanceKey chance to block an incoming projectile, doubled for bonusDurationKey seconds after
    /// dodging, dashing or jumping (until the next block). At most maxBlocks blocks per windowKey seconds.
    /// </summary>
    public class HediffCompProperties_NanoPlating : HediffCompProperties
    {
        public string blockChanceKey = "blockChance";
        public string bonusDurationKey = "bonusDuration";
        public string windowKey = "blockWindow";
        public int maxBlocks = 3;

        public HediffCompProperties_NanoPlating() => compClass = typeof(HediffComp_NanoPlating);
    }

    public class HediffComp_NanoPlating : HediffComp, ICyberIncomingFilter
    {
        private List<int> blockTicks = new List<int>();

        public HediffCompProperties_NanoPlating Props => (HediffCompProperties_NanoPlating)props;

        public bool TryBlock(DamageInfo dinfo)
        {
            bool projectile = (dinfo.Weapon != null && dinfo.Weapon.IsRangedWeapon) || dinfo.Def.isRanged;
            if (!projectile || dinfo.Def.isExplosive) return false;
            var cyber = parent as Hediff_Cyberware;
            var tracker = Pawn.GetCyberTracker();
            if (cyber == null || tracker == null) return false;
            int now = Find.TickManager.TicksGame;
            int window = CyberUtility.SecondsToTicks(cyber.GetValue(Props.windowKey, 60f));
            blockTicks.RemoveAll(t => now - t > window);
            if (blockTicks.Count >= Props.maxBlocks) return false;
            float chance = cyber.GetValue(Props.blockChanceKey, 0.05f);
            bool bonus = now - tracker.lastEvasiveTick <= CyberUtility.SecondsToTicks(cyber.GetValue(Props.bonusDurationKey, 15f));
            if (bonus) chance *= 2f;
            if (!Rand.Chance(chance)) return false;
            blockTicks.Add(now);
            if (bonus) tracker.lastEvasiveTick = -999999; // the bonus is spent by a block
            if (Pawn.Spawned) MoteMaker.ThrowText(Pawn.DrawPos, Pawn.Map, "RPC_Blocked".Translate(), 2f);
            Diag.Count("nanoplating-block");
            RPSound.At(RPSoundDefOf.RPC_Snd_NanoDeflect, Pawn);
            return true;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref blockTicks, "rpcBlockTicks", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) blockTicks ??= new List<int>();
        }
    }

    // ------------------------------------------------------------------------------------- Microgenerator

    /// <summary>
    /// Microgenerator: reloading an empty weapon charges the next ranged hit with an electroshock around the impact,
    /// up to shockDamageKey at a full 5-round charge (CompAmmoUser.LoadAmmo patch sets <see cref="charge"/>).
    /// </summary>
    public class HediffCompProperties_Microgenerator : HediffCompProperties
    {
        public string shockDamageKey = "shockDamage";
        public float radius = 2.5f;

        public HediffCompProperties_Microgenerator() => compClass = typeof(HediffComp_Microgenerator);
    }

    public class HediffComp_Microgenerator : HediffComp, ICyberEventReceiver
    {
        public int charge;

        public HediffCompProperties_Microgenerator Props => (HediffCompProperties_Microgenerator)props;

        public override string CompLabelInBracketsExtra => charge > 0 ? "RPC_Charged".Translate(charge) : null;

        public void Notify_DealtDamage(Thing victim, DamageInfo dinfo, float damageDealt)
        {
            if (charge <= 0 || dinfo.Weapon == null || !dinfo.Weapon.IsRangedWeapon || victim?.Map == null) return;
            float max = (parent as Hediff_Cyberware)?.GetValue(Props.shockDamageKey, 10f) ?? 10f;
            float dmg = max * Mathf.Min(5, charge) / 5f;
            charge = 0;
            CyberUtility.Electroshock(Pawn, victim.Position, victim.Map, Props.radius, dmg);
        }

        public void Notify_Neutralized(Pawn victim, bool killed) { }

        public void Notify_TookDamage(DamageInfo dinfo, float damageTaken) { }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref charge, "rpcCharge");
        }
    }

    // ------------------------------------------------------------------------------------- Self-ICE

    /// <summary>Self-ICE: automatically negates a hostile quickhack (cooldownKey seconds between negations).</summary>
    public class HediffCompProperties_SelfIce : HediffCompProperties
    {
        public string cooldownKey = "cooldown";

        public HediffCompProperties_SelfIce() => compClass = typeof(HediffComp_SelfIce);
    }

    public class HediffComp_SelfIce : HediffComp
    {
        private int lastUsedTick = -9999999;

        public HediffCompProperties_SelfIce Props => (HediffCompProperties_SelfIce)props;

        /// <summary>From the quickhack effect: true if the hack on <paramref name="target"/> was negated.</summary>
        public static bool TryNegate(Pawn target)
        {
            if (target?.health?.hediffSet == null) return false;
            foreach (var h in target.health.hediffSet.hediffs)
            {
                if (h is not HediffWithComps hwc || hwc.TryGetComp<HediffComp_SelfIce>() is not HediffComp_SelfIce c) continue;
                float cd = (h as Hediff_Cyberware)?.GetValue(c.Props.cooldownKey, 90f) ?? 90f;
                if (Find.TickManager.TicksGame - c.lastUsedTick < CyberUtility.SecondsToTicks(cd)) continue;
                c.lastUsedTick = Find.TickManager.TicksGame;
                if (target.Spawned) MoteMaker.ThrowText(target.DrawPos, target.Map, "RPC_HackNegated".Translate(), Color.cyan, 2f);
                Diag.Count("self-ice");
                return true;
            }
            return false;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastUsedTick, "rpcLastUsed", -9999999);
        }
    }

    // ------------------------------------------------------------------------------------- reveal (optics)

    public static class RevealUtility
    {
        /// <summary>Unfog the cells around hostile pawns, traps and turrets within the radii (0 = skip).</summary>
        public static void Reveal(Pawn pawn, float enemies, float traps, float turrets)
        {
            if (!pawn.Spawned) return;
            var map = pawn.Map;
            int n = 0;
            if (enemies > 0f)
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                    if (p.HostileTo(pawn) && p.Position.InHorDistOf(pawn.Position, enemies)) n += UnfogAround(map, p.Position, 1);
            if (traps > 0f || turrets > 0f)
                foreach (var b in map.listerBuildings.allBuildingsNonColonist)
                {
                    if (b.Faction == pawn.Faction) continue;
                    if (traps > 0f && b is Building_Trap && b.Position.InHorDistOf(pawn.Position, traps)) n += UnfogAround(map, b.Position, 1);
                    else if (turrets > 0f && b is Building_Turret && b.Position.InHorDistOf(pawn.Position, turrets)) n += UnfogAround(map, b.Position, 2);
                }
            if (n > 0) Diag.Count("reveal");
        }

        public static int UnfogAround(Map map, IntVec3 c, int radius)
        {
            int n = 0;
            foreach (var cell in GenRadial.RadialCellsAround(c, radius, true))
            {
                if (!cell.InBounds(map) || !cell.Fogged(map)) continue;
                map.fogGrid.Unfog(cell);
                n++;
            }
            return n;
        }
    }

    // ------------------------------------------------------------------------------------- abilities

    /// <summary>Instant flat heal from the source cyberware's healKey value (Blood Pump), before its regen buff.</summary>
    public class CompProperties_AbilityCyberHeal : CompProperties_AbilityEffect
    {
        public string healKey = "instantHeal";

        public CompProperties_AbilityCyberHeal() => compClass = typeof(CompAbilityEffect_CyberHeal);
    }

    public class CompAbilityEffect_CyberHeal : CompAbilityEffect
    {
        public new CompProperties_AbilityCyberHeal Props => (CompProperties_AbilityCyberHeal)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            float heal = (parent as CyberwareAbility)?.GetSourceValue(Props.healKey, 5f) ?? 5f;
            CyberUtility.HealFlat(parent.pawn, heal);
        }

        public override bool AICanTargetNow(LocalTargetInfo target) =>
            parent.pawn.health.summaryHealth.SummaryHealthPercent < 0.6f;
    }

    /// <summary>
    /// Monowire lash: whip a target up to the ability range (2-3 tiles). Damage from the source cyberware's
    /// lashDamage value; the hit is tagged with the monowire's hediff so its on-hit effects (shock, burn, poison) apply.
    /// </summary>
    public class CompProperties_AbilityMonowireLash : CompProperties_AbilityEffect
    {
        public DamageDef damageDef;
        public string damageKey = "lashDamage";
        public float armorPenetration = 1.2f;

        public CompProperties_AbilityMonowireLash() => compClass = typeof(CompAbilityEffect_MonowireLash);
    }

    public class CompAbilityEffect_MonowireLash : CompAbilityEffect
    {
        public new CompProperties_AbilityMonowireLash Props => (CompProperties_AbilityMonowireLash)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (!target.HasThing) return;
            var ability = parent as CyberwareAbility;
            var source = ability?.Source;
            var caster = parent.pawn;
            float dmg = (source?.GetValue(Props.damageKey, 10f) ?? 10f) * caster.GetStatValue(StatDefOf.MeleeDamageFactor);
            var dinfo = new DamageInfo(Props.damageDef ?? DamageDefOf.Cut, dmg, Props.armorPenetration, (target.Thing.Position - caster.Position).AngleFlat, caster);
            if (source != null) dinfo.SetWeaponHediff(source.def);
            if (caster.Map != null) FleckMaker.ThrowMicroSparks(target.Thing.DrawPos, caster.Map);
            target.Thing.TakeDamage(dinfo);
        }

        public override bool AICanTargetNow(LocalTargetInfo target) =>
            target.Thing is Pawn p && !p.Downed && p.HostileTo(parent.pawn);
    }
}
