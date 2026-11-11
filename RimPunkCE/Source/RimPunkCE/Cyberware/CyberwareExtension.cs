using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>Operating system family. Only used for labels/logic; exclusivity comes from the slot (max 1).</summary>
    public enum CyberOsType
    {
        None,
        Sandevistan,
        Berserk,
        Cyberdeck,
        ChromeCompressor,
    }

    /// <summary>
    /// Marks a HediffDef as RimPunk cyberware. Everything an item needs is here, and install / remove /
    /// upgrade surgeries are generated from it (see <see cref="CyberwareRecipeGenerator"/>), so new content
    /// is just XML: a HediffDef with this extension + a ThingDef for the item.
    /// </summary>
    public class CyberwareExtension : DefModExtension
    {
        public CyberSlotDef slot;
        public CyberOsType osType = CyberOsType.None;

        /// <summary>How this implant makes its user fight (ContextualCombat). Most stay Neutral; the pawn's role also looks at its stats and weapon.</summary>
        public CombatRole combatRole = CombatRole.Neutral;

        /// <summary>Cyberware capacity this uses ("cyberware capacity" in the manifests).</summary>
        public float capacityCost;

        public CyberTier minTier = CyberTier.T1;
        public CyberTier maxTier = CyberTier.Iconic;

        /// <summary>Cyberware that must already be installed before this can be (basic implants need the comms implant). Removing the required one is blocked while this is installed.</summary>
        public List<HediffDef> requiresCyberware;

        /// <summary>The item. Set this on the HediffDef; the item's ThingDef needs CompProperties_CyberwareTier.</summary>
        public ThingDef item;

        /// <summary>Passive tiered effects (stat offsets/factors, capacity mods).</summary>
        public TieredEffects effects;

        /// <summary>Tiered numbers read by C# (durations, cooldowns, chances). Keys are documented per comp.</summary>
        public List<TieredNamedValue> values;

        /// <summary>Surgery work amount for install/remove/upgrade.</summary>
        public float surgeryWorkAmount = 2500f;

        [Unsaved] public HediffDef hediffDef;

        public float GetValue(string key, CyberTier tier, float fallback = 0f)
        {
            if (values != null)
                for (int i = 0; i < values.Count; i++)
                    if (values[i].key == key)
                        return values[i].value.Evaluate(TierUtility.Clamp(tier, minTier, maxTier), minTier, maxTier);
            return fallback;
        }

        public bool HasValue(string key)
        {
            if (values != null)
                for (int i = 0; i < values.Count; i++)
                    if (values[i].key == key) return true;
            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (slot == null) yield return "CyberwareExtension: slot is null";
            if (item == null) yield return "CyberwareExtension: item is null";
            if (maxTier < minTier) yield return "CyberwareExtension: maxTier < minTier";
            if (effects != null) foreach (var e in effects.ConfigErrors("CyberwareExtension")) yield return e;
            if (values != null)
                foreach (var v in values)
                {
                    if (v.key.NullOrEmpty()) yield return "CyberwareExtension: value with empty key";
                    if (v.value == null) yield return "CyberwareExtension: value " + v.key + " is null";
                    else foreach (var e in v.value.ConfigErrors("CyberwareExtension." + v.key)) yield return e;
                }
        }
    }

    /// <summary>
    /// On a ThingDef (item): links back to the cyberware hediff. Filled in automatically at startup from
    /// <see cref="CyberwareExtension.item"/>; do not author by hand.
    /// </summary>
    public static class CyberwareDefCache
    {
        private static readonly Dictionary<ThingDef, HediffDef> itemToHediff = new Dictionary<ThingDef, HediffDef>();
        public static readonly List<HediffDef> AllCyberware = new List<HediffDef>();

        public static void Build()
        {
            itemToHediff.Clear();
            AllCyberware.Clear();
            foreach (var hd in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                var ext = hd.GetModExtension<CyberwareExtension>();
                if (ext == null) continue;
                ext.hediffDef = hd;
                AllCyberware.Add(hd);
                if (ext.item != null)
                {
                    if (itemToHediff.TryGetValue(ext.item, out var other))
                        RPLog.Error($"{ext.item.defName} is the item for both {other.defName} and {hd.defName}");
                    else itemToHediff[ext.item] = hd;
                }
            }
        }

        public static HediffDef HediffFor(ThingDef item) => item != null && itemToHediff.TryGetValue(item, out var hd) ? hd : null;

        public static CyberwareExtension Ext(this HediffDef def) => def?.GetModExtension<CyberwareExtension>();

        public static bool IsCyberwareItem(this ThingDef def) => HediffFor(def) != null;
    }
}
