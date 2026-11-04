using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Cyberware damage maths that needs more than a stat: values are read per installed cyberware by key
    /// (gen_cyberware.py writes them). Incoming is called from CombatDamageUtility.PreApplyDamage,
    /// outgoing from CombatDamageUtility.OutgoingDamageFactor.
    /// </summary>
    public static class CyberwareDefenseUtility
    {
        private const float CloseRangeFull = 3f;
        private const float CloseRangeZero = 6f;

        public static void ModifyIncoming(Pawn pawn, CompCyberTracker tracker, ref DamageInfo dinfo)
        {
            float factor = pawn.GetStatValue(RPStatDefOf.RPC_IncomingDamageFactor) / Mathf.Max(0.1f, pawn.GetStatValue(RPStatDefOf.RPC_MaxHealthFactor));

            Thing attacker = dinfo.Instigator;
            float dist = attacker != null && attacker.Spawned && pawn.Spawned ? attacker.Position.DistanceTo(pawn.Position) : 999f;
            float closeRange = 0f, rearArmor = 0f, explosiveResist = 0f, delayed = 0f;
            foreach (var c in pawn.InstalledCyberware())
            {
                var ext = c.Ext;
                if (ext?.values == null) continue;
                closeRange += c.GetValue("closeRangeReduction");
                rearArmor += c.GetValue("rearArmorEffectiveness");
                explosiveResist += c.GetValue("explosionResistPerCrafting") * (pawn.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0);
                delayed += c.GetValue("delayedFraction");
            }

            // ProxiShield / Peripheral Inverse: full reduction within 3 tiles, tapering to 0 at 6
            if (closeRange > 0f && dist < CloseRangeZero)
                factor *= 1f - Mathf.Min(0.9f, closeRange * Mathf.Clamp01((CloseRangeZero - dist) / (CloseRangeZero - CloseRangeFull)));

            // Cellular Adapter: explosion resistance per crafting level
            if (explosiveResist > 0f && dinfo.Def.isExplosive) factor *= 1f - Mathf.Min(0.9f, explosiveResist);

            if (factor != 1f) dinfo.SetAmount(dinfo.Amount * factor);

            // Carapace: armor works better against hits from the side or rear (armor penetration of the hit is reduced)
            if (rearArmor > 0f && attacker != null && attacker.Spawned && pawn.Spawned && FromSideOrRear(pawn, attacker))
            {
                ref float pen = ref AccessTools.StructFieldRefAccess<DamageInfo, float>(ref dinfo, "armorPenetrationInt");
                pen /= 1f + rearArmor;
            }

            // Painducer: hold back part of the damage and apply it as small hits over 50 s
            if (delayed > 0f && tracker != null && dinfo.Amount > 0.5f)
            {
                float held = dinfo.Amount * Mathf.Min(0.9f, delayed);
                dinfo.SetAmount(dinfo.Amount - held);
                tracker.AddDelayed(held, CyberUtility.SecondsToTicks(50f));
            }
        }

        private static bool FromSideOrRear(Pawn pawn, Thing attacker)
        {
            float facing = pawn.Rotation.AsAngle;
            float toAttacker = (attacker.Position - pawn.Position).AngleFlat;
            return Mathf.Abs(Mathf.DeltaAngle(facing, toAttacker)) > 45f;
        }

        public static float OutgoingFactor(Pawn attacker, Thing victim, DamageInfo dinfo)
        {
            float f = attacker.GetStatValue(RPStatDefOf.RPC_DamageFactorAll);
            var weapon = dinfo.Weapon;
            if (weapon != null && weapon.IsRangedWeapon)
            {
                if (weapon.weaponTags?.Contains("RPC_Type_tech") == true)
                {
                    f *= attacker.GetStatValue(RPStatDefOf.RPC_DamageFactorTech);
                    float perLevel = 0f;
                    foreach (var c in attacker.InstalledCyberware()) perLevel += c.GetValue("techDamagePerCrafting");
                    if (perLevel > 0f) f *= 1f + perLevel * (attacker.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0);
                }
                else if (weapon.weaponTags?.Contains("RPC_Type_smart") != true) f *= attacker.GetStatValue(RPStatDefOf.RPC_DamageFactorPower);
            }
            if (victim is Pawn vp)
            {
                if (vp.health.hediffSet.HasHediff(HediffDefOf.ToxicBuildup)) f *= attacker.GetStatValue(RPStatDefOf.RPC_DamageVsPoisoned);
                if (dinfo.HitPart != null && dinfo.HitPart.depth == BodyPartDepth.Inside) f *= attacker.GetStatValue(RPStatDefOf.RPC_OrganDamageFactor);
                if (IsMonowire(dinfo)) f *= MonowireFactor(attacker, vp);
            }
            foreach (var a in attacker.ActiveEffects()) f *= a.LowHealthDamageFactor();
            return f;
        }

        private static bool IsMonowire(DamageInfo dinfo) => dinfo.WeaponLinkedHediff?.defName.Contains("Monowire") == true;

        /// <summary>Cyberdeck monowire synergies: Paraline (+2% per used RAM, max 30%), Biotech (+25% vs damage-over-time targets).</summary>
        private static float MonowireFactor(Pawn attacker, Pawn victim)
        {
            float f = 1f;
            var tracker = attacker.GetCyberTracker();
            foreach (var c in attacker.InstalledCyberware())
            {
                float perRam = c.GetValue("monowirePerUsedRam");
                if (perRam > 0f && tracker != null) f *= 1f + Mathf.Min(0.3f, perRam * Mathf.Max(0f, tracker.RamMax - tracker.Ram));
                float vsDot = c.GetValue("monowireVsDot");
                if (vsDot > 0f && (victim.health.hediffSet.HasHediff(RPHediffDefOf.RPC_Electrified) || victim.health.hediffSet.HasHediff(RPHediffDefOf.RPC_ToxicBurn)))
                    f *= 1f + vsDot;
            }
            return f;
        }

        /// <summary>Deep-field Visual Interface / Visual Cortex Support: crit chance grows with distance to the target.</summary>
        public static float DistanceCritBonus(Pawn attacker, Thing victim)
        {
            if (victim == null || !victim.Spawned || !attacker.Spawned) return 0f;
            float dist = attacker.Position.DistanceTo(victim.Position);
            float bonus = 0f;
            foreach (var c in attacker.InstalledCyberware())
            {
                float max = c.GetValue("distanceCrit");
                if (max <= 0f) continue;
                float range = c.GetValue("distanceCritRange", 40f);
                bonus += max * Mathf.Clamp01(dist / range);
            }
            return bonus;
        }
    }
}
