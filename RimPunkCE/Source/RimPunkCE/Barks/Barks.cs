using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>The lines for one kind of bark. Edit Common/Defs/Barks/Barks.xml to change them.</summary>
    public class BarkDef : Def
    {
        public List<string> lines = new List<string>();

        /// <summary>Chance this bark fires at all when its event happens.</summary>
        public float chance = 1f;

        /// <summary>Seconds before the same pawn can bark again (any kind).</summary>
        public float pawnCooldown = 12f;
    }

    /// <summary>Which BarkDef each event uses.</summary>
    public static class BarkKeys
    {
        public const string NetrunUploading = "RPC_Bark_NetrunUploading";
        public const string NetrunHacked = "RPC_Bark_NetrunHacked";
        public const string NetrunResisted = "RPC_Bark_NetrunResisted";
        public const string PsychosisStart = "RPC_Bark_PsychosisStart";
        public const string PsychosisEnd = "RPC_Bark_PsychosisEnd";
        public const string MaxTacArrive = "RPC_Bark_MaxTacArrive";
        public const string TraumaArrive = "RPC_Bark_TraumaArrive";
        public const string Retaliation = "RPC_Bark_Retaliation";
        public const string Sandevistan = "RPC_Bark_Sandevistan";
        public const string Berserk = "RPC_Bark_Berserk";
        public const string GigOffered = "RPC_Bark_GigOffered";
        public const string GigAccepted = "RPC_Bark_GigAccepted";
        public const string GigCompleted = "RPC_Bark_GigCompleted";
        public const string GigFailed = "RPC_Bark_GigFailed";
    }

    /// <summary>
    /// A line of speech. It goes into the vanilla play log as an interaction log entry, which is what bubble mods read
    /// (Interaction Bubbles postfixes PlayLog.Add and only looks at this entry family). With no such mod the same line
    /// shows as floating text instead, and it's in the pawn's social log either way.
    /// </summary>
    public class PlayLogEntry_Bark : PlayLogEntry_InteractionSinglePawn
    {
        private string line;

        public PlayLogEntry_Bark() { }

        public PlayLogEntry_Bark(InteractionDef def, Pawn pawn, string line) : base(def, pawn, null)
        {
            this.line = line;
        }

        protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog) => forceLog ? InitiatorName + ": " + line : line;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref line, "line");
        }
    }

    public static class BarkUtility
    {
        private const int MapCooldownTicks = 90;

        // not saved: a load only resets who can speak next
        private static readonly Dictionary<int, int> lastPawnBark = new Dictionary<int, int>();
        private static int lastBarkTick = -9999;
        private static InteractionDef interaction;
        private static bool? listenerPresent;

        private static InteractionDef Interaction => interaction ??= DefDatabase<InteractionDef>.GetNamedSilentFail("RPC_Bark");

        /// <summary>Does any mod listen to PlayLog.Add? Checked by behaviour, not by name, so any bubble mod counts.</summary>
        public static bool BubbleModPresent
        {
            get
            {
                if (listenerPresent == null)
                {
                    var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(PlayLog), nameof(PlayLog.Add)));
                    listenerPresent = info != null && (info.Postfixes.Count > 0 || info.Prefixes.Count > 0);
                }
                return listenerPresent.Value;
            }
        }

        /// <summary>Make <paramref name="pawn"/> say a line of this kind, if it's in range of the rate limits.</summary>
        public static void Say(Pawn pawn, string barkDefName)
        {
            if (!RimPunkMod.Settings.barks || pawn == null || !pawn.Spawned || pawn.Dead || pawn.RaceProps?.Humanlike != true) return;
            var def = DefDatabase<BarkDef>.GetNamedSilentFail(barkDefName);
            if (def == null || def.lines.NullOrEmpty() || Interaction == null) return;
            int now = Find.TickManager.TicksGame;
            if (now - lastBarkTick < MapCooldownTicks) return;
            if (lastPawnBark.TryGetValue(pawn.thingIDNumber, out int last) && now - last < CyberUtility.SecondsToTicks(def.pawnCooldown)) return;

            // hashed, not Verse.Rand: a line of speech must not move the sim's random stream
            int hash = Mathf.Abs(Gen.HashCombineInt(now, pawn.thingIDNumber));
            if (def.chance < 1f && hash % 1000 >= def.chance * 1000f) return;
            string line = def.lines[hash % def.lines.Count];

            lastBarkTick = now;
            lastPawnBark[pawn.thingIDNumber] = now;
            Find.PlayLog.Add(new PlayLogEntry_Bark(Interaction, pawn, line));
            if (!BubbleModPresent) MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, line, new Color(0.85f, 0.95f, 1f), 3.5f);
            Diag.Event("bark", $"{Diag.Describe(pawn)} [{def.defName}]: {line}");
        }

        /// <summary>A colonist on the home map says it (faction and gig news).</summary>
        public static void SayColonist(string barkDefName, Map map = null)
        {
            map ??= Find.AnyPlayerHomeMap;
            var colonists = map?.mapPawns?.FreeColonistsSpawned;
            if (colonists.NullOrEmpty()) return;
            var awake = colonists.Where(c => !c.Downed && c.Awake()).ToList();
            if (awake.Count == 0) return;
            Say(awake[Mathf.Abs(Gen.HashCombineInt(Find.TickManager.TicksGame, map.uniqueID)) % awake.Count], barkDefName);
        }
    }
}
