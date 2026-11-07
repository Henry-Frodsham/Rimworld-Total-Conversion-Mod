using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Quality Bionics Remastered (assassinsbro.qualitybionicsremastered). It only gives quality to hediffs with
    /// addedPartProps (it scales part efficiency / max HP), and cyberware has neither, so it skips all of ours.
    /// Instead, when it is loaded, cyberware items get a vanilla CompQuality and the installed hediff remembers the
    /// item's quality; <see cref="Multiplier"/> then scales the hediff's effects (stat offsets, stat factors and
    /// capacity mods, see <see cref="TieredEffects.BuildStage"/>). The multipliers come from Quality Bionics'
    /// own settings, so the player's tuning there applies here too. No hard reference to its assembly.
    /// </summary>
    public static class QualityBionicsCompat
    {
        public const string PackageId = "assassinsbro.qualitybionicsremastered";

        /// <summary>Quality Bionics' default efficiency multipliers, used if its settings can't be read.</summary>
        private static readonly float[] Defaults = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.7f, 2f };

        public static readonly bool Active = ModsConfig.IsActive(PackageId);

        private static MethodInfo getMultiplier;

        /// <summary>Called from <see cref="Startup"/> after <see cref="CyberwareDefCache.Build"/>.</summary>
        public static void Init()
        {
            if (!Active) return;
            try
            {
                var settings = GenTypes.GetTypeInAnyAssembly("QualityBionicsRemastered.Settings");
                getMultiplier = settings?.GetMethod("GetQualityMultipliers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (getMultiplier == null) RPLog.Warn("Quality Bionics Remastered is loaded but its multiplier table wasn't found, using its default values");
            }
            catch (Exception e)
            {
                RPLog.Warn("Quality Bionics Remastered: couldn't read its settings, using its default values. " + e.Message);
            }

            int count = 0;
            foreach (var hd in CyberwareDefCache.AllCyberware)
            {
                var item = hd.Ext()?.item;
                if (item == null) continue;
                item.comps ??= new System.Collections.Generic.List<CompProperties>();
                if (item.comps.Exists(c => c.compClass == typeof(CompQuality))) continue;
                item.comps.Add(new CompProperties { compClass = typeof(CompQuality) });
                count++;
            }
            RPLog.Debug($"Quality Bionics Remastered found: quality added to {count} cyberware items");
        }

        /// <summary>Effect multiplier for a quality: 1 when Quality Bionics isn't loaded.</summary>
        public static float Multiplier(QualityCategory quality)
        {
            if (!Active) return 1f;
            if (getMultiplier != null)
            {
                try { return (float)getMultiplier.Invoke(null, new object[] { quality }); }
                catch { getMultiplier = null; }
            }
            return Defaults[(int)quality];
        }

        /// <summary>Keys where a lower number is better: they're divided by the multiplier, not multiplied.</summary>
        private static readonly System.Collections.Generic.HashSet<string> LowerIsBetter = new System.Collections.Generic.HashSet<string>
        {
            "cooldown",       // time between uses
            "adrenalineAbove",   // adrenaline needed before it triggers
            "healthLost",     // damage needed within the window to trigger
            "blockWindow",    // nano plating: blocks are capped per window, a shorter window frees them sooner
        };

        /// <summary>Not scaled: on/off flags and numbers with no clear better direction.</summary>
        private static readonly System.Collections.Generic.HashSet<string> Unscaled = new System.Collections.Generic.HashSet<string>
        {
            "blackwallGateway",
            "freshSeconds",
        };

        /// <summary>
        /// A tiered number (<see cref="Hediff_Cyberware.GetValue"/>) adjusted for quality. Durations, chances, damage,
        /// healing, thresholds that trigger earlier and ranges are better higher, so they scale up with quality;
        /// cooldowns and the like scale down.
        /// </summary>
        public static float ScaleValue(string key, float value, QualityCategory quality)
        {
            if (!Active || quality == QualityCategory.Normal || Unscaled.Contains(key)) return value;
            float m = Multiplier(quality);
            if (m <= 0f) return value;
            return LowerIsBetter.Contains(key) ? value / m : value * m;
        }

        /// <summary>The quality of an item stack (Normal when it has none).</summary>
        public static QualityCategory QualityOf(Thing thing) =>
            Active && thing.TryGetComp<CompQuality>() is CompQuality q ? q.Quality : QualityCategory.Normal;

        public static void SetQuality(Thing thing, QualityCategory quality)
        {
            if (Active && thing.TryGetComp<CompQuality>() is CompQuality q) q.SetQuality(quality, ArtGenerationContext.Colony);
        }
    }
}
