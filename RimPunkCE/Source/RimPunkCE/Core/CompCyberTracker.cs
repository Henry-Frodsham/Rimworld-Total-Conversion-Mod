using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    public class CompProperties_CyberTracker : CompProperties
    {
        public CompProperties_CyberTracker() => compClass = typeof(CompCyberTracker);
    }

    /// <summary>
    /// Per-pawn cyberware state, added at startup to every humanlike race (so other mods' races are covered).
    /// Holds rolled capacity, RAM, stamina regen and combat bookkeeping. Capacity max is a stat
    /// (RPC_CyberwareCapacity); this comp only contributes the rolled base via GetStatOffset.
    /// </summary>
    public class CompCyberTracker : ThingComp
    {
        public const float BaseCapacityMin = 50f;
        public const float BaseCapacityMax = 150f;

        private float baseCapacity = -1f;
        private float ram;
        public int lastCombatTick = -99999;

        private bool cacheDirty = true;
        private float cachedUsed;
        private readonly List<Hediff_Cyberware> cachedCyberware = new List<Hediff_Cyberware>();

        public Pawn Pawn => (Pawn)parent;

        public float BaseCapacity
        {
            get
            {
                EnsureBaseCapacity();
                return baseCapacity;
            }
        }

        // ---------- capacity ----------

        public IReadOnlyList<Hediff_Cyberware> Cyberware
        {
            get
            {
                RecacheIfDirty();
                return cachedCyberware;
            }
        }

        public bool HasCyberware => Cyberware.Count > 0;

        public float CapacityUsed
        {
            get
            {
                RecacheIfDirty();
                return cachedUsed;
            }
        }

        public float CapacityMax => Mathf.Max(1f, Pawn.GetStatValue(RPStatDefOf.RPC_CyberwareCapacity));

        /// <summary>Capacity usage as a fraction of max (cyberpsychosis thresholds: 0.8 / 0.5).</summary>
        public float UsageRatio => CapacityUsed / CapacityMax;

        public override float GetStatOffset(StatDef stat)
        {
            if (stat == RPStatDefOf.RPC_CyberwareCapacity) return BaseCapacity;
            return 0f;
        }

        public void Notify_CyberwareChanged()
        {
            cacheDirty = true;
            if (Pawn?.health?.hediffSet == null) return;
            RecacheIfDirty();
            CyberUtility.UpdateMatchingSets(Pawn, cachedCyberware);
            Pawn.health.capacities?.Notify_CapacityLevelsDirty();
            if (Pawn.Spawned) CyberpsychosisUtility.Notify_CapacityChanged(Pawn, this);
        }

        private void RecacheIfDirty()
        {
            if (!cacheDirty) return;
            cacheDirty = false;
            cachedCyberware.Clear();
            cachedUsed = 0f;
            var hediffs = Pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Cyberware c)
                {
                    cachedCyberware.Add(c);
                    cachedUsed += c.CapacityCost;
                }
            }
        }

        private void EnsureBaseCapacity()
        {
            if (baseCapacity >= 0f) return;
            // seeded per pawn: identical on every multiplayer client
            Rand.PushState(Gen.HashCombineInt(parent.thingIDNumber, 0x52504345));
            baseCapacity = Mathf.Round(Rand.Range(BaseCapacityMin, BaseCapacityMax));
            Rand.PopState();
        }

        // ---------- RAM ----------

        public float RamMax => Pawn.GetStatValue(RPStatDefOf.RPC_RamMax);
        public float Ram => Mathf.Min(ram, RamMax);

        public bool TryUseRam(float amount)
        {
            if (Ram < amount) return false;
            ram = Ram - amount;
            CyberEvents.Notify_RamChanged(Pawn, this);
            return true;
        }

        public void GainRam(float amount) => ram = Mathf.Clamp(Ram + amount, 0f, RamMax);

        // ---------- stamina ----------

        public Need_Stamina Stamina => Pawn.needs?.TryGetNeed<Need_Stamina>();

        public bool InCombat => Find.TickManager.TicksGame - lastCombatTick < RPDefOf.RPC_StaminaTuning.outOfCombatTicks;

        public void Notify_CombatAction()
        {
            bool wasInCombat = InCombat;
            lastCombatTick = Find.TickManager.TicksGame;
            if (!wasInCombat) CyberEvents.Notify_EnteredCombat(Pawn);
        }

        // ---------- lifecycle ----------

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureBaseCapacity();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureBaseCapacity();
            AptitudeUtility.EnsureAptitudeTrait(Pawn);
            cacheDirty = true;
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            if (!parent.IsHashIntervalTick(15, delta)) return;
            var pawn = Pawn;
            if (pawn.Dead) return;

            float ramMax = RamMax;
            if (ramMax > 0f && ram < ramMax)
                ram = Mathf.Min(ramMax, ram + pawn.GetStatValue(RPStatDefOf.RPC_RamRegen) * 15f / 60f);

            Stamina?.TickRegen(15);
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            var pawn = Pawn;
            if (pawn.Dead || !dinfo.Def.harmsHealth) return;
            CombatDamageUtility.PreApplyDamage(pawn, this, ref dinfo, out absorbed);
        }

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            if (dinfo.Def.ExternalViolenceFor(Pawn)) Notify_CombatAction();
            CyberEvents.Notify_DamageTaken(Pawn, dinfo, totalDamageDealt);
        }

        public override void Notify_UsedVerb(Pawn pawn, Verb verb)
        {
            base.Notify_UsedVerb(pawn, verb);
            if (verb is Verb_CastAbility) return;
            Notify_CombatAction();
            Stamina?.Notify_Attacked(verb);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;
            var pawn = Pawn;
            if (pawn.Faction != Faction.OfPlayer && !DebugSettings.ShowDevGizmos) yield break;
            if (RamMax > 0f) yield return new Gizmo_CyberBar(this, CyberBarKind.Ram);
            if (pawn.Drafted && Stamina != null) yield return new Gizmo_CyberBar(this, CyberBarKind.Stamina);
        }

        public override string CompInspectStringExtra()
        {
            if (!HasCyberware) return null;
            return "RPC_CapacityUsage".Translate(CapacityUsed.ToString("0"), CapacityMax.ToString("0"), UsageRatio.ToStringPercent());
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref baseCapacity, "baseCapacity", -1f);
            Scribe_Values.Look(ref ram, "ram");
            Scribe_Values.Look(ref lastCombatTick, "lastCombatTick", -99999);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) cacheDirty = true;
        }
    }
}
