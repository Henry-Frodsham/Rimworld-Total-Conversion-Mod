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

            // 2. mitigation: chance to block all of it, regaining stamina (terms.md)
            if (violent && RimPunkMod.Settings.staminaEnabled)
            {
                float chance = pawn.GetStatValue(RPStatDefOf.RPC_MitigationChance);
                if (chance > 0f && Rand.Chance(Mathf.Min(chance, 0.95f)))
                {
                    absorbed = true;
                    tracker.Stamina?.GainFraction(pawn.GetStatValue(RPStatDefOf.RPC_MitigationStrength));
                    MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "RPC_Mitigated".Translate(), 2f);
                    return;
                }
            }

            // 3. body part health floor (berserk: parts can't drop below 25%).
            // Only enforceable when the hit part is already chosen; otherwise the damage worker picks it later.
            if (partFloor > 0f && dinfo.HitPart != null)
            {
                float maxHp = dinfo.HitPart.def.GetMaxHealth(pawn);
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
            // ranged crits deal double damage (terms.md); melee crits are Combat Extended's own
            if (dinfo.Weapon != null && dinfo.Weapon.IsRangedWeapon)
            {
                float crit = attacker.GetStatValue(RPStatDefOf.RPC_RangedCritChance);
                if (crit > 0f && Rand.Chance(crit))
                {
                    f *= 2f * attacker.GetStatValue(RPStatDefOf.RPC_CritDamageFactor);
                    if (attacker.Map != null) MoteMaker.ThrowText(victim.DrawPos, attacker.Map, "RPC_Crit".Translate(), Color.yellow, 2f);
                }
            }
            return f;
        }
    }
}
