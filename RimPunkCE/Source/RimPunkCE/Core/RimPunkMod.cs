using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>Mod entry point. Holds settings; Harmony and startup work live in <see cref="Startup"/>.</summary>
    public class RimPunkMod : Mod
    {
        public static RimPunkSettings Settings;
        public static ModContentPack Content;

        public RimPunkMod(ModContentPack content) : base(content)
        {
            Content = content;
            Settings = GetSettings<RimPunkSettings>();
        }

        public override string SettingsCategory() => "RimPunk CE";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("RPC_Settings_CapacityMultiplier".Translate(Settings.capacityMultiplier.ToStringPercent()));
            Settings.capacityMultiplier = list.Slider(Settings.capacityMultiplier, 0.25f, 4f);
            list.Label("RPC_Settings_CyberpsychosisChanceMultiplier".Translate(Settings.cyberpsychosisChanceMultiplier.ToStringPercent()));
            Settings.cyberpsychosisChanceMultiplier = list.Slider(Settings.cyberpsychosisChanceMultiplier, 0f, 4f);
            list.CheckboxLabeled("RPC_Settings_VisualEffects".Translate(), ref Settings.visualEffects);
            list.CheckboxLabeled("RPC_Settings_StaminaEnabled".Translate(), ref Settings.staminaEnabled);
            list.CheckboxLabeled("RPC_Settings_DebugLogging".Translate(), ref Settings.debugLogging);
            list.End();
        }
    }

    public class RimPunkSettings : ModSettings
    {
        /// <summary>Multiplies every pawn's max cyberware capacity (not Gonko's hard 20).</summary>
        public float capacityMultiplier = 1f;
        public float cyberpsychosisChanceMultiplier = 1f;
        public bool visualEffects = true;
        public bool staminaEnabled = true;
        public bool debugLogging;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref capacityMultiplier, "capacityMultiplier", 1f);
            Scribe_Values.Look(ref cyberpsychosisChanceMultiplier, "cyberpsychosisChanceMultiplier", 1f);
            Scribe_Values.Look(ref visualEffects, "visualEffects", true);
            Scribe_Values.Look(ref staminaEnabled, "staminaEnabled", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging");
        }
    }

    internal static class RPLog
    {
        public static void Debug(string msg)
        {
            if (RimPunkMod.Settings?.debugLogging == true) Log.Message("[RimPunk CE] " + msg);
        }

        public static void Warn(string msg) => Log.Warning("[RimPunk CE] " + msg);
        public static void Error(string msg) => Log.Error("[RimPunk CE] " + msg);
    }
}
