using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// The comms implant's whole job: two colonists who both have one can chat from anywhere on the map. The chat is a
    /// normal vanilla social interaction (same thoughts, same social log), just not tied to standing next to each other,
    /// and its speech bubble is drawn blue. It happens at a quarter of the vanilla chatter rate.
    /// </summary>
    public static class CommsUtility
    {
        public const string ImplantDefName = "RPC_CommsImplant";

        /// <summary>Vanilla's "normal" random-chat mean time (Pawn_InteractionsTracker.RandomInteractMTBTicks_Normal), per pawn.</summary>
        public const float VanillaChatMtbTicks = 6600f;

        /// <summary>Comms chats happen at this fraction of the vanilla rate.</summary>
        public const float RateFactor = 0.25f;

        /// <summary>Partners closer than this with a clear line just talk out loud: the implant is for distance.</summary>
        public const float AloudDistance = 12f;

        public static readonly Color BubbleBackground = new Color(0.45f, 0.7f, 1f);
        public static readonly Color BubbleBorder = new Color(0.08f, 0.28f, 0.7f);

        /// <summary>True only while a comms chat is being made (lifts the "must be standing near" rule and tags the log entry blue).</summary>
        public static bool Active;

        private static HediffDef implantDef;
        private static List<InteractionDef> chatDefs;

        // ids of the most recent comms log entries (bubbles read them); old ones age out
        private static readonly Queue<int> recentEntries = new Queue<int>();
        private static readonly HashSet<int> recentSet = new HashSet<int>();

        public static HediffDef ImplantDef => implantDef ??= DefDatabase<HediffDef>.GetNamedSilentFail(ImplantDefName);

        public static bool HasImplant(Pawn p)
        {
            var def = ImplantDef;
            return def != null && p?.health?.hediffSet != null && p.health.hediffSet.HasHediff(def);
        }

        /// <summary>The chats comms carries: friendly small talk only (no insults, no romance, nothing that starts a fight).</summary>
        private static List<InteractionDef> ChatDefs => chatDefs ??= new[] { "Chitchat", "DeepTalk", "KindWords" }
            .Select(n => DefDatabase<InteractionDef>.GetNamedSilentFail(n)).Where(d => d != null && d.socialFightBaseChance <= 0f).ToList();

        public static bool IsCommsEntry(LogEntry entry) => entry != null && recentSet.Contains(entry.LogID);

        public static void MarkEntry(LogEntry entry)
        {
            if (entry == null || !recentSet.Add(entry.LogID)) return;
            recentEntries.Enqueue(entry.LogID);
            while (recentEntries.Count > 64) recentSet.Remove(recentEntries.Dequeue());
        }

        /// <summary>Can this colonist start a random comms chat right now?</summary>
        public static bool CanTalk(Pawn p) =>
            p != null && p.Spawned && !p.Dead && !p.Downed && p.Awake() && !p.Drafted && !p.InMentalState && p.RaceProps.Humanlike
            && p.interactions != null && HasImplant(p) && SocialInteractionUtility.CanInitiateRandomInteraction(p);

        /// <summary>Vanilla rolls once per 60 ticks per pawn; comms rolls the same way at a quarter of the rate.</summary>
        public static bool RollsChat() => Rand.MTBEventOccurs(VanillaChatMtbTicks / RateFactor, 1f, 60f);

        /// <summary>A far-enough partner on this map, or null.</summary>
        public static Pawn PickPartner(Pawn initiator)
        {
            foreach (var other in initiator.Map.mapPawns.FreeColonistsSpawned.InRandomOrder())
            {
                if (other == initiator || !HasImplant(other) || other.Downed || !other.Awake() || other.InMentalState) continue;
                if (!SocialInteractionUtility.CanReceiveRandomInteraction(other) || initiator.HostileTo(other)) continue;
                if (initiator.Position.InHorDistOf(other.Position, AloudDistance) && GenSight.LineOfSight(initiator.Position, other.Position, initiator.Map)) continue;
                return other;
            }
            return null;
        }

        /// <summary>Make <paramref name="initiator"/> chat to <paramref name="recipient"/> over comms. Returns true if it happened.</summary>
        public static bool TryChat(Pawn initiator, Pawn recipient)
        {
            var defs = ChatDefs;
            if (defs.Count == 0 || initiator?.interactions == null || recipient == null) return false;
            bool was = Active;
            Active = true;
            try
            {
                if (!defs.TryRandomElementByWeight(d => initiator.interactions.CanInteractNowWith(recipient, d) ? d.Worker.RandomSelectionWeight(initiator, recipient) : 0f, out var chosen)) return false;
                bool done = initiator.interactions.TryInteractWith(recipient, chosen);
                if (done) Diag.Event("comms", $"{Diag.Describe(initiator)} -> {Diag.Describe(recipient)} [{chosen.defName}]");
                return done;
            }
            finally { Active = was; }
        }
    }

    /// <summary>Gives every comms-implanted colonist their (quarter-rate) chance to chat with another one, once per 60 ticks.</summary>
    public class GameComponent_Comms : GameComponent
    {
        public GameComponent_Comms(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 != 0 || CommsUtility.ImplantDef == null) return;
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var colonists = maps[m].mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    var p = colonists[i];
                    if (!CommsUtility.CanTalk(p) || p.interactions.InteractedTooRecentlyToInteract()) continue;
                    if (!CommsUtility.RollsChat()) continue;
                    var partner = CommsUtility.PickPartner(p);
                    if (partner != null) CommsUtility.TryChat(p, partner);
                }
            }
        }
    }

    /// <summary>During a comms chat the "standing within chat distance" check passes.</summary>
    [HarmonyPatch]
    public static class Patch_Comms_PositionCheck
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SocialInteractionUtility), nameof(SocialInteractionUtility.IsGoodPositionForInteraction), new[] { typeof(Pawn), typeof(Pawn) });
            yield return AccessTools.Method(typeof(SocialInteractionUtility), nameof(SocialInteractionUtility.IsGoodPositionForInteraction), new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map) });
        }

        public static bool Prefix(ref bool __result)
        {
            if (!CommsUtility.Active) return true;
            __result = true;
            return false;
        }
    }

    /// <summary>Tags the log entry that the comms chat creates, so its bubble can be told apart.</summary>
    [HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]
    public static class Patch_Comms_TagEntry
    {
        public static void Prefix(LogEntry entry)
        {
            if (CommsUtility.Active && entry is PlayLogEntry_Interaction) CommsUtility.MarkEntry(entry);
        }
    }

    /// <summary>
    /// Interaction Bubbles (optional) draws every bubble with its global colours. For a comms bubble, swap them to blue
    /// for the length of its Draw call and put them back. Looked up by name so there is no compile-time reference.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Bubbles_CommsColour
    {
        private static MethodBase draw;
        private static PropertyInfo entryProp;
        private static FieldInfo[] colourFields;   // the four Setting<Color>.Value fields, in order: bg, fg, selected bg, selected fg
        private static readonly object[] saved = new object[4];
        private static bool swapped;

        private static bool Prepare()
        {
            var bubble = AccessTools.TypeByName("Bubbles.Core.Bubble");
            var settings = AccessTools.TypeByName("Bubbles.Settings");
            if (bubble == null || settings == null) return false;   // Interaction Bubbles not loaded: nothing to recolour
            draw = AccessTools.Method(bubble, "Draw");
            entryProp = AccessTools.Property(bubble, "Entry");
            colourFields = new[] { "Background", "Foreground", "SelectedBackground", "SelectedForeground" }
                .Select(n => AccessTools.Field(settings, n)?.GetValue(null)).Select(o => o?.GetType().GetField("Value")).ToArray();
            if (draw == null || entryProp == null || colourFields.Any(f => f == null) || !TryGetSettings(settings))
            {
                RPLog.Error("Interaction Bubbles changed shape; comms bubbles will not be blue.");
                return false;
            }
            return true;
        }

        private static object[] settingObjects;

        private static bool TryGetSettings(System.Type settings)
        {
            settingObjects = new[] { "Background", "Foreground", "SelectedBackground", "SelectedForeground" }.Select(n => AccessTools.Field(settings, n)?.GetValue(null)).ToArray();
            return settingObjects.All(o => o != null);
        }

        private static MethodBase TargetMethod() => draw;

        public static void Prefix(object __instance)
        {
            swapped = false;
            if (!(entryProp.GetValue(__instance) is LogEntry entry) || !CommsUtility.IsCommsEntry(entry)) return;
            for (int i = 0; i < 4; i++) saved[i] = colourFields[i].GetValue(settingObjects[i]);
            colourFields[0].SetValue(settingObjects[0], CommsUtility.BubbleBackground);
            colourFields[1].SetValue(settingObjects[1], CommsUtility.BubbleBorder);
            colourFields[2].SetValue(settingObjects[2], Color.Lerp(CommsUtility.BubbleBackground, Color.white, 0.35f));
            colourFields[3].SetValue(settingObjects[3], CommsUtility.BubbleBorder);
            swapped = true;
        }

        public static void Finalizer()
        {
            if (!swapped) return;
            swapped = false;
            for (int i = 0; i < 4; i++) colourFields[i].SetValue(settingObjects[i], saved[i]);
        }
    }
}
