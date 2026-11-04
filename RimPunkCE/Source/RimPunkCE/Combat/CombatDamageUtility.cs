using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Incoming-damage rules applied from <see cref="CompCyberTracker.PostPreApplyDamage"/> (no Harmony):
    /// active-effect damage reduction / invulnerability, mitigation, and the body-part health floor.
    /// </summary>
    public static class CombatDamageUtility
    {
        public static void PreApplyDamage(Pawn pawn, CompCyberTracker tracker, ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            bool violent = dinfo.Def.ExternalViolenceFor(pawn);

            // 1. active effects (berserk damage reduction, invulnerability)
            float factor = 1f;
            float partFloor = 0f;
            foreach (var active in pawn.ActiveEffects())
            {
                factor *= active.IncomingDamageFactor;
                var ext = active.Ext;
                if (ext != null && ext.partHealthFloor > partFloor) partFloor = ext.partHealthFloor;
            }
            if (factor <= 0f && violent)
            {
                absorbed = true;
                return;
            }
            if (factor < 1f && violent) dinfo.SetAmount(dinfo.Amount * factor);

            // 1b. cyberware filters (Nano-plating projectile block)
            if (violent && !CompCyberTracker.ApplyingDelayed)
                foreach (var h in pawn.health.hediffSet.hediffs)
                    if (h is HediffWithComps hwc && hwc.comps != null)
                        foreach (var c in hwc.comps)
                            if (c is ICyberIncomingFilter f && f.TryBlock(dinfo)) { absorbed = true; return; }

            // 1c. cyberware damage reduction (CyberwareDefenseUtility)
            if (violent && !CompCyberTracker.ApplyingDelayed) CyberwareDefenseUtility.ModifyIncoming(pawn, tracker, ref dinfo);
            if (dinfo.Amount <= 0f) { absorbed = true; return; }

            // 2. mitigation: chance to block all of it, regaining stamina (terms.md)
            if (violent && RimPunkMod.Settings.staminaEnabled)
            {
                float chance = pawn.GetStatValue(RPStatDefOf.RPC_MitigationChance);
                if (chance > 0f && Rand.Chance(Mathf.Min(chance, 0.95f)))
                {
                    absorbed = true;
                    Diag.Count("mitigation");
                    tracker.Stamina?.GainFraction(pawn.GetStatValue(RPStatDefOf.RPC_MitigationStrength));
                    MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "RPC_Mitigated".Translate(), 2f);
                    return;
                }
            }

            // 3. body part health floor (berserk: parts can't drop below 25%).
            // Only enforceable when the hit part is already chosen; otherwise the damage worker picks it later.
            if (partFloor > 0f && dinfo.HitPart != null)
            {
                float maxHp = PartHealthUtility.MaxHealth(dinfo.HitPart, pawn);
                float current = pawn.health.hediffSet.GetPartHealth(dinfo.HitPart);
                float allowed = Mathf.Max(0f, current - maxHp * partFloor);
                if (dinfo.Amount > allowed) dinfo.SetAmount(allowed);
                if (allowed <= 0f) absorbed = true;
            }
        }

        /// <summary>Outgoing-damage multipliers from attacker stats (vs mechanoids, crits, headshots).</summary>
        public static float OutgoingDamageFactor(Pawn attacker, Thing victim, DamageInfo dinfo)
        {
            float f = 1f;
            if (victim is Pawn vp)
            {
                if (vp.RaceProps.IsMechanoid) f *= attacker.GetStatValue(RPStatDefOf.RPC_DamageVsMechanoids);
                if (dinfo.HitPart != null && dinfo.HitPart.def == BodyPartDefOf.Head)
                    f *= attacker.GetStatValue(RPStatDefOf.RPC_HeadshotDamageFactor);
            }
            f *= CyberwareDefenseUtility.OutgoingFactor(attacker, victim, dinfo);
            // ranged crits deal double damage (terms.md); melee crits are Combat Extended's own
            bool grenade = dinfo.Def.isExplosive && WeaponClassUtility.Matches(CyberWeaponFilter.Grenade, dinfo);
            if ((dinfo.Weapon != null && dinfo.Weapon.IsRangedWeapon) || grenade)
            {
                float crit = grenade ? attacker.GetStatValue(RPStatDefOf.RPC_CritChanceGrenades)
                                     : attacker.GetStatValue(RPStatDefOf.RPC_RangedCritChance) + CyberwareDefenseUtility.DistanceCritBonus(attacker, victim);
                if (crit > 0f && Rand.Chance(crit))
                {
                    f *= 2f * attacker.GetStatValue(RPStatDefOf.RPC_CritDamageFactor);
                    Diag.Count("ranged-crit");
                    if (attacker.Map != null) MoteMaker.ThrowText(victim.DrawPos, attacker.Map, "RPC_Crit".Translate(), Color.yellow, 2f);
                }
            }
            return f;
        }
    }
}
