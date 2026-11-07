using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>The systems from groundwork.md, one behaviour at a time, driven through the same entry points as play.</summary>
    public partial class GameComponent_RimPunkLiveTests
    {
        private static RecipeDef RecipeWhere(System.Func<RecipeDef, bool> f) => DefDatabase<RecipeDef>.AllDefsListForReading.FirstOrDefault(f);

        // ------------------------------------------------------------------------------------ tiers + surgery

        [LiveScenario("systems")]
        private IEnumerable<int> Scenario_SurgeryLifecycle()
        {
            var patient = Colonist(Center);
            var def = Hediff("RPC_GorillaArms");
            var install = RecipeWhere(r => r.addsHediff == def && r.Worker is Recipe_InstallCyberware);
            Check(install != null, "install recipe exists");
            var item = ThingMaker.MakeThing(def.Ext().item);
            item.TryGetComp<CompCyberwareTier>().SetTier(CyberTier.T3);
            var part = install.Worker.GetPartsToApplyOn(patient, install).First();
            install.Worker.ApplyOnPawn(patient, part, null, new List<Thing> { item }, null);
            var c = patient.InstalledCyberware().FirstOrDefault(x => x.def == def);
            Check(c?.tier == CyberTier.T3, $"installed with the item's tier: {c?.tier}");

            var up = DefDatabase<RecipeDef>.AllDefsListForReading.FirstOrDefault(r => r.GetModExtension<UpgradeRecipeExtension>() is UpgradeRecipeExtension x && x.cyberware == def && x.fromTier == CyberTier.T3);
            Check(up != null, "upgrade recipe exists");
            up?.Worker.ApplyOnPawn(patient, part, null, new List<Thing>(), null);
            Check(c?.tier == CyberTier.T4, $"upgrade raised the tier: {c?.tier}");

            var remove = RecipeWhere(r => r.removesHediff == def && r.Worker is Recipe_RemoveCyberware);
            Check(remove != null, "remove recipe exists");
            remove?.Worker.ApplyOnPawn(patient, part, null, new List<Thing>(), null);
            Check(!patient.InstalledCyberware().Any(x => x.def == def), "removed");
            var dropped = map.listerThings.ThingsOfDef(def.Ext().item).FirstOrDefault(t => t.Position.InHorDistOf(patient.Position, 5f));
            Check(dropped != null, "item dropped");
            Check(dropped?.TryGetComp<CompCyberwareTier>()?.tier == CyberTier.T4, $"dropped item kept its tier: {dropped?.TryGetComp<CompCyberwareTier>()?.tier}");
            dropped?.Destroy();

            // replacing a limb drops what's on it
            RimPunkDebugActions.DebugInstall(patient, def, CyberTier.T2);
            var armImplant = patient.InstalledCyberware().First(x => x.def == def);
            MedicalRecipesUtility.RestorePartAndSpawnAllPreviousParts(patient, armImplant.Part, patient.Position, map);
            Check(!patient.InstalledCyberware().Any(x => x.def == def), "limb restored: implant off");
            Check(map.listerThings.ThingsOfDef(def.Ext().item).Any(t => t.Position.InHorDistOf(patient.Position, 5f)), "and dropped as an item, not deleted");
            yield break;
        }

        [LiveScenario("systems")]
        private IEnumerable<int> Scenario_SlotRules()
        {
            var p = Colonist(Center);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_DynalarSandevistan"), CyberTier.T2);
            Check(!CyberUtility.CanInstall(p, Hediff("RPC_MooreTechBerserk"), null, out _), "one operating system only");

            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_GorillaArms"), CyberTier.T2);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_MantisBlades"), CyberTier.T2);
            Check(p.InstalledCyberware().Count(c => c.Ext.slot.defName == "RPC_Slot_Arm") == 2, "two different arms mix");
            Check(!CyberSurgeryUtility.CandidateParts(p, Hediff("RPC_Monowire").Ext().slot).Any(x => CyberUtility.CanInstall(p, Hediff("RPC_Monowire"), x, out _)), "no third arm");

            var pfc = CyberwareDefCache.AllCyberware.Where(d => d.Ext().slot.defName == "RPC_Slot_PrefrontalCortex").ToList();
            Check(pfc.Count >= 4, $"{pfc.Count} cortex implants to test the limit with");
            int added = 0;
            foreach (var d in pfc)
                if (CyberUtility.CanInstall(p, d, null, out _) || CyberSurgeryUtility.CandidateParts(p, d.Ext().slot).Any(x => CyberUtility.CanInstall(p, d, x, out _)))
                {
                    RimPunkDebugActions.DebugInstall(p, d, d.Ext().minTier);
                    added++;
                }
            Check(added == 3, $"cortex stops at 3: {added}");

            // legs must match, and a matching pair doubles
            var legDefs = CyberwareDefCache.AllCyberware.Where(d => d.Ext().slot.defName == "RPC_Slot_Legs").ToList();
            Check(legDefs.Count >= 2, "leg cyberware exists");
            var legs = legDefs[0];
            RimPunkDebugActions.DebugInstall(p, legs, legs.Ext().minTier);
            var otherPart = CyberSurgeryUtility.CandidateParts(p, legs.Ext().slot).FirstOrDefault(x => !p.InstalledCyberware().Any(c => c.Part == x));
            Check(otherPart != null && !CyberUtility.CanInstall(p, legDefs[1], otherPart, out _), "mismatched leg refused");
            Check(!p.InstalledCyberware().First(c => c.def == legs).effectsDoubled, "one leg isn't doubled");
            RimPunkDebugActions.DebugInstall(p, legs, legs.Ext().minTier);
            Check(p.InstalledCyberware().Where(c => c.def == legs).All(c => c.effectsDoubled), "a matching pair is doubled");
            yield break;
        }

        // ------------------------------------------------------------------------------------ capacity + traits

        [LiveScenario("systems")]
        private IEnumerable<int> Scenario_CapacityAndAptitude()
        {
            var p = Colonist(Center);
            var t = p.GetCyberTracker();
            Check(p.story.traits.HasTrait(RPDefOf.RPC_CyberAptitude), "every humanlike has a capacity trait");
            Check(t.BaseCapacity >= CompCyberTracker.BaseCapacityMin && t.BaseCapacity <= CompCyberTracker.BaseCapacityMax, $"base capacity in range: {t.BaseCapacity}");

            void SetAptitude(int degree)
            {
                var old = p.story.traits.GetTrait(RPDefOf.RPC_CyberAptitude);
                if (old != null) p.story.traits.RemoveTrait(old);
                p.story.traits.GainTrait(new Trait(RPDefOf.RPC_CyberAptitude, degree));
                p.health.capacities.Notify_CapacityLevelsDirty();
            }
            SetAptitude(AptitudeDegree.Edgerunner);
            float normal = t.CapacityMax;
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_ChromeCompressor"), CyberTier.Iconic);
            yield return 2;
            float compressed = t.CapacityMax;
            Check(compressed > normal, $"chrome compressor raises capacity: {normal} -> {compressed}");
            SetAptitude(AptitudeDegree.Gonk);
            yield return 2;
            Check(Mathf.Approximately(t.CapacityMax, 20f), $"gonk is 20, compressor or not: {t.CapacityMax}");

            // immunoblockers are the one thing that lifts a gonk
            p.health.AddHediff(Hediff("RPC_ImmunoblockerHigh_Surge"));
            yield return 2;
            Check(t.CapacityMax > 20f, $"immunoblocker lifts a gonk: {t.CapacityMax}");

            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_GorillaArms"), CyberTier.T2);
            Check(t.CapacityUsed >= Hediff("RPC_GorillaArms").Ext().capacityCost, $"used capacity counts it: {t.CapacityUsed}");
            string inspect = t.CompInspectStringExtra();
            Check(!inspect.NullOrEmpty(), "inspect line: " + inspect);

            // other mods' implants count (vanilla bionic arm stands in)
            var bionic = HediffDef.Named("BionicArm");
            Check(ForeignCyberwareUtility.IsForeignImplantDef(bionic), "a vanilla bionic counts as foreign chrome");
            Check(ForeignCyberwareUtility.CostOfDef(bionic, out _) > 0f, "with a capacity cost");
        }

        [LiveScenario("systems")]
        private IEnumerable<int> Scenario_ImmunoblockersEveryDose()
        {
            string[] names = { "Basewatch", "Bethaloperidol", "PrimeTime", "Surge", "BlackLace", "DavidsLastStand" };
            var bad = new List<string>();
            foreach (string name in names)
            {
                var item = DefDatabase<ThingDef>.GetNamedSilentFail("RPC_Immunoblocker_" + name);
                var high = Hediff("RPC_ImmunoblockerHigh_" + name);
                if (item == null)
                {
                    bad.Add(name + ": no drug item");
                    continue;
                }
                var p = Colonist(Center);
                var dose = ThingMaker.MakeThing(item);
                GenSpawn.Spawn(dose, p.Position, map);
                dose.Ingested(p, 0f);
                yield return 2;
                float bonus = ImmunoblockerUtility.CapacityBonus(p);
                if (!p.health.hediffSet.HasHediff(high)) bad.Add(name + ": no effect after taking it");
                else if (bonus <= 0f) bad.Add(name + ": no capacity bonus");
                Cleanup(p);
            }
            Check(bad.Count == 0, $"{names.Length} drugs give capacity when taken" + (bad.Count > 0 ? ". Broken: " + string.Join("; ", bad) : ""));

            // strongest only, no stacking
            var q = Colonist(Center);
            q.health.AddHediff(Hediff("RPC_ImmunoblockerHigh_Basewatch"));
            q.health.AddHediff(Hediff("RPC_ImmunoblockerHigh_BlackLace"));
            float both = ImmunoblockerUtility.CapacityBonus(q);
            Check(Mathf.Approximately(both, ((Hediff_Immunoblocker)q.health.hediffSet.GetFirstHediffOfDef(Hediff("RPC_ImmunoblockerHigh_BlackLace"))).CapacityBonus), $"doses don't stack: {both}");

            // burnout + a blocker wearing off = instant episode
            var b = Colonist(Center + new IntVec3(4, 0, 0));
            MakeGonk(b);
            RimPunkDebugActions.DebugInstall(b, Hediff("RPC_DynalarSandevistan"), CyberTier.T2);
            b.health.AddHediff(Hediff("RPC_ImmunoblockerHigh_BlackLace"));
            b.health.AddHediff(RPDefOf.RPC_CyberpsychosisBurnout);
            Check(!CyberpsychosisUtility.InCyberpsychosis(b), "held off while the blocker runs");
            b.health.RemoveHediff(b.health.hediffSet.GetFirstHediffOfDef(Hediff("RPC_ImmunoblockerHigh_BlackLace")));
            yield return 2;
            Check(CyberpsychosisUtility.InCyberpsychosis(b), $"blocker off with burnout over 50%: episode ({b.GetCyberTracker().UsageRatio:P0})");

            // David's last stand: permanent when it ends
            var d = Colonist(Center + new IntVec3(-4, 0, 0));
            d.health.AddHediff(Hediff("RPC_ImmunoblockerHigh_DavidsLastStand"));
            d.health.RemoveHediff(d.health.hediffSet.GetFirstHediffOfDef(Hediff("RPC_ImmunoblockerHigh_DavidsLastStand")));
            yield return 2;
            Check(CyberpsychosisUtility.IsPermanent(d), "last stand ends in permanent cyberpsychosis");
            Check(CyberpsychosisUtility.InCyberpsychosis(d), "and the episode starts");
        }

        // ------------------------------------------------------------------------------------ cyberpsychosis

        [LiveScenario("systems")]
        private IEnumerable<int> Scenario_CyberpsychosisEpisode()
        {
            var p = Colonist(Center);
            var t = p.GetCyberTracker();
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_GorillaArms"), CyberTier.T5);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_DynalarSandevistan"), CyberTier.T5);
            MakeGonk(p);
            Check(t.UsageRatio > 0.8f, $"over the threshold: {t.UsageRatio:P0}");
            Check(CyberpsychosisUtility.TryStart(p, "live test"), "episode started");
            yield return 2;
            Check(p.MentalStateDef == RPDefOf.RPC_Cyberpsychosis, "in the mental state");
            Check(p.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisRage), "rage hediff on");

            var victim = Standing(Center + new IntVec3(6, 0, 0));
            bool fought = false;
            for (int i = 0; i < 40 && !fought; i++)
            {
                yield return 10;
                fought = p.CurJobDef == JobDefOf.AttackMelee || p.CurJobDef == JobDefOf.Wait_Combat || p.CurJob?.ability != null;
            }
            Check(fought, $"the cyberpsycho goes for people (job {p.CurJobDef?.defName})");

            // chrome off below 50%: it ends, and leaves burnout
            foreach (var c in p.InstalledCyberware().ToList()) p.health.RemoveHediff(c);
            for (int i = 0; i < 40 && CyberpsychosisUtility.InCyberpsychosis(p); i++) yield return 15;
            Check(!CyberpsychosisUtility.InCyberpsychosis(p), $"ended once under 50% ({t.UsageRatio:P0})");
            Check(CyberpsychosisUtility.HasBurnout(p), "burnout left behind");
            Check(!p.health.hediffSet.HasHediff(RPDefOf.RPC_CyberpsychosisRage), "rage gone");

            // from now on, over 50% is instant
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_DynalarSandevistan"), CyberTier.T5);
            yield return 2;
            Check(CyberpsychosisUtility.InCyberpsychosis(p), $"with burnout, going over 50% starts an episode ({t.UsageRatio:P0})");

            // who can't get it
            var deathless = Colonist(Center + new IntVec3(-8, 0, 0));
            if (RPDefOf.Deathless != null)
            {
                deathless.genes?.AddGene(RPDefOf.Deathless, true);
                Check(CyberpsychosisUtility.IsImmune(deathless), "deathless gene is immune");
                Check(!CyberpsychosisUtility.TryStart(deathless, "live test"), "and doesn't start");
            }
            else Note("Biotech not loaded: deathless not tested");
        }

        [LiveScenario("factions")]
        private IEnumerable<int> Scenario_MaxTacPutsDownPsycho()
        {
            var inst = GameComponent_RimPunk.Instance;
            var psycho = Colonist(Center);
            RimPunkDebugActions.DebugInstall(psycho, Hediff("RPC_GorillaArms"), CyberTier.T5);
            RimPunkDebugActions.DebugInstall(psycho, Hediff("RPC_DynalarSandevistan"), CyberTier.T5);
            MakeGonk(psycho);
            CyberpsychosisUtility.TryStart(psycho, "live test");
            yield return 2;
            Check(CyberpsychosisUtility.InCyberpsychosis(psycho), "cyberpsycho ready");
            bool hasKind = RPDefOf.RPC_CyberpsychosisTuning.maxTacOperatorKind != null;
            Check(hasKind, "operator kind set");
            inst.CallMaxTac(psycho, "live test");
            Check(inst.MaxTacComingFor(psycho), "dispatch on the way");
            Pawn officer = null;
            for (int i = 0; i < 60 && officer == null; i++)
            {
                yield return 30;
                officer = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => CyberpsychosisUtility.IsMaxTac(p));
            }
            Check(officer != null, "officers landed");
            if (officer == null) yield break;
            Check(CyberpsychosisUtility.IsImmune(officer), "MaxTac are immune");
            for (int i = 0; i < 80 && !psycho.Dead; i++) yield return 30;
            Check(psycho.Dead || psycho.Downed, $"target put down (dead {psycho.Dead}, downed {psycho.Downed})");
            for (int i = 0; i < 80 && map.mapPawns.AllPawnsSpawned.Any(p => CyberpsychosisUtility.IsMaxTac(p)); i++) yield return 30;
            Check(!map.mapPawns.AllPawnsSpawned.Any(p => CyberpsychosisUtility.IsMaxTac(p)), "and MaxTac left");
        }

        // ------------------------------------------------------------------------------------ trauma team

        [LiveScenario("factions")]
        private IEnumerable<int> Scenario_TraumaTeamExtractsAndBills()
        {
            var inst = GameComponent_RimPunk.Instance;
            inst.ClearDebt();
            var patient = Colonist(Center);
            RimPunkDebugActions_Testing.GiveCoverage(patient);
            Check(GameComponent_RimPunk.IsCovered(patient), "covered");
            // real blood loss: vanilla's DamageUntilDowned only forces the state, and the patient can stand back up
            HealthUtility.AdjustSeverity(patient, HediffDefOf.BloodLoss, 0.75f);
            yield return 5;
            Check(patient.Downed, "downed");

            bool Medics() => map.mapPawns.AllPawnsSpawned.Any(p => CyberpsychosisUtility.IsTraumaTeam(p));
            for (int i = 0; i < 60 && !Medics(); i++) yield return 30;
            Check(Medics(), "squad landed");
            for (int i = 0; i < 300 && patient.Spawned; i++) yield return 30;
            Check(!patient.Spawned, $"patient extracted (spawned {patient.Spawned}, downed {patient.Downed})");
            if (patient.Spawned) yield break;
            spawned.Remove(patient);

            for (int i = 0; i < 120 && Medics(); i++) yield return 30;
            Check(!Medics(), "squad left");
            Check(GameComponent_RimPunk.IsAwayWithTraumaTeam(patient), "patient counted as away with the team");

            inst.ForceReturnsNow();
            for (int i = 0; i < 60 && !patient.Spawned; i++) yield return 30;
            Check(patient.Spawned, "patient back");
            if (patient.Spawned)
            {
                spawned.Add(patient);
                Check(!patient.Downed, $"healed (injuries {patient.health.hediffSet.hediffs.OfType<Hediff_Injury>().Count()})");
            }
            int silver = map.listerThings.ThingsOfDef(ThingDefOf.Silver).Sum(t => t.stackCount);
            Check(GameComponent_RimPunk.PlayerInDebt || silver > 0, $"bill landed: debt {inst.TraumaDebt:0}, silver on map {silver}");
            inst.ClearDebt();
        }

        [LiveScenario("factions")]
        private IEnumerable<int> Scenario_TraumaDebtAndCollectors()
        {
            var inst = GameComponent_RimPunk.Instance;
            inst.ClearDebt();
            Check(!GameComponent_RimPunk.PlayerInDebt, "starts clear");
            var bill = TraumaBill.For(GameComponent_RimPunk.Tuning, 4, 2, true);
            Check(bill.Total > 0, $"bill adds up: {bill.Total}");
            inst.AddDebt(1000f);
            Check(GameComponent_RimPunk.PlayerInDebt, "in debt");
            float d0 = inst.TraumaDebt;
            inst.Debug_DailyTick();
            Check(inst.TraumaDebt > d0, $"interest: {d0} -> {inst.TraumaDebt}");
            var p = Colonist(Center);
            RimPunkDebugActions_Testing.GiveCoverage(p);
            var gizmos = p.GetGizmos().ToList();
            Check(gizmos.Count > 0, "gizmos draw while in debt");

            var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = 500;
            GenSpawn.Spawn(silver, Center + new IntVec3(2, 0, 0), map);
            d0 = inst.TraumaDebt;
            GameComponent_RimPunk.PayDebt(map);
            Check(inst.TraumaDebt < d0, $"paying reduces it: {d0} -> {inst.TraumaDebt}");

            var collectors = GameComponent_RimPunk.Collectors;
            Check(collectors != null, "collector faction exists");
            bool sent = inst.SendCollectors(GameComponent_RimPunk.Tuning);
            Check(sent, "collectors sent");
            for (int i = 0; i < 40 && !map.mapPawns.AllPawnsSpawned.Any(x => x.Faction == collectors); i++) yield return 30;
            var raid = map.mapPawns.AllPawnsSpawned.Where(x => x.Faction == collectors).ToList();
            Check(raid.Count > 0 || !sent, $"collectors on the map: {raid.Count}");
            spawned.AddRange(raid);
            inst.ClearDebt();
            Check(!GameComponent_RimPunk.PlayerInDebt, "cleared");
            yield return 2;
        }

        // ------------------------------------------------------------------------------------ factions + events

        [LiveScenario("factions")]
        private IEnumerable<int> Scenario_RivalriesAndRetaliation()
        {
            var corps = Find.FactionManager.AllFactionsListForReading.Where(f => f.IsCorporation() && !f.defeated).ToList();
            Check(corps.Count > 0, $"{corps.Count} corporations in this world");
            var withRival = corps.FirstOrDefault(c => corps.Any(r => r != c && c.Ext().rivals.Contains(r.def)));
            if (withRival != null)
            {
                var rival = corps.First(r => r != withRival && withRival.Ext().rivals.Contains(r.def));
                Check(withRival.HostileTo(rival), $"{withRival.Name} and {rival.Name} are hostile");
                CyberFactionUtility.ForceRelation(withRival, Faction.OfPlayer, FactionRelationKind.Neutral);
                CyberFactionUtility.ForceRelation(rival, Faction.OfPlayer, FactionRelationKind.Neutral);
                int g0 = rival.PlayerGoodwill;
                withRival.TryAffectGoodwillWith(Faction.OfPlayer, 10, canSendMessage: false, canSendHostilityLetter: false);
                Check(rival.PlayerGoodwill < g0, $"helping one angers its rival: {g0} -> {rival.PlayerGoodwill}");
                CyberFactionUtility.ForceRelation(withRival, Faction.OfPlayer, FactionRelationKind.Hostile);
                CyberFactionUtility.ForceRelation(rival, Faction.OfPlayer, FactionRelationKind.Hostile);
            }
            else Note("no corp with a rival in this world");

            int queued = Find.Storyteller.incidentQueue.Count;
            Patches.Patch_Faction_TryAffectGoodwillWith.QueueRetaliation(corps[0], corps[0].Ext());
            Check(Find.Storyteller.incidentQueue.Count > queued, "retaliation queued");
            yield break;
        }

        [LiveScenario("factions")]
        private IEnumerable<int> Scenario_StreetNamesAndNpcCoverage()
        {
            var corp = Find.FactionManager.AllFactionsListForReading.First(f => f.IsCorporation() && !f.defeated);
            var kind = DefDatabase<PawnKindDef>.AllDefsListForReading.First(k => k.defaultFactionDef == corp.def);
            int nick = 0;
            for (int i = 0; i < 12; i++)
            {
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, corp, forceGenerateNewPawn: true));
                if (p.Name is NameTriple n && DefDatabase<StreetNamesDef>.GetNamed("RPC_StreetNames").names.Contains(n.Nick)) nick++;
                p.Destroy();
            }
            Check(nick > 0, $"street names used: {nick} of 12");
            Check(TraumaCoverageUtility.FactionCanBuyCoverage(corp), "corporations can buy Trauma cover");
            var gang = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.Ext()?.kind == CyberFactionKind.Gang);
            if (gang != null) Check(!TraumaCoverageUtility.FactionCanBuyCoverage(gang), "gangs can't");
            yield break;
        }

        [LiveScenario("factions")]
        private IEnumerable<int> Scenario_NetWatchHunts()
        {
            var inc = DefDatabase<IncidentDef>.AllDefsListForReading.FirstOrDefault(d => d.workerClass == typeof(IncidentWorker_NetWatchHunt));
            Check(inc != null, "incident defined");
            if (inc == null) yield break;
            var parms = StorytellerUtility.DefaultParmsNow(inc.category, map);
            parms.forced = true;
            foreach (var c in map.mapPawns.FreeColonistsSpawned) { if (c.GetCyberTracker() != null) c.GetCyberTracker().lastHighTierQuickhackTick = -9999999; }
            var rule = HarmonyLib.AccessTools.Method(typeof(IncidentWorker_NetWatchHunt), "CanFireNowSub");
            Check(!(bool)rule.Invoke(inc.Worker, new object[] { parms }), "doesn't fire without a recent high-tier hack");
            var runner = Colonist(Center);
            runner.GetCyberTracker().lastHighTierQuickhackTick = Find.TickManager.TicksGame;
            Check((bool)rule.Invoke(inc.Worker, new object[] { parms }), "fires after one");
            var nw = CyberFactionUtility.FirstOfKind(CyberFactionKind.NetWatch);
            Check(nw != null, "NetWatch faction exists");
            bool ok = inc.Worker.TryExecute(parms);
            Check(ok, "raid sent");
            for (int i = 0; i < 40 && !map.mapPawns.AllPawnsSpawned.Any(p => p.Faction == nw); i++) yield return 30; // drop pods take a while
            var raid = map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == nw).ToList();
            Check(raid.Count > 0, $"NetWatch on the map: {raid.Count}");
            spawned.AddRange(raid);
        }

        [LiveScenario("factions")]
        private IEnumerable<int> Scenario_TraderStock()
        {
            var bad = new List<string>();
            var normal = new StockGenerator_Cyberware { minTier = CyberTier.T1, maxTier = CyberTier.T2, maxCapacityCost = 20f, countRange = new IntRange(40, 40), maxTechLevelGenerate = TechLevel.Archotech };
            var exotic = new StockGenerator_Cyberware { minTier = CyberTier.T3, maxTier = CyberTier.Iconic, countRange = new IntRange(40, 40), maxTechLevelGenerate = TechLevel.Archotech };
            var low = normal.GenerateThings(map.Tile).ToList();
            var high = exotic.GenerateThings(map.Tile).ToList();
            Check(low.Count > 0 && high.Count > 0, $"stock generated: {low.Count} normal, {high.Count} exotic");
            foreach (var t in low)
            {
                var tier = t.TryGetComp<CompCyberwareTier>().tier;
                if (tier > CyberTier.T2) bad.Add($"{t.def.defName} {tier} on a normal trader");
                if (CyberwareDefCache.HediffFor(t.def).Ext().capacityCost > 20f) bad.Add($"{t.def.defName}: too big for a normal trader");
            }
            foreach (var t in high)
                if (t.TryGetComp<CompCyberwareTier>().tier < CyberTier.T3) bad.Add($"{t.def.defName} too low for an exotic trader");
            Check(bad.Count == 0, "tiers and capacity respect the trader" + (bad.Count > 0 ? ": " + string.Join("; ", bad.Take(8)) : ""));
            foreach (var t in low.Concat(high)) t.Destroy();

            int patched = DefDatabase<TraderKindDef>.AllDefsListForReading.Count(k => k.stockGenerators.Any(g => g is StockGenerator_Cyberware));
            Check(patched > 0, $"{patched} trader kinds stock cyberware");
            yield break;
        }

        // ------------------------------------------------------------------------------------ arms

        [LiveScenario("arms")]
        private IEnumerable<int> Scenario_MantisBladesAndDash()
        {
            var p = Colonist(Center, Thing("Gun_Autopistol"));
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_MantisBlades"), CyberTier.T4);
            bool Bladed(Verb v) => v?.HediffSource is HediffWithComps h && h.TryGetComp<HediffComp_MantisBlades>() != null;
            yield return 2;
            var verbs = p.meleeVerbs.GetUpdatedAvailableVerbsList(false);
            Check(verbs.All(e => Bladed(e.verb)) && verbs.Count > 0, $"extended: melee only uses the blades ({verbs.Count} verbs), not the gun butt");
            HediffComp_MantisBlades.SetExtended(p, false);
            yield return 2;
            verbs = p.meleeVerbs.GetUpdatedAvailableVerbsList(false);
            Check(!verbs.Any(e => Bladed(e.verb)), "retracted: blades unavailable");
            HediffComp_MantisBlades.SetExtended(p, true);

            var foe = Dummy(Center + new IntVec3(8, 0, 0));
            p.drafter.Drafted = true;
            var dash = p.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("RPC_Ability_MantisDash"));
            Check(dash.CanCast.Accepted, "dash ready");
            p.jobs.StartJob(dash.GetJob(foe, foe), JobCondition.InterruptForced);
            bool landed = false;
            for (int i = 0; i < 40 && !landed; i++)
            {
                yield return 5;
                landed = p.Spawned && p.Position.AdjacentTo8WayOrInside(foe.Position);
            }
            Check(landed, $"dash landed next to the target ({p.PositionHeld.DistanceTo(foe.Position):0.#} away)");
            Check(dash.CooldownTicksRemaining > 120, $"on its tiered cooldown: {dash.CooldownTicksRemaining} ticks");
        }

        [LiveScenario("arms")]
        private IEnumerable<int> Scenario_MonowireLash()
        {
            var p = Colonist(Center);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_Monowire"), CyberTier.T5);
            var foe = Dummy(Center + new IntVec3(5, 0, 0));
            p.drafter.Drafted = true;
            var lash = p.abilities.AllAbilitiesForReading.FirstOrDefault(a => a.def.defName == "RPC_Ability_MonowireLash");
            Check(lash != null, "lash granted");
            if (lash == null) yield break;
            Check(lash.verb.verbProps.range >= 5f, $"reaches 5+ tiles: {lash.verb.verbProps.range}");
            float hp = foe.health.summaryHealth.SummaryHealthPercent;
            p.jobs.StartJob(lash.GetJob(foe, foe), JobCondition.InterruptForced);
            for (int i = 0; i < 30 && foe.health.summaryHealth.SummaryHealthPercent >= hp; i++) yield return 5;
            Check(foe.health.summaryHealth.SummaryHealthPercent < hp, "lash did damage from range");
        }

        [LiveScenario("arms")]
        private IEnumerable<int> Scenario_ProjectileLaunchSystem()
        {
            var p = Colonist(Center);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_ProjectileLaunchSystem"), CyberTier.T3);
            var mag = ArmLauncherUtility.Magazine(p);
            Check(mag != null, "magazine on the arm");
            if (mag == null) yield break;
            Check(mag.Capacity == 4, $"T3 holds 4: {mag.Capacity}");
            ArmLauncherUtility.StockGenerated(p);
            Check(mag.Loaded == mag.Capacity, $"generated loaded: {mag.Loaded}");
            var snap = p.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("RPC_Ability_LauncherSnap"));
            var aimed = p.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("RPC_Ability_LauncherAimed"));
            Check(snap != null && aimed != null, "both fire modes granted");
            var foe = Dummy(Center + new IntVec3(12, 0, 0));
            p.drafter.Drafted = true;
            int loaded = mag.Loaded;
            p.jobs.StartJob(snap.GetJob(foe, foe), JobCondition.InterruptForced);
            for (int i = 0; i < 30 && mag.Loaded >= loaded; i++) yield return 5;
            Check(mag.Loaded < loaded, $"snap shot used a shell: {loaded} -> {mag.Loaded}");
            yield return 90;

            // every shell type, aimed
            for (int s = 0; s < mag.Props.ammo.Count; s++)
            {
                var ammo = mag.Props.ammo[s];
                ArmLauncherUtility.GiveToInventory(p, ammo, 5);
                HediffComp_LauncherAmmo.CycleShell(p, s);
                HediffComp_LauncherAmmo.OrderReload(p);
                for (int i = 0; i < 40 && (mag.loadedIndex != s || mag.Loaded == 0); i++) yield return 15;
                Check(mag.loadedIndex == s && mag.Loaded > 0, $"{ammo.defName} reloads (loaded {mag.Loaded})");
                aimed.ResetCooldown();
                loaded = mag.Loaded;
                Cleanup(foe);
                foe = Standing(Center + new IntVec3(12, 0, 0));
                p.jobs.StartJob(aimed.GetJob(foe, foe), JobCondition.InterruptForced);
                for (int i = 0; i < 40 && mag.Loaded >= loaded; i++) yield return 5;
                Check(mag.Loaded < loaded, $"{ammo.defName} fires aimed");
                float shellHp = foe.health.summaryHealth.SummaryHealthPercent;
                yield return 150;
                if (ammo.defName.EndsWith("HE")) Check(foe.Destroyed || foe.Dead || foe.health.summaryHealth.SummaryHealthPercent < shellHp, "aimed HE did damage");
            }
        }

        // ------------------------------------------------------------------------------------ weapons

        [LiveScenario("weapons")]
        private IEnumerable<int> Scenario_SmartAndTechWeapons()
        {
            // smart: linked, blue-tracer guns hit a target in the open
            var smart = Colonist(Center, Thing("RPC_Gun_KangTaoG58Dian"));
            RimPunkDebugActions.DebugInstall(smart, Hediff("RPC_SmartLink"), CyberTier.Iconic);
            Check(SmartLinkUtility.HasSmartLink(smart), "smart link detected");
            var foe = Standing(Center + new IntVec3(12, 0, 0));
            smart.equipment.Primary.TryGetComp<CombatExtended.CompAmmoUser>().ResetAmmoCount();
            smart.drafter.Drafted = true;
            float hp = foe.health.summaryHealth.SummaryHealthPercent;
            var job = JobMaker.MakeJob(JobDefOf.AttackStatic, foe);
            job.playerForced = true;
            smart.jobs.StartJob(job, JobCondition.InterruptForced);
            for (int i = 0; i < 40 && !foe.Dead && !foe.Destroyed && foe.health.summaryHealth.SummaryHealthPercent >= hp; i++) yield return 10;
            Check(foe.Dead || foe.Destroyed || foe.health.summaryHealth.SummaryHealthPercent < hp, "smart gun hit its target");
            Cleanup(smart, foe);

            // tech: fires through a wall in aimed mode
            var tech = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.weaponTags?.Contains("RPC_Type_tech") == true && d.defName.StartsWith("RPC_Gun_"));
            var shooter = Colonist(Center, tech);
            var behind = Standing(Center + new IntVec3(6, 0, 0));
            var wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), Center + new IntVec3(3, 0, 0), map);
            Check(Verb_ShootTech.WallsBetween(map, shooter.Position, behind.Position) == 1, "wall counted between them");
            Check(Verb_ShootTech.IsWall(wall), "recognised as a wall");
            shooter.drafter.Drafted = true;
            var ammo = shooter.equipment.Primary.TryGetComp<CombatExtended.CompAmmoUser>();
            ammo.ResetAmmoCount();
            int mag = ammo.CurMagCount;
            var fireModes = shooter.equipment.Primary.TryGetComp<CombatExtended.CompFireModes>();
            if (fireModes != null) fireModes.CurrentAimMode = CombatExtended.AimMode.AimedShot;
            float behindHp = behind.health.summaryHealth.SummaryHealthPercent;
            var job2 = JobMaker.MakeJob(JobDefOf.AttackStatic, behind);
            job2.playerForced = true;
            var shotVerb = shooter.equipment.Primary.TryGetComp<CompEquippable>().PrimaryVerb as CombatExtended.Verb_LaunchProjectileCE;
            string report = null;
            bool canHit = shotVerb?.CanHitTarget(behind, out report) ?? false;
            Note($"tech verb {shotVerb?.GetType().Name}, aim mode {fireModes?.CurrentAimMode}, can hit through the wall: {canHit} ({report})");
            shooter.jobs.StartJob(job2, JobCondition.InterruptForced);
            yield return 3;
            shooter.jobs.StopAll();
            bool started = shotVerb.TryStartCastOn(behind);
            var taut = shooter.TryGetComp<CombatExtended.CompTacticalManager>();
            Note($"direct cast through the wall: started {started}, stance {shooter.stances.curStance?.GetType().Name}, verb {shotVerb.state}, tactical manager {taut != null}");
            yield return 2;
            Note($"2 ticks later: stance {shooter.stances.curStance?.GetType().Name}, verb {shotVerb.state}, mag {ammo.CurMagCount}, burst left {shotVerb.BurstShotCount}");
            job2 = JobMaker.MakeJob(JobDefOf.AttackStatic, behind);
            job2.playerForced = true;
            shooter.jobs.StartJob(job2, JobCondition.InterruptForced);
            var states = new List<string>();
            for (int i = 0; i < 80 && behind.health.summaryHealth.SummaryHealthPercent >= behindHp && !behind.Dead; i++)
            {
                yield return 8;
                string s = $"{shooter.CurJobDef?.defName}/{shooter.stances.curStance?.GetType().Name}/verb {shotVerb?.state}/mode {fireModes?.CurrentAimMode}/canHit {shotVerb?.CanHitTarget(behind)}/mag {ammo.CurMagCount}/attackVerb {shooter.TryGetAttackVerb(behind)?.GetType().Name}/pierce {Verb_ShootTech.WallsBetween(map, shooter.Position, behind.Position)}";
                if (!states.Contains(s)) states.Add(s);
            }
            Note("walled shot timeline: " + string.Join(" | ", states.Take(8)));
            Note($"tech gun {tech.defName} at a target behind a wall: ammo {mag} -> {ammo.CurMagCount}, job {shooter.CurJobDef?.defName}");
            Check(ammo.CurMagCount < mag, "tech gun fires at a target behind a wall");
            Check(behind.Dead || behind.health.summaryHealth.SummaryHealthPercent < behindHp, "aimed shots go through the wall and hit");
        }

        // ------------------------------------------------------------------------------------ data-driven cyberware behaviour

        private Pawn foe3Maker(Pawn near) => Standing(near.Position + new IntVec3(0, 0, 2));

        [LiveScenario("cyberware")]
        private IEnumerable<int> Scenario_TriggersAndConditionals()
        {
            // heal on kill
            var p = Colonist(Center);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_HealOnKill"), CyberTier.Iconic);
            var part = p.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside).First(x => x.def == BodyPartDefOf.Torso);
            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 12f, 999f, -1f, null, part));
            float injuredAfter = p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(i => i.Severity);
            var foe = Standing(Center + new IntVec3(2, 0, 0));
            foe.Kill(new DamageInfo(DamageDefOf.Cut, 500f, 999f, -1f, p));
            Check(p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(i => i.Severity) < injuredAfter, "heal-on-kill healed on a kill");

            // adrenaline booster, melee kill only
            var q = Colonist(Center + new IntVec3(-4, 0, 0));
            RimPunkDebugActions.DebugInstall(q, Hediff("RPC_AdrenalineBooster"), CyberTier.Iconic);
            q.GetCyberTracker().SetAdrenaline(0f);
            var foe2 = Standing(Center + new IntVec3(-6, 0, 0));
            foe2.Kill(new DamageInfo(DamageDefOf.Cut, 500f, 999f, -1f, q));
            Check(q.GetCyberTracker().Adrenaline > 0f, $"adrenaline booster on a melee kill: {q.GetCyberTracker().Adrenaline}");

            // conditional: standing still
            var r = Colonist(Center + new IntVec3(6, 0, 4));
            r.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
            RimPunkDebugActions.DebugInstall(r, Hediff("RPC_RangeGuard"), CyberTier.Iconic);
            yield return 70;
            Check(r.health.hediffSet.HasHediff(Hediff("RPC_RangeGuardBoost")), "RangeGuard armor up with nobody close");
            var close = Standing(r.Position + new IntVec3(2, 0, 0));
            yield return 100;
            Check(!r.health.hediffSet.HasHediff(Hediff("RPC_RangeGuardBoost")), "and off with an enemy close");

            // Second Heart: back on their feet
            var h = Colonist(Center + new IntVec3(-8, 0, -4));
            RimPunkDebugActions.DebugInstall(h, Hediff("RPC_SecondHeart"), CyberTier.Iconic);
            HealthUtility.AdjustSeverity(h, HediffDefOf.BloodLoss, 0.75f);
            yield return 5;
            Check(!h.Downed && !h.Dead, $"Second Heart got them back up (downed {h.Downed}, dead {h.Dead})");
            var h2 = Colonist(Center + new IntVec3(-8, 0, 4));
            RimPunkDebugActions.DebugInstall(h2, Hediff("RPC_SecondHeart"), CyberTier.Iconic);
            h2.Kill(new DamageInfo(DamageDefOf.Cut, 500f, 999f, -1f, foe3Maker(h2)));
            Check(!h2.Dead, "Second Heart cancels a killing blow");
            var h3 = Colonist(Center + new IntVec3(-8, 0, 8));
            RimPunkDebugActions.DebugInstall(h3, Hediff("RPC_SecondHeart"), CyberTier.Iconic);
            HealthUtility.AdjustSeverity(h3, HediffDefOf.BloodLoss, 0.75f);
            yield return 5;
            HealthUtility.AdjustSeverity(h3, HediffDefOf.BloodLoss, 0.75f);
            yield return 5;
            Check(h3.Downed, "second time inside the cooldown it doesn't save them");

            // cooldown reduction
            var n = Colonist(Center + new IntVec3(8, 0, -4));
            RimPunkDebugActions.DebugInstall(n, Hediff("RPC_DynalarSandevistan"), CyberTier.T3);
            RimPunkDebugActions.DebugInstall(n, Hediff("RPC_NewtonModule"), CyberTier.Iconic);
            var sandy = AbilityFrom(n, CyberBuffAiRule.CloseDistance);
            sandy.StartCooldown(6000);
            var foe3 = Dummy(n.Position + new IntVec3(2, 0, 0));
            int cd = sandy.CooldownTicksRemaining;
            CyberEvents.Notify_DamageDealt(n, foe3, new DamageInfo(DamageDefOf.Cut, 5f, 0f, -1f, n), 5f);
            Check(sandy.CooldownTicksRemaining < cd, $"Newton Module cuts cooldowns on a hit: {cd} -> {sandy.CooldownTicksRemaining}");

            // Painducer: holds damage back, drops it when down
            var d = Colonist(Center + new IntVec3(0, 0, 8));
            RimPunkDebugActions.DebugInstall(d, Hediff("RPC_Painducer"), CyberTier.Iconic);
            d.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 10f, 0f, -1f, foe3));
            Check(d.GetCyberTracker().DelayedDamagePending > 0f, $"Painducer held some back: {d.GetCyberTracker().DelayedDamagePending:0.#}");

            // mitigation blocks a hit outright
            var m = Colonist(Center + new IntVec3(-4, 0, 8));
            m.health.AddHediff(Hediff("RPC_CountershellActive"));
            var dh = m.health.hediffSet.GetFirstHediffOfDef(Hediff("RPC_CountershellActive")) as Hediff_CyberActive;
            dh?.Setup(CyberTier.Iconic, CyberTier.T1, CyberTier.Iconic, 600);
            float chance = m.GetStatValue(RPStatDefOf.RPC_MitigationChance);
            Check(chance > 0f, $"Countershell raises mitigation: {chance:P0}");
        }

        // ------------------------------------------------------------------------------------ what the player sees

        [LiveScenario("ui")]
        private IEnumerable<int> Scenario_InspectTabsAndGizmos()
        {
            var p = Colonist(Center);
            RimPunkDebugActions_Scenarios.LearnAllAt(p, CyberTier.T3);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_GorillaArms"), CyberTier.T3);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_ChromeCompressor"), CyberTier.T3);
            p.drafter.Drafted = true;
            p.GetCyberTracker().SetAdrenaline(80f);
            Find.CameraDriver.JumpToCurrentMapLoc(Center);
            Find.Selector.ClearSelection();
            Find.Selector.Select(p, playSound: false, forceDesignatorDeselect: false);
            yield return 30;
            var gizmos = p.GetGizmos().ToList();
            Check(gizmos.OfType<Gizmo_CyberBar>().Count() == 2, $"RAM and adrenaline bars on the gizmo row: {gizmos.OfType<Gizmo_CyberBar>().Count()}");
            InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Cyberware));
            yield return 30;
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, false);
            InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Cyberware));
            yield return 30;
            Check(Find.MainTabsRoot.OpenTab == MainButtonDefOf.Inspect, "cyberware tab opened");
            var menu = FloatMenuMakerMap.GetOptions(new List<Pawn> { p }, p.DrawPos + new Vector3(3f, 0, 0), out _);
            Check(menu != null, "float menu builds with a netrunner selected");
            Find.Selector.ClearSelection();
            Find.MainTabsRoot.EscapeCurrentTab(false);
            yield return 5;
        }

        [LiveScenario("ui")]
        private IEnumerable<int> Scenario_AnimationsAndAfterimages()
        {
            Find.CameraDriver.JumpToCurrentMapLoc(Center);
            var p = Colonist(Center);
            RimPunkDebugActions_Scenarios.PlayAnimations(p);
            yield return 90;
            Check(!p.Destroyed, "every cyberware animation played");
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_DynalarSandevistan"), CyberTier.T4);
            p.GetCyberTracker().SetAdrenaline(100f);
            AbilityFrom(p, CyberBuffAiRule.CloseDistance).CompOfType<CompAbilityEffect_CyberBuff>().Apply(p, p);
            p.drafter.Drafted = true;
            p.pather.StartPath(Center + new IntVec3(10, 0, 0), PathEndMode.OnCell);
            yield return 60;
            Check(p.pather.Moving || p.Position != Center, "moved under sandevistan with afterimages drawing");
        }

        [LiveScenario("ui")]
        private IEnumerable<int> Scenario_SettingsOff()
        {
            var s = RimPunkMod.Settings;
            var p = Colonist(Center);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_DynalarSandevistan"), CyberTier.T2);
            var t = p.GetCyberTracker();
            var sandy = AbilityFrom(p, CyberBuffAiRule.CloseDistance);
            t.SetAdrenaline(0f);
            s.adrenalineEnabled = false;
            Check(sandy.CanCast.Accepted, "adrenaline off: activations are free");
            t.GainAdrenaline(50f);
            Check(t.Adrenaline == 0f, "adrenaline off: nothing is gained");
            s.adrenalineEnabled = true;

            s.barks = false;
            int before = Find.PlayLog.AllEntries.OfType<PlayLogEntry_Bark>().Count();
            yield return 100;
            BarkUtility.Say(p, BarkKeys.GigOffered);
            Check(Find.PlayLog.AllEntries.OfType<PlayLogEntry_Bark>().Count() == before, "barks off: silent");
            s.barks = true;

            s.contextualAI = false;
            var target = Colonist(Center + new IntVec3(20, 0, 0));
            target.drafter.Drafted = true;
            var brawler = Hostile(Center + new IntVec3(0, 0, 4), Thing("Gun_Autopistol"));
            RimPunkDebugActions.DebugInstall(brawler, Hediff("RPC_GorillaArms"), CyberTier.T5);
            brawler.mindState.enemyTarget = target;
            bool charged = false;
            for (int i = 0; i < 20 && !charged; i++)
            {
                yield return 10;
                charged = brawler.CurJobDef == JobDefOf.AttackMelee;
            }
            Check(!charged, $"contextual AI off: vanilla behaviour (job {brawler.CurJobDef?.defName})");
            s.contextualAI = true;

            s.visualEffects = false;
            yield return 30;
            s.visualEffects = true;
            Check(true, "visual effects can be switched off and back");
        }
    }
}
