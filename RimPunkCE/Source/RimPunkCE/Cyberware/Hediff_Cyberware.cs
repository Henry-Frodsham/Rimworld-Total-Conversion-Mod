using System.Text;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Installed cyberware. Not a Hediff_Implant because whole-body systems (skeleton, nervous system...)
    /// have no body part, and Hediff_Implant errors on a null part.
    /// The current stage is generated from the tier, so vanilla stats/capacities apply tiered values directly.
    /// </summary>
    public class Hediff_Cyberware : HediffWithComps
    {
        public CyberTier tier = CyberTier.T1;

        /// <summary>Legs: set by the tracker when a full matching set is installed.</summary>
        public bool effectsDoubled;

        private CyberwareExtension extCached;
        private TieredStageCache stageCache;

        public CyberwareExtension Ext => extCached ??= def.GetModExtension<CyberwareExtension>();

        public override HediffStage CurStage
        {
            get
            {
                var ext = Ext;
                if (ext == null) return base.CurStage;
                stageCache ??= new TieredStageCache();
                return stageCache.Get(ext.effects, TierUtility.Clamp(tier, ext.minTier, ext.maxTier), ext.minTier, ext.maxTier, effectsDoubled, def.stages?.FirstOrFallback());
            }
        }

        public override int CurStageIndex => 0;

        public override string LabelInBrackets
        {
            get
            {
                string b = base.LabelInBrackets;
                string t = tier.Label();
                if (effectsDoubled) t += ", " + "RPC_Paired".Translate();
                return b.NullOrEmpty() ? t : t + ", " + b;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                var sb = new StringBuilder(base.TipStringExtra);
                var ext = Ext;
                if (ext != null)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("RPC_CapacityCost".Translate(ext.capacityCost.ToString("0.#")));
                }
                return sb.ToString();
            }
        }

        public float CapacityCost => Ext?.capacityCost ?? 0f;

        public float GetValue(string key, float fallback = 0f) => Ext?.GetValue(key, tier, fallback) ?? fallback;

        public void SetTier(CyberTier newTier)
        {
            var ext = Ext;
            tier = ext != null ? TierUtility.Clamp(newTier, ext.minTier, ext.maxTier) : newTier;
            pawn?.health?.Notify_HediffChanged(this);
            pawn?.GetCyberTracker()?.Notify_CyberwareChanged();
        }

        public void SetDoubled(bool doubled)
        {
            if (effectsDoubled == doubled) return;
            effectsDoubled = doubled;
            pawn?.health?.Notify_HediffChanged(this);
            this.TryGetComp<HediffComp_GiveCyberAbilities>()?.Refresh();
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            var ext = Ext;
            if (ext != null) tier = TierUtility.Clamp(tier, ext.minTier, ext.maxTier);
            if (pawn.Faction != null && pawn.Faction.IsPlayer) Diag.Event("cyberware", $"{Diag.Describe(pawn)} +{def.defName} {tier} on {Part?.Label ?? "whole body"}");
            else Diag.Count("cyberware-installed-npc");
            pawn.GetCyberTracker()?.Notify_CyberwareChanged();
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            if (pawn.Faction != null && pawn.Faction.IsPlayer) Diag.Event("cyberware", $"{Diag.Describe(pawn)} -{def.defName} {tier}");
            pawn.GetCyberTracker()?.Notify_CyberwareChanged();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tier, "tier", CyberTier.T1);
            Scribe_Values.Look(ref effectsDoubled, "effectsDoubled");
        }
    }
}
