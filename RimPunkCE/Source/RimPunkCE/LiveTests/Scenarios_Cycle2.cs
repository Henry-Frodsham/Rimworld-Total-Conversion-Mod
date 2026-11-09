using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatExtended;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>Cycle 2 integration suites: real CE armour, cast jobs, damage, treatment, extraction, and Scribe.</summary>
    public partial class GameComponent_RimPunkLiveTests
    {
        private Pawn Cycle2Runner(IntVec3 at)
        {
            var p = Colonist(at);
            RimPunkDebugActions_Scenarios.LearnAllAt(p, CyberTier.T5);
            p.GetCyberTracker().GainRam(9999f);
            p.drafter.Drafted = true;
            return p;
        }

        [LiveScenario("cycle2-armour")]
        private IEnumerable<int> Scenario_C2_ArmourTierAndCoverageMatrix()
        {
            var p = Colonist(Center);
            p.GetCyberTracker().Debug_SetBaseCapacity(1000f);
            var torso = p.RaceProps.body.corePart;
            float plain = ArmorUtilityCE.PartialStat(p, StatDefOf.ArmorRating_Sharp, torso);
            foreach (CyberTier tier in Enum.GetValues(typeof(CyberTier)))
            {
                foreach (var c in p.InstalledCyberware().ToList()) p.health.RemoveHediff(c);
                RimPunkDebugActions.DebugInstall(p, Hediff("RPC_GorillaArms"), tier);
                RimPunkDebugActions.DebugInstall(p, Hediff("RPC_BionicJoints"), tier);
                var arm = p.InstalledCyberware().First(c => c.Part != null).Part;
                ChromePlatingUtility.Totals(p, arm, out float general, out float generalB, out float hp);
                Check(general == 0f && generalB == 0f && hp > 1f, $"{tier}: general chrome supplies health and zero armour");
                Check(Mathf.Approximately(ArmorUtilityCE.PartialStat(p, StatDefOf.ArmorRating_Sharp, torso), plain), $"{tier}: general chrome does not armour torso");
                foreach (var c in p.InstalledCyberware().ToList()) p.health.RemoveHediff(c);
                foreach (string name in new[] { "Chitin", "CellularAdapter", "Carapace", "ParaBellum", "RaraAvis" })
                    RimPunkDebugActions.DebugInstall(p, Hediff("RPC_" + name), tier);
                foreach (var part in p.RaceProps.body.AllParts.Where(b => b.depth == BodyPartDepth.Outside))
                {
                    ChromePlatingUtility.Totals(p, part, out float sharp, out float blunt, out _);
                    Check(sharp > 0f && sharp <= 10.001f && blunt > 0f && blunt <= 25.001f, $"{tier}/{part.Label}: plating {sharp:0.###} mm / {blunt:0.###} MPa within ceiling");
                    if (part.IsInGroup(CE_BodyPartGroupDefOf.CoveredByNaturalArmor))
                        Check(ArmorUtilityCE.PartialStat(p, StatDefOf.ArmorRating_Sharp, part) <= plain + 10.001f, $"{tier}/{part.Label}: CE has no uncapped part bonus");
                }
                if (tier == CyberTier.Iconic)
                {
                    ChromePlatingUtility.Totals(p, torso, out float capS, out _, out _);
                    Check(Mathf.Approximately(capS, 10f), "dedicated implants alone reach the sharp ceiling");
                }
            }
            float natural = p.GetStatValue(StatDefOf.ArmorRating_Sharp);
            var apparel = (Apparel)ThingMaker.MakeThing(Thing("Apparel_PowerArmor"));
            p.apparel.Wear(apparel, dropReplacedApparel: false);
            Check(ArmorUtilityCE.PartialStat(apparel, StatDefOf.ArmorRating_Sharp, torso) > 0f, "apparel keeps separate CE protection above implant cap");
            Check(Mathf.Approximately(p.GetStatValue(StatDefOf.ArmorRating_Sharp), natural), "wearing armour does not consume the natural/plating allowance");
            yield break;
        }

        [LiveScenario("cycle2-armour")]
        private IEnumerable<int> Scenario_C2_MultipliersNeedPlating()
        {
            var p = Colonist(Center);
            var torso = p.RaceProps.body.corePart;
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_ParaBellum"), CyberTier.Iconic);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_RaraAvis"), CyberTier.Iconic);
            ChromePlatingUtility.Totals(p, torso, out float empty, out _, out _);
            Check(empty == 0f, "multipliers alone supply no flat armour");
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_SubdermalArmor"), CyberTier.Iconic);
            ChromePlatingUtility.Totals(p, torso, out float boosted, out _, out _);
            foreach (var c in p.InstalledCyberware().Where(c => c.def.defName is "RPC_ParaBellum" or "RPC_RaraAvis").ToList()) p.health.RemoveHediff(c);
            ChromePlatingUtility.Totals(p, torso, out float unboosted, out _, out _);
            Check(boosted > unboosted && unboosted > 0f, $"multipliers strengthen plating: {unboosted} -> {boosted}");
            yield break;
        }

        [LiveScenario("cycle2-upload")]
        private IEnumerable<int> Scenario_C2_UploadTimingAndSuccessfulCommitment()
        {
            float previous = 0f;
            for (int distance = 0; distance <= 80; distance += 2)
            {
                float seconds = Verb_Quickhack.SecondsFor(distance, 40f);
                Check(seconds >= 60f && seconds <= 120f && seconds >= previous, $"distance {distance}: bounded, monotonic upload {seconds}s");
                previous = seconds;
            }
            Check(Verb_Quickhack.SecondsFor(0f, 40f) == 60f && Verb_Quickhack.SecondsFor(40f, 40f) == 120f, "60s near / 120s at maximum range");
            Check(Verb_Quickhack.SecondsFor(40f, 40f, 2f) < Verb_Quickhack.SecondsFor(40f, 40f), "upload-speed bonus still helps at distance");
            var caster = Cycle2Runner(Center);
            var target = Standing(Center + new IntVec3(10, 0, 0));
            var hack = QuickhackDefOf.RPC_Quickhack_ShortCircuit;
            int before = MapComponent_Netrun.For(map).ResultCount(NetrunResult.Uploaded);
            NetrunUtility.QueueUpload(caster, hack, target);
            yield return 15;
            Check(Verb_Quickhack.IsUploading(caster, out var stance), "real casting job starts an upload");
            int remaining = stance?.ticksLeft ?? 0;
            Check(remaining >= 3500 && remaining <= 7200, $"actual stance duration in commitment range: {remaining} ticks");
            var origin = caster.Position;
            yield return 120;
            Check(caster.Position == origin && !caster.pather.Moving, "caster remains stationary during upload");
            Check(MapComponent_Netrun.For(map).ResultCount(NetrunResult.Uploaded) == before, "hack has not landed early");
            for (int i = 0; i < 130 && MapComponent_Netrun.For(map).ResultCount(NetrunResult.Uploaded) == before; i++) yield return 60;
            Check(MapComponent_Netrun.For(map).ResultCount(NetrunResult.Uploaded) == before + 1, "uninterrupted upload lands exactly once");
            yield return 120;
            Check(!MapComponent_Netrun.For(map).IsUploading(hack, target), "completed upload leaves no tracked progress");
        }

        [LiveScenario("cycle2-upload")]
        private IEnumerable<int> Scenario_C2_DamageCancelsWithoutGhostCast()
        {
            var caster = Cycle2Runner(Center);
            var target = Standing(Center + new IntVec3(10, 0, 0));
            var hack = QuickhackDefOf.RPC_Quickhack_ShortCircuit;
            int uploaded = MapComponent_Netrun.For(map).ResultCount(NetrunResult.Uploaded);
            NetrunUtility.QueueUpload(caster, hack, target);
            yield return 30;
            Check(Verb_Quickhack.IsUploading(caster, out _), "upload active before damage");
            float ram = caster.GetCyberTracker().Ram;
            caster.TakeDamage(new DamageInfo(DamageDefOf.Cut, 1f, 999f, -1f, target, caster.RaceProps.body.corePart));
            Check(!Verb_Quickhack.IsUploading(caster, out _), "real health damage immediately cancels upload");
            Check(Mathf.Approximately(caster.GetCyberTracker().Ram, ram), "cancelled upload spends no RAM");
            yield return 10;
            Check(!MapComponent_Netrun.For(map).IsUploading(hack, target), "cancelled upload disappears from tracking");
            yield return 7300;
            Check(MapComponent_Netrun.For(map).ResultCount(NetrunResult.Uploaded) == uploaded, "no ghost cast after original completion time");
        }

        [LiveScenario("cycle2-upload")]
        private IEnumerable<int> Scenario_C2_MovementAndTargetLossCancel()
        {
            var caster = Cycle2Runner(Center);
            var target = Standing(Center + new IntVec3(10, 0, 0));
            var hack = QuickhackDefOf.RPC_Quickhack_ShortCircuit;
            NetrunUtility.QueueUpload(caster, hack, target);
            yield return 20;
            Check(Verb_Quickhack.IsUploading(caster, out _), "upload starts before movement order");
            caster.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, Center + new IntVec3(-4, 0, 0)));
            yield return 120;
            Check(!Verb_Quickhack.IsUploading(caster, out _), "movement order abandons upload");
            caster.abilities.GetAbility(hack).ResetCooldown();
            NetrunUtility.QueueUpload(caster, hack, target);
            yield return 30;
            Check(Verb_Quickhack.IsUploading(caster, out _), "new upload starts after cancelling/resetting cooldown");
            target.DeSpawn();
            yield return 15;
            Check(!Verb_Quickhack.IsUploading(caster, out _), "despawning target cancels stance");
            Check(!MapComponent_Netrun.For(map).IsUploading(hack, target), "target loss clears tracked upload");
        }

        [LiveScenario("cycle2-features")]
        private IEnumerable<int> Scenario_C2_AllFeatureCombinationsAndSerialization()
        {
            var flags = WorldFeatures.Current;
            var caster = Cycle2Runner(Center);
            var target = Standing(Center + new IntVec3(10, 0, 0));
            var ability = caster.abilities.GetAbility(QuickhackDefOf.RPC_Quickhack_ShortCircuit) as QuickhackAbility;
            string path = Path.Combine(Path.GetDirectoryName(Diag.FilePath), "cycle2-flags.xml");
            bool defaultGigs = RimPunkMod.Settings.newWorldGigs, defaultHacks = RimPunkMod.Settings.newWorldQuickhacks;
            try
            {
                foreach (bool gigs in new[] { false, true }) foreach (bool hacks in new[] { false, true })
                {
                    flags.SetForTests(gigs, hacks);
                    RimPunkMod.Settings.newWorldGigs = !gigs;
                    RimPunkMod.Settings.newWorldQuickhacks = !hacks;
                    Check(WorldFeatures.Gigs == gigs && WorldFeatures.Quickhacks == hacks, $"{gigs}/{hacks}: global defaults cannot change world choices");
                    int count = GameComponent_Gigs.Instance.Gigs.Count;
                    var offer = GameComponent_Gigs.Instance.TryAddOffer();
                    Check(gigs ? offer != null : offer == null && GameComponent_Gigs.Instance.Gigs.Count == count, $"{gigs}/{hacks}: gig creation gate");
                    Check(DefDatabase<MainButtonDef>.GetNamed("RPC_Gigs").Worker.Visible == gigs, $"{gigs}/{hacks}: gig tab visibility");
                    Check((ability.BlockReason() == null) == hacks, $"{gigs}/{hacks}: retained ability obeys world gate");
                    if (!hacks) Check(!ability.Activate(target, target), "disabled quickhack cannot activate via retained reference");
                    Scribe.saver.InitSaving(path, "features");
                    flags.ExposeData();
                    Scribe.saver.FinalizeSaving();
                    var restored = new WorldComponent_RimPunkFeatures(Find.World);
                    Scribe.loader.InitLoading(path);
                    restored.ExposeData();
                    Scribe.loader.FinalizeLoading();
                    Check(restored.Gigs == gigs && restored.Quickhacks == hacks, $"{gigs}/{hacks}: actual Scribe round trip");
                }
                File.WriteAllText(path, "<?xml version=\"1.0\"?><features />");
                var legacy = new WorldComponent_RimPunkFeatures(Find.World);
                Scribe.loader.InitLoading(path);
                legacy.ExposeData();
                Scribe.loader.FinalizeLoading();
                Check(legacy.Gigs && legacy.Quickhacks, "legacy world without flags retains both existing features");
            }
            finally
            {
                Scribe.ForceStop();
                flags.SetForTests(true, true);
                RimPunkMod.Settings.newWorldGigs = defaultGigs;
                RimPunkMod.Settings.newWorldQuickhacks = defaultHacks;
                if (File.Exists(path)) File.Delete(path);
            }
            yield break;
        }

        [LiveScenario("cycle2-ai")]
        private IEnumerable<int> Scenario_C2_RangeRolesAndAntiNetrun()
        {
            var shooter = Hostile(Center, Thing("Gun_AssaultRifle"));
            RimPunkDebugActions.DebugInstall(shooter, Hediff("RPC_SmartLink"), CyberTier.T5);
            var target = Colonist(Center + new IntVec3(18, 0, 0));
            target.drafter.Drafted = true;
            shooter.mindState.enemyTarget = target;
            Check(ContextualCombat.RoleOf(shooter) == CombatRole.Ranged, "ranged cyberware user classified correctly");
            var result = ContextualCombat.AdjustFightJob(shooter, JobMaker.MakeJob(JobDefOf.AttackMelee, target));
            Check(result?.def != JobDefOf.AttackMelee, "valid rifle position cannot be overridden by melee-closing job");
            var corp = Find.FactionManager.AllFactionsListForReading.First(f => f.Ext()?.kind == CyberFactionKind.Corporation);
            var gang = Find.FactionManager.AllFactionsListForReading.First(f => f.Ext()?.kind == CyberFactionKind.Gang);
            shooter.SetFaction(corp);
            float corpRange = ContextualCombat.PreferredRange(shooter);
            shooter.SetFaction(gang);
            Check(corpRange > ContextualCombat.PreferredRange(shooter), "corporations maintain longer range than gangs with the same gun");
            shooter.SetFaction(enemy);
            var runner = Cycle2Runner(Center + new IntVec3(12, 0, 0));
            var hack = runner.abilities.GetAbility(QuickhackDefOf.RPC_Quickhack_ShortCircuit);
            NetrunUtility.QueueUpload(runner, hack.def, shooter);
            yield return 20;
            Check(ContextualCombat.IncomingNetrunner(shooter) == runner, "incoming upload identifies actual attacker");
            shooter.mindState.enemyTarget = target;
            ContextualCombat.AdjustFightJob(shooter, null);
            Check(shooter.mindState.enemyTarget == runner, "AI focuses uploader so it can interrupt them");
            WorldFeatures.Current.SetForTests(true, false);
            Check(ContextualCombat.IncomingNetrunner(shooter) == null, "disabled quickhacks remove anti-netrun action path");
            WorldFeatures.Current.SetForTests(true, true);
        }

        [LiveScenario("cycle2-ai")]
        private IEnumerable<int> Scenario_C2_TargetChromeAndCover()
        {
            var runner = Cycle2Runner(Center);
            var plain = Standing(Center + new IntVec3(15, 0, -2));
            var chrome = Standing(Center + new IntVec3(15, 0, 2));
            RimPunkDebugActions.DebugInstall(chrome, Hediff("RPC_Chitin"), CyberTier.T5);
            Check(ContextualCombat.HackTargetScore(chrome, runner) > ContextualCombat.HackTargetScore(plain, runner), "equal-distance chromed target has higher hack value");
            var cell = Center + new IntVec3(0, 0, -10);
            float bare = ContextualCombat.CoverScore(cell, map);
            var sandbag = ThingMaker.MakeThing(Thing("Sandbags"), ThingDefOf.Steel);
            GenSpawn.Spawn(sandbag, cell + IntVec3.North, map);
            Check(ContextualCombat.CoverScore(cell, map) > bare, "position scoring recognizes actual adjacent cover");
            yield break;
        }

        [LiveScenario("cycle2-psychosis")]
        private IEnumerable<int> Scenario_C2_VolumeEventsAndNoPassiveDecay()
        {
            var light = Colonist(Center);
            var heavy = Colonist(Center + new IntVec3(4, 0, 0));
            RimPunkDebugActions.DebugInstall(light, Hediff("RPC_GorillaArms"), CyberTier.T1);
            RimPunkDebugActions.DebugInstall(heavy, Hediff("RPC_GorillaArms"), CyberTier.T5);
            RimPunkDebugActions.DebugInstall(heavy, Hediff("RPC_Chitin"), CyberTier.T5);
            var lt = light.GetCyberTracker(); var ht = heavy.GetCyberTracker();
            lt.instability = ht.instability = 20f;
            CyberpsychosisUtility.TickInstability(light); CyberpsychosisUtility.TickInstability(heavy);
            Check(ht.instability > lt.instability && lt.instability >= 20f, "chrome volume/tier increases baseline accumulation");
            light.drafter.Drafted = heavy.drafter.Drafted = true;
            float idle = ht.instability;
            yield return 180;
            Check(ht.instability >= idle, "idle time does not decay instability");
            float before = ht.instability;
            CyberEvents.Notify_DamageTaken(heavy, new DamageInfo(DamageDefOf.Cut, 1f), 1f);
            Check(ht.instability > before, "damage pipeline causes spike");
            before = ht.instability;
            CyberEvents.Notify_Neutralized(heavy, light, true);
            Check(ht.instability > before, "kill pipeline causes spike");
            RimPunkDebugActions.DebugInstall(heavy, Hediff("RPC_DynalarSandevistan"), CyberTier.T5);
            ht.SetAdrenaline(100f);
            before = ht.instability;
            var sandy = AbilityFrom(heavy, CyberBuffAiRule.CloseDistance);
            Check(sandy != null && sandy.Activate(heavy, heavy), "sandevistan activates through real ability");
            Check(ht.instability > before, "sandevistan causes instability spike");
        }

        [LiveScenario("cycle2-psychosis")]
        private IEnumerable<int> Scenario_C2_RecoveryThroughGameplayActions()
        {
            var p = Colonist(Center);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_Chitin"), CyberTier.T5);
            var tracker = p.GetCyberTracker(); tracker.instability = 60f;
            p.needs.joy.CurLevel = 0.1f;
            p.needs.joy.GainJoy(0.2f, DefDatabase<JoyKindDef>.AllDefsListForReading.First());
            Check(tracker.instability < 60f, "actual recreation gain reduces instability");
            tracker.instability = 60f;
            var drug = ThingMaker.MakeThing(Thing("RPC_Immunoblocker_Basewatch"));
            GenSpawn.Spawn(drug, p.Position, map);
            drug.Ingested(p, 0f);
            Check(tracker.instability < 60f, "actual drug ingestion reduces instability");
            tracker.instability = 60f;
            var injury = (Hediff_Injury)p.health.AddHediff(HediffDefOf.Cut, p.RaceProps.body.corePart);
            injury.Severity = 2f;
            injury.Tended(1f, 1f, 0);
            Check(tracker.instability < 60f, "actual medical tending reduces instability");
            tracker.instability = 60f;
            p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, p.Position), JobCondition.InterruptForced);
            yield return 5;
            p.jobs.curDriver.asleep = true;
            p.needs.rest.TickResting(1f);
            Check(tracker.instability < 60f, "actual sleeping/rest event reduces instability");
            p.jobs.EndCurrentJob(JobCondition.InterruptForced);
            tracker.instability = 0.1f;
            CyberpsychosisUtility.RecoverInstability(p, InstabilityRecovery.MedicalCare, 100f);
            Check(tracker.instability == 0f, "recovery clamps at zero");
        }

        [LiveScenario("cycle2-psychosis")]
        private IEnumerable<int> Scenario_C2_TemporaryBreakAndPermanentEligibility()
        {
            var p = Colonist(Center);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_GorillaArms"), CyberTier.T1);
            Check(!CyberpsychosisUtility.PermanentEligible(p), "light chrome cannot become permanently rogue");
            CyberpsychosisUtility.AddInstability(p, 100f);
            Check(CyberpsychosisUtility.InCyberpsychosis(p) && !CyberpsychosisUtility.IsPermanent(p), "light chrome has a temporary break at instability 100");
            if (p.MentalState is MentalState_Cyberpsychosis ms) ms.episodeTicks = 1;
            yield return 35;
            Check(!CyberpsychosisUtility.InCyberpsychosis(p), "temporary break times out without chrome removal");
            Check(p.GetCyberTracker().instability >= 99f, "ending a temporary break does not passively erase instability");
            Check(p.GetCyberTracker().nextPsychosisTick > Find.TickManager.TicksGame, "relapse grace prevents immediate repeated breaks");
            RimPunkDebugActions_Scenarios.MaxCyberware(p);
            Check(CyberpsychosisUtility.PermanentEligible(p), "heavy high-tier chrome is eligible for rare permanent rogue outcome");
            var tuning = RPDefOf.RPC_CyberpsychosisTuning;
            float chance = tuning.permanentChance;
            try
            {
                tuning.permanentChance = 1f; // Force the rare branch; production probability remains 3%.
                p.GetCyberTracker().nextPsychosisTick = 0;
                CyberpsychosisUtility.AddInstability(p, 1f);
                Check(CyberpsychosisUtility.IsPermanent(p), "eligible permanent outcome uses the same event pipeline");
                CyberpsychosisUtility.RecoverInstability(p, InstabilityRecovery.MedicalCare, 100f);
                yield return 90;
                Check(CyberpsychosisUtility.InCyberpsychosis(p), "permanent rogue survives recovery actions");
            }
            finally { tuning.permanentChance = chance; }
        }

        [LiveScenario("cycle2-trauma")]
        private IEnumerable<int> Scenario_C2_AdrenalinePatientStandsBeforeExtraction()
        {
            var inst = GameComponent_RimPunk.Instance;
            inst.ClearDebt();
            var p = Colonist(Center);
            RimPunkDebugActions_Testing.GiveCoverage(p);
            HealthUtility.AdjustSeverity(p, HediffDefOf.BloodLoss, 0.75f);
            p.GetCyberTracker().SetAdrenaline(100f);
            yield return 30;
            Check(p.Downed, "covered patient starts downed with high adrenaline");
            Check(!p.health.hediffSet.HasHediff(RPDefOf.RPC_AdrenalineSurge), "downed patient does not gain pain-suppressing adrenaline surge");
            for (int i = 0; i < 80 && !map.mapPawns.AllPawnsSpawned.Any(CyberpsychosisUtility.IsTraumaTeam); i++) yield return 30;
            Check(map.mapPawns.AllPawnsSpawned.Any(CyberpsychosisUtility.IsTraumaTeam), "Trauma Team arrives");
            var blood = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (blood != null) p.health.RemoveHediff(blood);
            yield return 20;
            Check(!p.Downed, "patient stands up while rescue is active");
            for (int i = 0; i < 350 && !GameComponent_RimPunk.IsAwayWithTraumaTeam(p); i++) yield return 30;
            Check(GameComponent_RimPunk.IsAwayWithTraumaTeam(p), "standing patient was carried aboard and extracted");
            if (!p.Spawned) spawned.Remove(p);
            inst.ForceReturnsNow();
            for (int i = 0; i < 80 && !p.Spawned; i++) yield return 30;
            Check(p.Spawned, "standing patient follows the normal treatment/return path");
            if (p.Spawned) spawned.Add(p);
            inst.ClearDebt();
        }
    }
}
