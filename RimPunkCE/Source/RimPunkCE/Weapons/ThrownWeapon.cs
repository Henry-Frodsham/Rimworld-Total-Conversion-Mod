using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Throw the equipped melee weapon (knives, axes). Damage comes from the weapon's own best cutting/stabbing
    /// tool. The weapon is "in flight" (RPC_WeaponThrown) for returnDelaySeconds: its melee attacks are blocked
    /// until it returns to the thrower's hand automatically (weapons/overview.md).
    /// Granted through vanilla CompEquippableAbility on the weapon.
    /// </summary>
    public class CompProperties_AbilityThrowWeapon : CompProperties_AbilityEffect
    {
        public float damageFactor = 1.5f;
        public float returnDelaySeconds = 4f;

        public CompProperties_AbilityThrowWeapon() => compClass = typeof(CompAbilityEffect_ThrowWeapon);
    }

    public class CompAbilityEffect_ThrowWeapon : CompAbilityEffect
    {
        public new CompProperties_AbilityThrowWeapon Props => (CompProperties_AbilityThrowWeapon)props;

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest) =>
            base.CanApplyOn(target, dest) && parent.pawn.equipment?.Primary != null && !ThrownWeaponUtility.IsThrown(parent.pawn);

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            var pawn = parent.pawn;
            var weapon = pawn.equipment?.Primary;
            if (weapon == null || !target.HasThing) return;

            var tool = weapon.def.tools?
                .Where(t => t.capacities != null && t.capacities.Any(c => c.defName == "Cut" || c.defName == "Stab"))
                .OrderByDescending(t => t.power).FirstOrDefault() ?? weapon.def.tools?.OrderByDescending(t => t.power).FirstOrDefault();
            if (tool == null) return;

            var capacity = tool.capacities.FirstOrDefault(c => c.defName == "Stab") ?? tool.capacities.First();
            var damageDef = capacity.VerbsProperties.FirstOrDefault()?.meleeDamageDef ?? DamageDefOf.Stab;
            float amount = tool.power * Props.damageFactor * weapon.GetStatValue(StatDefOf.MeleeWeapon_DamageMultiplier);
            // the weapon's own CE melee verb for this tool: sharp pen with skill, and the blunt pen CE needs if armour deflects
            var verb = weapon.TryGetComp<CompEquippable>()?.AllVerbs.OfType<CombatExtended.Verb_MeleeAttackCE>().FirstOrDefault(v => v.tool == tool);
            float pen = verb?.ArmorPenetrationSharp ?? (tool as CombatExtended.ToolCE)?.armorPenetrationSharp ?? 0.5f;

            if (pawn.Map != null) FleckMaker.ThrowDustPuff(target.Thing.DrawPos, pawn.Map, 0.6f);
            MeleeVerbAccess.SetLastAttackVerb(verb);
            try { target.Thing.TakeDamage(new DamageInfo(damageDef, amount, pen, -1f, pawn, null, weapon.def)); }
            finally { MeleeVerbAccess.SetLastAttackVerb(null); }

            var h = HediffMaker.MakeHediff(RPDefOf.RPC_WeaponThrown, pawn);
            var disappears = h.TryGetComp<HediffComp_Disappears>();
            if (disappears != null) disappears.ticksToDisappear = CyberUtility.SecondsToTicks(Props.returnDelaySeconds);
            pawn.health.AddHediff(h);
        }

        public override bool AICanTargetNow(LocalTargetInfo target) =>
            target.Thing is Pawn p && !p.Downed && p.HostileTo(parent.pawn) && !parent.pawn.Position.AdjacentTo8WayOrInside(p.Position);
    }

    /// <summary>CE reads Verb_MeleeAttackCE.LastAttackVerb (protected setter) for deflected-hit blunt pen; ability damage has no verb otherwise.</summary>
    internal abstract class MeleeVerbAccess : CombatExtended.Verb_MeleeAttackCE
    {
        public static void SetLastAttackVerb(CombatExtended.Verb_MeleeAttackCE verb) => LastAttackVerb = verb;
    }

    public static class ThrownWeaponUtility
    {
        public static bool IsThrown(Pawn pawn) => pawn?.health?.hediffSet?.HasHediff(RPDefOf.RPC_WeaponThrown) == true;
    }
}
