using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RimPunkCE
{
    /// <summary>Marks a method of GameComponent_RimPunkLiveTests as a scenario. It yields how many ticks to wait before going on.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class LiveScenarioAttribute : Attribute
    {
        public readonly string group;
        public LiveScenarioAttribute(string group) => this.group = group;
    }

    /// <summary>
    /// Live test suite: scenarios that run in the real game, on a throwaway quick-test map. Start it with
    ///     python Source/Tools/run_live_tests.py [--filter text]
    /// which launches RimWorld with -quicktest -rimpunktests[=filter] and an isolated data folder. Without that flag none of
    /// this does anything. Results: Diagnostics/livetest.log (PASS / FAIL / WARN lines, ending in SUMMARY); the game then quits.
    /// A scenario fails on a failed check, an exception, or any error logged with RimPunk in its message or stack while it ran.
    /// To add one: a method in a Scenarios_*.cs file with [LiveScenario("group")] returning IEnumerable&lt;int&gt;.
    /// </summary>
    public partial class GameComponent_RimPunkLiveTests : GameComponent
    {
        public const string Flag = "rimpunktests";
        private const string SaveName = "RimPunkLiveTests";

        // static: these survive the save / load scenario, which replaces this component
        private static bool reloading;
        private static int pass;
        private static int fail;
        private static int warn;
        private static readonly List<string> failures = new List<string>();
        private static int scenariosRun;
        private static string resultsPath;
        private static DateTime startedAt;
        private static bool finished;
        private static int savedGigs;
        private static float savedAdrenaline;
        private static int savedPawnId;

        private readonly Queue<MethodInfo> queue = new Queue<MethodInfo>();
        private IEnumerator<int> current;
        private string currentName;
        private int waitUntil;
        private bool started;
        private int checksAtStart;
        private readonly List<string> scenarioErrors = new List<string>();

        private Map map;
        private Faction enemy;

        public GameComponent_RimPunkLiveTests(Game game) { }

        private static bool? enabled;

        /// <summary>The flag, with or without =filter. Read once: it's asked every tick.</summary>
        public static bool Enabled => enabled ??= GenCommandLine.CommandLineArgPassed(Flag) || GenCommandLine.TryGetCommandLineArg(Flag, out _);

        public override void GameComponentUpdate()
        {
            // the tick clock only runs unpaused: letters and combat try to stop it, so keep it at top speed
            if (Enabled && started && (Find.TickManager.Paused || Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast)) Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
        }

        public override void GameComponentTick()
        {
            if (!Enabled) return;
            if (!started)
            {
                if (Find.TickManager.TicksGame < 30 && !reloading) return;
                Begin();
            }
            if (Find.TickManager.TicksGame < waitUntil) return;
            Step();
        }

        /// <summary>The scenarios need one of each RimPunk faction, whatever the quick-test world came with.</summary>
        private static void EnsureFactions()
        {
            foreach (var def in DefDatabase<FactionDef>.AllDefsListForReading.Where(d => d.HasModExtension<CyberFactionExtension>()))
            {
                if (Find.FactionManager.FirstFactionOfDef(def) != null) continue;
                Find.FactionManager.Add(FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def)));
            }
            CyberFactionUtility.ApplyRivalries();
            foreach (var kind in new[] { CyberFactionKind.Corporation, CyberFactionKind.Gang })
            {
                var f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.Ext()?.kind == kind && !x.defeated);
                if (f != null && !f.HostileTo(Faction.OfPlayer)) CyberFactionUtility.ForceRelation(f, Faction.OfPlayer, FactionRelationKind.Hostile);
            }
        }

        private void Begin()
        {
            started = true;
            if (!reloading) EnsureFactions();
            map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            enemy = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.Ext()?.kind is CyberFactionKind.Corporation or CyberFactionKind.Gang && f.HostileTo(Faction.OfPlayer))
                    ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false) ?? Find.FactionManager.OfAncientsHostile;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            Diag.ErrorListener = OnGameError;

            if (!reloading)
            {
                startedAt = DateTime.Now;
                resultsPath = Path.Combine(Path.GetDirectoryName(Diag.FilePath), "livetest.log");
                File.WriteAllText(resultsPath, $"=== RimPunk CE live tests {DateTime.Now:yyyy-MM-dd HH:mm:ss}, map {map}, enemy {enemy?.Name} ===\n");
                foreach (var f in Find.FactionManager.AllFactionsListForReading.Where(f => f.Ext() != null && f.def.humanlikeFaction && f.leader == null))
                    Write($"NOTE start: {f.Name} has no leader at the start of the run");
            }
            GenCommandLine.TryGetCommandLineArg(Flag, out string filter);
            var all = typeof(GameComponent_RimPunkLiveTests).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.GetCustomAttribute<LiveScenarioAttribute>() != null)
                .OrderBy(m => m.GetCustomAttribute<LiveScenarioAttribute>().group == "last" ? 1 : 0)
                .ThenBy(m => m.GetCustomAttribute<LiveScenarioAttribute>().group).ThenBy(m => m.Name).ToList();
            if (reloading) queue.Enqueue(typeof(GameComponent_RimPunkLiveTests).GetMethod(nameof(Scenario_AfterReload), BindingFlags.Instance | BindingFlags.NonPublic));
            else
                foreach (var m in all)
                {
                    string g = m.GetCustomAttribute<LiveScenarioAttribute>().group;
                    bool named = m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || g.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    // "showcase" holds the game for minutes: only when asked for by name
                    if (g == "showcase" ? !filter.NullOrEmpty() && named : filter.NullOrEmpty() || named)
                        queue.Enqueue(m);
                }
        }

        private void Step()
        {
            while (true)
            {
                if (current == null)
                {
                    if (queue.Count == 0)
                    {
                        Finish();
                        return;
                    }
                    var m = queue.Dequeue();
                    currentName = m.Name.Replace("Scenario_", "");
                    scenariosRun++;
                    scenarioErrors.Clear();
                    Write("--- " + currentName);
                    checksAtStart = pass + fail;
                    try
                    {
                        if (!reloading) Prepare(); // after a reload the point is to look at what was saved
                        current = ((IEnumerable<int>)m.Invoke(this, null)).GetEnumerator();
                    }
                    catch (Exception e)
                    {
                        Check(false, "threw starting: " + (e.InnerException ?? e));
                        current = null;
                        continue;
                    }
                }
                try
                {
                    if (!current.MoveNext())
                    {
                        EndScenario();
                        continue;
                    }
                }
                catch (Exception e)
                {
                    Check(false, "threw: " + e);
                    EndScenario();
                    continue;
                }
                waitUntil = Find.TickManager.TicksGame + Mathf.Max(1, current.Current);
                return;
            }
        }

        private void EndScenario()
        {
            current = null;
            foreach (string e in scenarioErrors.Distinct().Take(5)) Check(false, "error logged: " + e);
            if (pass + fail == checksAtStart) Check(false, "scenario made no checks");
        }

        private void OnGameError(string text, bool rimpunk)
        {
            string line = (text ?? "null").Split('\n')[0];
            if (line.Length > 400) line = line.Substring(0, 400);
            if (rimpunk) scenarioErrors.Add(line);
            else
            {
                warn++;
                Write($"WARN {currentName}: other error: {line}");
            }
        }

        private void Check(bool ok, string what)
        {
            if (ok) pass++;
            else
            {
                fail++;
                failures.Add(currentName + ": " + what.Split('\n')[0]);
            }
            Write((ok ? "PASS " : "FAIL ") + currentName + ": " + what);
        }

        private void Note(string what) => Write($"NOTE {currentName}: {what}");

        private static void Write(string line)
        {
            try { File.AppendAllText(resultsPath, $"t{Find.TickManager?.TicksGame} {line}\n"); }
            catch { /* the run carries on */ }
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            DiagChecks.RunAll();
            Write($"SUMMARY {scenariosRun} scenarios: {pass} passed, {fail} failed, {warn} warnings, {(DateTime.Now - startedAt).TotalSeconds:0}s");
            foreach (string f in failures) Write("FAILED " + f);
            Diag.ErrorListener = null;
            Diag.Flush();
            Root.Shutdown();
        }

        // --------------------------------------------------------------------------------------- helpers

        private IntVec3 Center => map.Center;
        private readonly List<Pawn> spawned = new List<Pawn>();

        /// <summary>Between scenarios: what the last one spawned goes, and the arena is flat, clear and unroofed.</summary>
        private void Prepare()
        {
            foreach (var p in spawned) Remove(p);
            spawned.Clear();
            if (Current.Game.CurrentMap != map) Current.Game.CurrentMap = map;
            foreach (var c in GenRadial.RadialCellsAround(Center, 25f, true))
            {
                if (!c.InBounds(map)) continue;
                foreach (var t in c.GetThingList(map).ToList())
                    if (!(t is Pawn) && !t.Destroyed && t.def.destroyable) t.Destroy();
                if (c.GetTerrain(map).passability == Traversability.Impassable || c.GetTerrain(map).IsWater) map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                map.roofGrid.SetRoof(c, null);
                if (c.Fogged(map)) map.fogGrid.Unfog(c);
            }
            // the map's own colonists wait in a corner, and anyone left in the arena goes, so nobody joins in
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p.IsColonist && p.RaceProps.Humanlike)
                {
                    p.jobs?.StopAll();
                    if (p.Position.InHorDistOf(Center, 45f)) p.Position = CellFinder.RandomClosewalkCellNear(new IntVec3(5, 0, 5), map, 4);
                }
                else if (!(p.ParentHolder is PawnFlyer)) Remove(p);
            }
            foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.DropPodIncoming).ToList()) t.Destroy();
            foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.ActiveDropPod).ToList()) t.Destroy();
            foreach (var l in map.lordManager.lords.Where(l => l.ownedPawns.Count == 0).ToList()) map.lordManager.RemoveLord(l);
        }

        /// <summary>A faction's leader can end up in a squad: destroying them outright leaves the faction with no leader and no successor.</summary>
        private static void Remove(Pawn p)
        {
            if (p == null || p.Destroyed) return;
            if (p.Faction?.leader == p)
            {
                if (p.Spawned) p.DeSpawn();
                if (!Find.WorldPawns.Contains(p)) Find.WorldPawns.PassToWorld(p);
                return;
            }
            p.Destroy();
        }

        private Pawn Spawn(PawnKindDef kind, Faction faction, IntVec3 at, ThingDef gun = null)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, forceGenerateNewPawn: true, mustBeCapableOfViolence: true));
            foreach (var c in p.InstalledCyberware().ToList()) p.health.RemoveHediff(c);
            GenSpawn.Spawn(p, at, map);
            spawned.Add(p);
            if (gun != null) RimPunkDebugActions_Testing.Equip(p, gun);
            else p.equipment?.DestroyAllEquipment();
            return p;
        }

        private Pawn Colonist(IntVec3 at, ThingDef gun = null) => Spawn(PawnKindDefOf.Colonist, Faction.OfPlayer, at, gun);

        private Pawn Hostile(IntVec3 at, ThingDef gun = null)
        {
            var p = Spawn(PawnKindDefOf.Villager, enemy, at, gun);
            LordMaker.MakeNewLord(enemy, new LordJob_AssaultColony(enemy, canKidnap: false, canTimeoutOrFlee: false, sappers: false, useAvoidGridSmart: false, canSteal: false), map, new[] { p });
            return p;
        }

        /// <summary>A hostile that just stands there being a target.</summary>
        private Pawn Dummy(IntVec3 at)
        {
            var p = Spawn(PawnKindDefOf.Villager, enemy, at);
            p.health.AddHediff(GameComponent_Gigs.Tuning.restrained);
            return p;
        }

        /// <summary>A hostile on their feet, stunned in place. Downed pawns don't count for neutralize events or "enemy near".</summary>
        private Pawn Standing(IntVec3 at)
        {
            var p = Spawn(PawnKindDefOf.Villager, enemy, at);
            p.stances.stunner.StunFor(100000, null, addBattleLog: false, showMote: false);
            return p;
        }

        /// <summary>Capacity 20, whatever the roll: a couple of implants puts this pawn well over 80%.</summary>
        private static void MakeGonk(Pawn p)
        {
            var old = p.story.traits.GetTrait(RPDefOf.RPC_CyberAptitude);
            if (old != null) p.story.traits.RemoveTrait(old);
            p.story.traits.GainTrait(new Trait(RPDefOf.RPC_CyberAptitude, AptitudeDegree.Gonk));
        }

        private static HediffDef Hediff(string name) => DefDatabase<HediffDef>.GetNamed(name);

        private static ThingDef Thing(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static float PowerOf(string hediff) => (Hediff(hediff).CompProps<HediffCompProperties_VerbGiver>()?.tools?.FirstOrDefault()?.power) ?? 0f;

        private static CyberwareAbility AbilityFrom(Pawn p, CyberBuffAiRule rule) =>
            p.abilities.AllAbilitiesForReading.OfType<CyberwareAbility>().FirstOrDefault(a => a.CompOfType<CompAbilityEffect_CyberBuff>()?.Props.aiRule == rule);

        private static bool BuffActive(CyberwareAbility a) => a?.CompOfType<CompAbilityEffect_CyberBuff>()?.IsActive == true;
    }
}

namespace RimPunkCE.Patches
{
    /// <summary>
    /// TimeSlower.SignalForceNormalSpeed: combat drops the game to normal speed for a few seconds. Only while the live tests
    /// run, it's ignored, or every fight scenario would crawl. No effect in normal play.
    /// </summary>
    [HarmonyPatch(typeof(TimeSlower), nameof(TimeSlower.SignalForceNormalSpeed))]
    public static class Patch_TimeSlower_LiveTests
    {
        public static bool Prefix() => !GameComponent_RimPunkLiveTests.Enabled;
    }
}
