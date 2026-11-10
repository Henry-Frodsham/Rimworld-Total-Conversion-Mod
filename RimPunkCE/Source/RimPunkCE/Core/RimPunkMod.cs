using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>Mod entry point: settings and Harmony. Post-def-load work lives in <see cref="Startup"/>.</summary>
    public class RimPunkMod : Mod
    {
        public static RimPunkSettings Settings;
        public static ModContentPack ContentPack;

        public RimPunkMod(ModContentPack content) : base(content)
        {
            ContentPack = content;
            Settings = GetSettings<RimPunkSettings>();
            // patched here, not in a [StaticConstructorOnStartup], because the implied-recipe patch
            // (DefGenerator.GenerateImpliedDefs_PreResolve) must be live before defs are loaded
            new HarmonyLib.Harmony(Startup.HarmonyId).PatchAll(typeof(RimPunkMod).Assembly);
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
            list.CheckboxLabeled("RPC_Settings_AdrenalineEnabled".Translate(), ref Settings.adrenalineEnabled);
            list.CheckboxLabeled("RPC_Settings_ContextualAI".Translate(), ref Settings.contextualAI, "RPC_Settings_ContextualAITip".Translate());
            list.CheckboxLabeled("RPC_Settings_Barks".Translate(), ref Settings.barks);
            list.CheckboxLabeled("RPC_Settings_FactionsDefault".Translate(), ref Settings.cyberpunkFactionsDefault, "RPC_Settings_FactionsDefaultTip".Translate());
            list.CheckboxLabeled("RPC_Settings_DebugLogging".Translate(), ref Settings.debugLogging);
            list.CheckboxLabeled("RPC_Settings_Diagnostics".Translate(), ref Settings.diagnostics, "RPC_Settings_DiagnosticsTip".Translate());
            list.Gap();
            list.Label("RPC_Features_Defaults".Translate());
            list.CheckboxLabeled("RPC_Features_Gigs".Translate(), ref Settings.newWorldGigs);
            list.CheckboxLabeled("RPC_Features_Quickhacks".Translate(), ref Settings.newWorldQuickhacks);
            if (WorldFeatures.Current != null)
                list.Label("RPC_Features_Current".Translate(WorldFeatures.Gigs ? "On" : "Off", WorldFeatures.Quickhacks ? "On" : "Off"));
            list.End();
        }
    }

    public class RimPunkSettings : ModSettings
    {
        /// <summary>Multiplies every pawn's max cyberware capacity (not Gonk's hard 20).</summary>
        public float capacityMultiplier = 1f;
        public float cyberpsychosisChanceMultiplier = 1f;
        public bool newWorldGigs;
        public bool newWorldQuickhacks = true;
        public bool visualEffects = true;
        public bool adrenalineEnabled = true;
        public bool contextualAI = true;
        public bool barks = true;
        public bool debugLogging;
        /// <summary>Corps and gangs are the default factions in a new world (vanilla still selectable).</summary>
        public bool cyberpunkFactionsDefault = true;
        /// <summary>Write RimPunkCE/Diagnostics/diag.log (events, RimPunk errors, invariant checks). On while testing.</summary>
        public bool diagnostics = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref newWorldGigs, "newWorldGigs");
            Scribe_Values.Look(ref newWorldQuickhacks, "newWorldQuickhacks", true);
            Scribe_Values.Look(ref capacityMultiplier, "capacityMultiplier", 1f);
            Scribe_Values.Look(ref cyberpsychosisChanceMultiplier, "cyberpsychosisChanceMultiplier", 1f);
            Scribe_Values.Look(ref visualEffects, "visualEffects", true);
            Scribe_Values.Look(ref adrenalineEnabled, "adrenalineEnabled", true);
            Scribe_Values.Look(ref contextualAI, "contextualAI", true);
            Scribe_Values.Look(ref barks, "barks", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging");
            Scribe_Values.Look(ref cyberpunkFactionsDefault, "cyberpunkFactionsDefault", true);
            Scribe_Values.Look(ref diagnostics, "diagnostics", true);
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
