using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>All stamina numbers live in Defs/StaminaTuning.xml.</summary>
    public class StaminaTuningDef : Def
    {
        /// <summary>Regen per second while standing still (50/s: empty to full in 2 s).</summary>
        public float regenPerSecond = 50f;

        /// <summary>Melee drain = meleeMassFactor x weapon mass x attack cooldown (seconds).</summary>
        public float meleeMassFactor = 4f;

        /// <summary>Ranged drain per shot = rangedMassFactor x weapon mass.</summary>
        public float rangedMassFactor = 1.5f;

        /// <summary>Unarmed / implant tools: drain = toolPowerFactor x tool power.</summary>
        public float toolPowerFactor = 0.6f;

        public float minDrain = 1f;
        public float maxDrain = 40f;

        /// <summary>Seconds with no shooting / being shot at before a pawn counts as out of combat (terms.md: 10 s).</summary>
        public int outOfCombatTicks = 600;
    }

    /// <summary>Per-weapon override: put on any weapon ThingDef (vanilla, CE, other mods) via XML patch.</summary>
    public class StaminaCostExtension : DefModExtension
    {
        public float costPerAttack = -1f;
    }

    public static class StaminaUtility
    {
        private static readonly Dictionary<ThingDef, float> weaponCostCache = new Dictionary<ThingDef, float>();

        public static float BaseCostFor(Verb verb)
        {
            var tuning = RPDefOf.RPC_StaminaTuning;
            var weapon = verb.EquipmentSource;
            float cost;
            if (weapon != null)
            {
                if (!weaponCostCache.TryGetValue(weapon.def, out cost))
                {
                    var ext = weapon.def.GetModExtension<StaminaCostExtension>();
                    if (ext != null && ext.costPerAttack >= 0f) cost = ext.costPerAttack;
                    else
                    {
                        float mass = weapon.def.GetStatValueAbstract(StatDefOf.Mass);
                        cost = verb.IsMeleeAttack
                            ? tuning.meleeMassFactor * mass * Mathf.Max(0.5f, verb.tool?.cooldownTime ?? 1f)
                            : tuning.rangedMassFactor * mass;
                    }
                    weaponCostCache[weapon.def] = cost;
                }
            }
            else
            {
                // unarmed or implant (hediff) tools: scale with how hard the tool hits
                cost = tuning.toolPowerFactor * (verb.tool?.power ?? 8f);
            }
            return Mathf.Clamp(cost, tuning.minDrain, tuning.maxDrain);
        }

        public static void ClearCache() => weaponCostCache.Clear();
    }

    /// <summary>
    /// Stamina need (Needs tab). Level is 0..100 (RPC_StaminaMax). Drains per attack, regenerates while
    /// standing still. At 0 the pawn becomes exhausted (no attacks, 50% move speed) until it's back to full.
    /// Regen runs from the tracker every 15 ticks; NeedInterval (every 150 ticks) is too coarse for a 2 s refill.
    /// </summary>
    public class Need_Stamina : Need
    {
        public Need_Stamina(Pawn pawn) : base(pawn) { }

        public override float MaxLevel => Mathf.Max(1f, pawn.GetStatValue(RPStatDefOf.RPC_StaminaMax));

        public override int GUIChangeArrow => pawn.pather != null && !pawn.pather.Moving && CurLevel < MaxLevel ? 1 : 0;

        public bool Exhausted => pawn.health.hediffSet.HasHediff(RPDefOf.RPC_Exhausted);

        public override void SetInitialLevel() => CurLevel = MaxLevel;

        public override void NeedInterval() { }

        public void TickRegen(int ticks)
        {
            if (!RimPunkMod.Settings.staminaEnabled) return;
            if (pawn.pather != null && pawn.pather.Moving) return;
            float max = MaxLevel;
            if (CurLevel >= max)
            {
                ClearExhausted();
                return;
            }
            float regen = RPDefOf.RPC_StaminaTuning.regenPerSecond * pawn.GetStatValue(RPStatDefOf.RPC_StaminaRegen) * ticks / 60f;
            CurLevel = Mathf.Min(max, CurLevel + regen);
            if (CurLevel >= max) ClearExhausted();
        }

        public void Notify_Attacked(Verb verb)
        {
            if (!RimPunkMod.Settings.staminaEnabled) return;
            float cost = StaminaUtility.BaseCostFor(verb) * pawn.GetStatValue(RPStatDefOf.RPC_StaminaCostFactor)
                         * pawn.GetStatValue(verb.IsMeleeAttack ? RPStatDefOf.RPC_StaminaCostFactorMelee : RPStatDefOf.RPC_StaminaCostFactorRanged);
            foreach (var active in pawn.ActiveEffects())
                if (active.Ext != null) cost *= active.Ext.staminaCostFactor;
            if (cost <= 0f) return;
            CurLevel = Mathf.Max(0f, CurLevel - cost);
            Diag.Count("stamina-drain");
            if (CurLevel <= 0f && !Exhausted)
            {
                pawn.health.AddHediff(RPDefOf.RPC_Exhausted);
                Diag.Event("stamina", $"{Diag.Describe(pawn)} exhausted ({verb.EquipmentSource?.def.defName ?? verb.tool?.label}, cost {cost:0.#})");
            }
        }

        public void GainFraction(float fraction)
        {
            if (fraction <= 0f) return;
            CurLevel = Mathf.Min(MaxLevel, CurLevel + MaxLevel * fraction);
        }

        private void ClearExhausted()
        {
            var h = pawn.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_Exhausted);
            if (h != null) pawn.health.RemoveHediff(h);
        }

        public override string GetTipString() =>
            base.GetTipString() + "\n\n" + (Exhausted ? "RPC_StaminaExhaustedTip".Translate() : "RPC_StaminaTip".Translate());
    }
}
