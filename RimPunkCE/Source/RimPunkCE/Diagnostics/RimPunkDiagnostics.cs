using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Live-test diagnostics (mod setting "Diagnostics log"). Writes RimPunkCE/Diagnostics/diag.log:
    ///  - EVENT lines from RimPunk systems (cyberware, abilities, cyberpsychosis, adrenaline, Trauma Team, factions, gigs);
    ///  - ERROR lines for any game error whose stack passes through RimPunk code (with the stack);
    ///  - CHECK lines from invariant checks every in-game hour (state that's wrong but invisible on screen);
    ///  - STATS lines summarising event counts per period.
    /// Writes are buffered and flushed every few seconds, so the cost in play is negligible.
    /// Never touches Verse.Rand or game state: safe in multiplayer.
    /// </summary>
    public static class Diag
    {
        private static readonly object Lock = new object();
        private static readonly StringBuilder Buffer = new StringBuilder();
        private static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();
        private static string path;
        private static DateTime lastFlush = DateTime.MinValue;
        private static readonly HashSet<string> ReportedOnce = new HashSet<string>();

        public static bool Enabled => RimPunkMod.Settings?.diagnostics == true;

        /// <summary>Every game error, and whether it's ours (the live tests fail a scenario on those).</summary>
        public static System.Action<string, bool> ErrorListener;

        public static string FilePath
        {
            get
            {
                if (path == null)
                {
                    var dir = Path.Combine(RimPunkMod.ContentPack?.RootDir ?? GenFilePaths.SaveDataFolderPath, "Diagnostics");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, "diag.log");
                }
                return path;
            }
        }

        public static void Start()
        {
            if (!Enabled) return;
            try
            {
                File.WriteAllText(FilePath, $"=== RimPunk CE diagnostics, session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            }
            catch (Exception e)
            {
                Log.Warning("[RimPunk CE] diagnostics disabled, can't write " + FilePath + ": " + e.Message);
            }
        }

        private static string Stamp()
        {
            int tick = Current.Game?.tickManager?.TicksGame ?? -1;
            return $"{DateTime.Now:HH:mm:ss} t{tick}";
        }

        private static void Write(string level, string category, string msg)
        {
            if (!Enabled) return;
            try
            {
                lock (Lock)
                {
                    Buffer.Append(Stamp()).Append(' ').Append(level).Append(" [").Append(category).Append("] ").Append(msg).Append('\n');
                }
                // lines are rare: write through so nothing is lost if the game quits or crashes
                Flush();
            }
            catch
            {
                // diagnostics must never break the game
            }
        }

        public static void Event(string category, string msg)
        {
            Count(category);
            Write("EVENT", category, msg);
        }

        /// <summary>High-frequency events: only counted, summarised in STATS.</summary>
        public static void Count(string key)
        {
            if (!Enabled) return;
            lock (Lock)
            {
                Counters.TryGetValue(key, out int n);
                Counters[key] = n + 1;
            }
        }

        public static void Check(string category, string msg, string onceKey = null)
        {
            if (onceKey != null && !ReportedOnce.Add(onceKey)) return;
            Count("check:" + category);
            Write("CHECK", category, msg);
        }

        public static void Error(string msg, string stack) => Write("ERROR", "error", msg + "\n    " + stack.Replace("\n", "\n    "));

        public static void Stats()
        {
            if (!Enabled) return;
            string line;
            lock (Lock)
            {
                if (Counters.Count == 0) return;
                line = string.Join(", ", Counters.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
                Counters.Clear();
            }
            Write("STATS", "period", line);
        }

        public static void Flush()
        {
            string text;
            lock (Lock)
            {
                if (Buffer.Length == 0) return;
                text = Buffer.ToString();
                Buffer.Clear();
            }
            lastFlush = DateTime.Now;
            try { File.AppendAllText(FilePath, text); }
            catch { /* never let diagnostics break the game */ }
        }

        public static string Describe(Pawn p) => p == null ? "null" : $"{p.LabelShort}#{p.thingIDNumber}({p.Faction?.Name ?? "no faction"})";
    }

    /// <summary>Log.Error: copy any error whose stack passes through RimPunk code into diag.log with the stack.</summary>
    [HarmonyPatch(typeof(Log), nameof(Log.Error), typeof(string))]
    public static class Patch_Log_Error
    {
        [ThreadStatic] private static bool inside;

        public static void Postfix(string text)
        {
            if (inside || !Diag.Enabled) return;
            inside = true;
            try
            {
                string stack = Environment.StackTrace;
                // ignore our own frames (this patch, Diag) when deciding whether RimPunk code caused the error
                bool rimpunkFrame = stack.Split('\n').Any(l => l.Contains("RimPunkCE.") && !l.Contains("Patch_Log_Error") && !l.Contains("RimPunkCE.Diag"));
                bool ours = text != null && (text.Contains("RimPunk") || text.Contains("RPC_") || rimpunkFrame);
                if (ours) Diag.Error(text, stack);
                else Diag.Count("game-errors-elsewhere");
                Diag.ErrorListener?.Invoke(text, ours);
            }
            catch
            {
                // never throw from inside Log.Error: that recurses into the game's own error handling
            }
            finally { inside = false; }
        }
    }

    /// <summary>
    /// Invariant checks over every pawn RimPunk touches. Each problem is reported once (per pawn + problem),
    /// so a broken state doesn't flood the file.
    /// </summary>
    public static class DiagChecks
    {
        public static void RunAll()
        {
            if (!Diag.Enabled || Current.Game == null) return;
            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    try { CheckPawn(pawn); }
                    catch (Exception e) { Diag.Check("checker", $"check crashed on {Diag.Describe(pawn)}: {e.Message}", "crash" + pawn.thingIDNumber); }
                }
                CheckMap(map);
            }
            Diag.Stats();
            Diag.Flush();
        }

        private static void CheckPawn(Pawn pawn)
        {
            string who = Diag.Describe(pawn);
            int id = pawn.thingIDNumber;
            var tracker = pawn.GetCyberTracker();

            if (pawn.RaceProps.Humanlike && pawn.story?.traits != null && !pawn.Dead)
            {
                if (tracker == null) Diag.Check("tracker", $"{who} is humanlike but has no CompCyberTracker", "notracker" + id);
                if (!pawn.story.traits.HasTrait(RPDefOf.RPC_CyberAptitude)) Diag.Check("traits", $"{who} has no capacity aptitude trait", "noapt" + id);
            }
            if (tracker == null) return;

            // capacity cache vs a fresh sum
            float sum = pawn.InstalledCyberware().Sum(c => c.CapacityCost)
                        + pawn.health.hediffSet.hediffs.Where(ForeignCyberwareUtility.IsForeignImplant).Sum(ForeignCyberwareUtility.CostOf);
            if (Math.Abs(sum - tracker.CapacityUsed) > 0.01f)
                Diag.Check("capacity", $"{who}: cached usage {tracker.CapacityUsed} != actual {sum} (cache not invalidated)", "cap" + id + "_" + sum);
            float max = tracker.CapacityMax;
            if (float.IsNaN(max) || max <= 0) Diag.Check("capacity", $"{who}: capacity max is {max}", "capmax" + id);

            // slot rules
            foreach (var group in pawn.InstalledCyberware().GroupBy(c => c.Ext?.slot))
            {
                var slot = group.Key;
                if (slot == null) { Diag.Check("cyberware", $"{who}: cyberware without slot: {string.Join(",", group.Select(g => g.def.defName))}", "noslot" + id); continue; }
                if (group.Count() > slot.maxInstalled) Diag.Check("slots", $"{who}: {group.Count()} in {slot.defName} (max {slot.maxInstalled})", "slotmax" + id + slot.defName);
                if (slot.onePerPart && group.Where(c => c.Part != null).GroupBy(c => c.Part).Any(g => g.Count() > 1))
                    Diag.Check("slots", $"{who}: two {slot.defName} cyberware on one part", "slotpart" + id + slot.defName);
                if (!slot.WholeBody && group.Any(c => c.Part == null)) Diag.Check("slots", $"{who}: {slot.defName} cyberware with no body part", "slotnull" + id + slot.defName);
            }
            foreach (var c in pawn.InstalledCyberware())
            {
                var ext = c.Ext;
                if (ext != null && (c.tier < ext.minTier || c.tier > ext.maxTier))
                    Diag.Check("tiers", $"{who}: {c.def.defName} at {c.tier}, allowed {ext.minTier}-{ext.maxTier}", "tier" + id + c.def.defName);
                var give = c.TryGetComp<HediffComp_GiveCyberAbilities>();
                if (give != null && pawn.abilities != null)
                    foreach (var a in give.Props.abilities)
                        if (pawn.abilities.GetAbility(a) == null) Diag.Check("abilities", $"{who}: {c.def.defName} installed but ability {a.defName} missing", "abmiss" + id + a.defName);
            }
            foreach (var a in pawn.CyberAbilities())
                if (!(a is QuickhackAbility) && CyberUtility.SourceCyberwareFor(pawn, a.def) == null && !(pawn.equipment?.Primary?.TryGetComp<CompEquippableAbility>()?.AbilityForReading == a))
                    Diag.Check("abilities", $"{who}: has {a.def.defName} but no cyberware grants it (orphan ability)", "aborphan" + id + a.def.defName);

            // adrenaline
            if (float.IsNaN(tracker.Adrenaline) || tracker.Adrenaline < 0f || tracker.Adrenaline > tracker.AdrenalineMax + 0.01f)
                Diag.Check("adrenaline", $"{who}: adrenaline {tracker.Adrenaline}/{tracker.AdrenalineMax}", "adr" + id);

            // RAM
            if (tracker.Ram > tracker.RamMax + 0.01f) Diag.Check("ram", $"{who}: RAM {tracker.Ram} > max {tracker.RamMax}", "ram" + id);

            // cyberpsychosis
            bool psycho = CyberpsychosisUtility.InCyberpsychosis(pawn);
            bool rage = pawn.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisRage);
            if (psycho && tracker.UsageRatio < RPDefOf.RPC_CyberpsychosisTuning.safeUsage)
                Diag.Check("psychosis", $"{who}: in cyberpsychosis at {tracker.UsageRatio:P0} usage (should have ended)", "psyend" + id + Find.TickManager.TicksGame / 2500);
            if (psycho != rage) Diag.Check("psychosis", $"{who}: cyberpsychosis={psycho} but rage hediff={rage}", "psyrage" + id + psycho);
            if (psycho && !CyberpsychosisUtility.HasBurnout(pawn)) Diag.Check("psychosis", $"{who}: in cyberpsychosis without burnout", "psyburn" + id);

            // timed hediffs that should have expired
            foreach (var act in pawn.ActiveEffects())
                if (act.ticksLeft > 60 * 600) Diag.Check("abilities", $"{who}: {act.def.defName} has {act.ticksLeft} ticks left (too long)", "actlong" + id + act.def.defName);
            var thrown = pawn.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_WeaponThrown);
            if (thrown != null && thrown.ageTicks > 60 * 30) Diag.Check("weapons", $"{who}: weapon still 'thrown' after {thrown.ageTicks} ticks", "thrown" + id);

            // ammo: NPC with a RimPunk gun and no ammo at all
            var gun = pawn.equipment?.Primary;
            if (gun != null && gun.def.defName.StartsWith("RPC_Gun_") && !(pawn.Faction?.IsPlayer ?? false))
            {
                var ammo = gun.TryGetComp<CombatExtended.CompAmmoUser>();
                if (ammo != null && ammo.CurMagCount == 0 && !ammo.HasAmmo)
                    Diag.Check("weapons", $"{who}: spawned with {gun.def.defName} and no ammo (CE loadout extension?)", "noammo" + id);
            }
        }

        private static void CheckMap(Map map)
        {
            var tt = CyberFactionUtility.FirstOfKind(CyberFactionKind.TraumaTeam);
            if (tt != null)
            {
                int ttPawns = map.mapPawns.SpawnedPawnsInFaction(tt).Count;
                if (ttPawns > 0) Diag.Count("trauma-team-pawns-on-map:" + ttPawns);
                if (tt.HostileTo(Faction.OfPlayer) && !GameComponent_RimPunk.TraumaHostileToPlayer) Diag.Check("factions", "Trauma Team is hostile to the player", "tthostile");
            }
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                var ext = f.Ext();
                if (ext == null) continue;
                foreach (var r in ext.rivals)
                {
                    var rf = Find.FactionManager.FirstFactionOfDef(r);
                    if (rf != null && !f.HostileTo(rf)) Diag.Check("factions", $"{f.Name} is not hostile to rival {rf.Name}", "rival" + f.def.defName + r.defName);
                }
            }
        }
    }
}
