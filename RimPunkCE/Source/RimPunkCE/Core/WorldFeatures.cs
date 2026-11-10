using HarmonyLib;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>Immutable gameplay choices, owned and saved by the world rather than global mod settings.</summary>
    public class WorldComponent_RimPunkFeatures : WorldComponent
    {
        private bool gigs;
        private bool quickhacks;
        public bool Gigs => gigs;
        public bool Quickhacks => quickhacks;
        public WorldComponent_RimPunkFeatures(World world) : base(world)
        {
            gigs = WorldFeatures.DraftGigs ?? RimPunkMod.Settings?.newWorldGigs ?? false;
            quickhacks = WorldFeatures.DraftQuickhacks ?? RimPunkMod.Settings?.newWorldQuickhacks ?? true;
        }
        public override void ExposeData()
        {
            // Worlds predating these flags already contain both features. Preserve them on migration.
            Scribe_Values.Look(ref gigs, "rpcGigsEnabled", true, forceSave: true);
            Scribe_Values.Look(ref quickhacks, "rpcQuickhacksEnabled", true, forceSave: true);
        }
        internal void SetForTests(bool enableGigs, bool enableQuickhacks)
        {
            if (!GameComponent_RimPunkLiveTests.Enabled) return;
            gigs = enableGigs;
            quickhacks = enableQuickhacks;
        }
    }

    public static class WorldFeatures
    {
        internal static bool? DraftGigs;
        internal static bool? DraftQuickhacks;
        public static WorldComponent_RimPunkFeatures Current => Find.World?.GetComponent<WorldComponent_RimPunkFeatures>();
        public static bool Gigs => Current?.Gigs ?? false;
        public static bool Quickhacks => Current?.Quickhacks ?? false;
    }

    public class Dialog_RimPunkWorldFeatures : Window
    {
        public override Vector2 InitialSize => new Vector2(480f, 260f);
        public Dialog_RimPunkWorldFeatures() { doCloseButton = true; closeOnClickedOutside = true; }
        public override void DoWindowContents(Rect rect)
        {
            bool gigs = WorldFeatures.DraftGigs ?? false;
            bool hacks = WorldFeatures.DraftQuickhacks ?? true;
            var list = new Listing_Standard();
            list.Begin(rect);
            list.Label("RPC_Features_WorldExplanation".Translate());
            list.Gap();
            list.CheckboxLabeled("RPC_Features_Gigs".Translate(), ref gigs);
            list.CheckboxLabeled("RPC_Features_Quickhacks".Translate(), ref hacks);
            list.End();
            WorldFeatures.DraftGigs = gigs;
            WorldFeatures.DraftQuickhacks = hacks;
        }
    }

    [HarmonyPatch(typeof(Page_CreateWorldParams), nameof(Page_CreateWorldParams.PreOpen))]
    public static class Patch_WorldFeatureDraft
    {
        private static readonly ConditionalWeakTable<Page_CreateWorldParams, object> initialized = new ConditionalWeakTable<Page_CreateWorldParams, object>();
        public static void Postfix(Page_CreateWorldParams __instance)
        {
            if (initialized.TryGetValue(__instance, out _)) return;
            initialized.Add(__instance, new object());
            WorldFeatures.DraftGigs = RimPunkMod.Settings?.newWorldGigs ?? false;
            WorldFeatures.DraftQuickhacks = RimPunkMod.Settings?.newWorldQuickhacks ?? true;
        }
    }

    [HarmonyPatch(typeof(Page_CreateWorldParams), nameof(Page_CreateWorldParams.DoWindowContents))]
    public static class Patch_WorldFeatureButton
    {
        public static void Postfix(Rect rect)
        {
            if (Widgets.ButtonText(new Rect(rect.xMax - 205f, rect.y, 200f, 30f), "RPC_Features_Button".Translate()))
                Find.WindowStack.Add(new Dialog_RimPunkWorldFeatures());
        }
    }
}
