using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>Cycle 4, the wide part: every shard, the real menus, real ingest / surgery jobs, lifecycle edges.</summary>
    public partial class GameComponent_RimPunkLiveTests
    {
        [LiveScenario("cycle4-allshards")]
        private IEnumerable<int> Scenario_C4_EveryShard()
        {
            var p = WithPort(Colonist(Center));
            var tracker = p.GetCyberTracker();
            if (AptitudeUtility.IsGonk(p))
            {
                p.story.traits.RemoveTrait(p.story.traits.GetTrait(RPDefOf.RPC_CyberAptitude));
                p.story.traits.GainTrait(new Trait(RPDefOf.RPC_CyberAptitude, 0));
            }
            int tested = 0;
            foreach (var shard in ShardUtility.AllShards.Where(d => !d.ShardExt().soulkiller).ToList())
            {
                var ext = shard.ShardExt();
                float capBefore = tracker.CapacityMax;
                ShardUtility.Slot(p, shard);
                Check(tracker.shards.Contains(shard), $"{shard.defName} slots");
                if (ext.skill != null)
                    Check(p.skills.GetSkill(ext.skill).Aptitude == ext.skillLevels, $"{shard.defName}: +{ext.skillLevels} {ext.skill.defName}");
                if (ext.statFactors != null)
                    foreach (var m in ext.statFactors)
                        Check(Mathf.Approximately(tracker.GetStatFactor(m.stat), m.value), $"{shard.defName}: {m.stat.defName} x{m.value}");
                if (ext.statOffsets != null)
                    foreach (var m in ext.statOffsets)
                        Check(Mathf.Approximately(tracker.GetStatOffset(m.stat), m.value), $"{shard.defName}: {m.stat.defName} +{m.value}");
                if (ext.capacityBonus > 0f)
                    Check(Mathf.Approximately(tracker.CapacityMax, capBefore + ext.capacityBonus), $"{shard.defName}: capacity {capBefore} -> {tracker.CapacityMax}");
                ShardUtility.Unslot(p, shard, p.Position, map);
                Check(tracker.shards.Count == 0 && Mathf.Approximately(tracker.CapacityMax, capBefore), $"{shard.defName}: removing leaves nothing behind");
                if (ext.skill != null) Check(p.skills.GetSkill(ext.skill).Aptitude == 0, $"{shard.defName}: skill bonus gone");
                tested++;
            }
            Check(tested >= 13, $"tested {tested} shards (12 skills + capacity)");

            // a gonk's capacity is a hard 20: shards don't raise it
            var g = WithPort(Colonist(Center + new IntVec3(3, 0, 0)));
            MakeGonk(g);
            float gonk = g.GetCyberTracker().CapacityMax;
            ShardUtility.Slot(g, Thing("RPC_CapacityShard"));
            Check(Mathf.Approximately(g.GetCyberTracker().CapacityMax, gonk), $"capacity shard does nothing for a gonk ({gonk})");

            // two skill shards at once both count
            var q = WithPort(Colonist(Center + new IntVec3(-3, 0, 0)));
            ShardUtility.Slot(q, Thing("RPC_SkillShard_Shooting"));
            ShardUtility.Slot(q, Thing("RPC_SkillShard_Melee"));
            Check(q.skills.GetSkill(SkillDefOf.Shooting).Aptitude == 5 && q.skills.GetSkill(SkillDefOf.Melee).Aptitude == 5, "two skill shards both apply");
            yield break;
        }

        private static List<FloatMenuOption> OpenMenu(string method, params object[] args)
        {
            typeof(ITab_Pawn_Cyberware).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, args);
            var menu = Find.WindowStack.WindowOfType<FloatMenu>();
            var options = menu == null ? null : (List<FloatMenuOption>)HarmonyLib.Traverse.Create(menu).Field("options").GetValue();
            if (menu != null) Find.WindowStack.TryRemove(menu, true);
            return options;
        }

        [LiveScenario("cycle4-ui")]
        private IEnumerable<int> Scenario_C4_Ui()
        {
            var p = Colonist(Center);
            var tab = new ITab_Pawn_Cyberware();
            Check(tab != null, "tab constructs");
            var empty = OpenMenu("OpenShardMenu", p);
            Check(empty != null && empty.Count == 1 && empty[0].Disabled, "no shards in stock: one disabled hint");

            GenSpawn.Spawn(ThingMaker.MakeThing(Thing("RPC_SkillShard_Plants")), Center + new IntVec3(3, 0, 0), map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Thing("RPC_SoulkillerShard")), Center + new IntVec3(4, 0, 0), map);
            var noPort = OpenMenu("OpenShardMenu", p);
            Check(noPort != null && noPort.Count == 2 && noPort.All(o => o.Disabled), "without a port every shard is listed but locked");
            Check(noPort != null && noPort.Any(o => o.Label.Contains("comms")), "and says why");
            WithPort(p);
            var open = OpenMenu("OpenShardMenu", p);
            Check(open != null && open.Count == 2 && open.All(o => !o.Disabled), "with a port both can be picked");
            Check(open != null && open.Any(o => o.Label.ToLower().Contains("farmer")) && open.Any(o => o.Label.ToLower().Contains("soulkiller")), "labelled with the shard names");

            // pick the farmer's shard from the menu as the player would
            open.First(o => o.Label.ToLower().Contains("farmer")).action();
            yield return 400;
            Check(ShardUtility.Slotted(p).Contains(Thing("RPC_SkillShard_Plants")), "picking it from the menu slots it");

            // a prisoner gets the operation instead of the walk
            var prisoner = WithPort(Dummy(Center + new IntVec3(0, 0, 5)));
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            GenSpawn.Spawn(ThingMaker.MakeThing(Thing("RPC_SkillShard_Mining")), Center + new IntVec3(5, 0, 0), map);
            var forPrisoner = OpenMenu("OpenShardMenu", prisoner);
            Check(forPrisoner != null && forPrisoner.Any(o => o.Label.Contains("operation")), "a prisoner's menu offers the operation");
            forPrisoner?.First(o => o.Label.ToLower().Contains("miner")).action();
            Check(prisoner.BillStack.Bills.OfType<Bill_Medical>().Any(b => b.recipe.Worker is Recipe_InstallShard), "which queues an install bill");

            // engram menu
            var recipe = DefDatabase<RecipeDef>.GetNamed("RPC_InsertEngram");
            var none = OpenMenu("OpenEngramMenu", p, recipe);
            Check(none != null && none.Count == 1 && none[0].Disabled, "no engrams in stock: one disabled hint");
            GenSpawn.Spawn(ThingMaker.MakeThing(Thing("RPC_Engram_JackieWelles")), Center + new IntVec3(-3, 0, 0), map);
            var eng = OpenMenu("OpenEngramMenu", p, recipe);
            Check(eng != null && eng.Count == 1 && !eng[0].Disabled && eng[0].Label.Contains("Jackie Welles"), "engram menu lists it by person");
            eng?[0].action();
            var bill = p.BillStack.Bills.OfType<Bill_Medical>().FirstOrDefault(b => b.recipe.Worker is Recipe_InstallEngram);
            Check(bill != null && bill.uniqueRequiredIngredients?.Count == 1, "and queues the operation with that exact engram");
        }

        [LiveScenario("cycle4-ingest")]
        private IEnumerable<int> Scenario_C4_Ingest()
        {
            void Take(Pawn pawn, string defName)
            {
                var thing = ThingMaker.MakeThing(Thing(defName));
                GenSpawn.Spawn(thing, pawn.Position + new IntVec3(1, 0, 0), map);
                var job = JobMaker.MakeJob(JobDefOf.Ingest, thing);
                job.count = 1;
                pawn.jobs.StartJob(job, Verse.AI.JobCondition.InterruptForced);
            }

            var a = Colonist(Center);
            var b = Colonist(Center + new IntVec3(0, 0, 6));
            Wound(a, a.RaceProps.body.corePart, 7f); // (small: a downed pawn can't walk over to take its dose)
            Wound(b, b.RaceProps.body.corePart, 7f);
            float bBefore = Injury(b, b.RaceProps.body.corePart);
            int started = Find.TickManager.TicksGame;
            Take(a, "RPC_BounceBack_3");
            Take(b, "RPC_MaxDoc_3");
            int injectorDone = -1, inhalerDone = -1;
            for (int i = 0; i < 80 && (injectorDone < 0 || inhalerDone < 0); i++)
            {
                yield return 10;
                if (injectorDone < 0 && a.health.hediffSet.HasHediff(Hediff("RPC_BounceBackHigh_3"))) injectorDone = Find.TickManager.TicksGame - started;
                if (inhalerDone < 0 && Injury(b, b.RaceProps.body.corePart) < bBefore - 0.5f) inhalerDone = Find.TickManager.TicksGame - started;
            }
            Check(injectorDone > 0, "the pawn really walks over and injects");
            Check(inhalerDone > 0, "the pawn really inhales");
            Check(injectorDone > 0 && inhalerDone > injectorDone, $"inhaling takes longer than injecting ({inhalerDone} vs {injectorDone} ticks)");
            Check(a.health.hediffSet.BleedRateTotal <= 0.0001f && b.health.hediffSet.BleedRateTotal <= 0.0001f, "both seal the bleeding through the real ingest path");
            Check(map.listerThings.ThingsOfDef(Thing("RPC_BounceBack_3")).Count == 0 && map.listerThings.ThingsOfDef(Thing("RPC_MaxDoc_3")).Count == 0, "the doses are used up (not infinite)");

            // every tier is stronger and pricier than the one before
            for (int n = 2; n <= 5; n++)
            {
                Check(Hediff("RPC_BounceBackHigh_" + n).CompProps<HediffCompProperties_PartRegen>().totalFraction > Hediff("RPC_BounceBackHigh_" + (n - 1)).CompProps<HediffCompProperties_PartRegen>().totalFraction, $"bounce back Mk.{n} regenerates more than Mk.{n - 1}");
                Check(Thing("RPC_MaxDoc_" + n).ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_HealParts>().Single().fraction > Thing("RPC_MaxDoc_" + (n - 1)).ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_HealParts>().Single().fraction, $"maxdoc Mk.{n} heals more than Mk.{n - 1}");
                Check(Thing("RPC_MaxDoc_" + n).BaseMarketValue > Thing("RPC_MaxDoc_" + (n - 1)).BaseMarketValue && Thing("RPC_BounceBack_" + n).BaseMarketValue > Thing("RPC_BounceBack_" + (n - 1)).BaseMarketValue, $"Mk.{n} is worth more than Mk.{n - 1}");
            }
            // the recipes sit behind their own research and make exactly one dose
            foreach (string d in new[] { "RPC_BounceBack_1", "RPC_MaxDoc_5" })
            {
                var recipe = DefDatabase<RecipeDef>.AllDefsListForReading.FirstOrDefault(r => r.products?.Any(pc => pc.thingDef.defName == d && pc.count == 1) == true);
                Check(recipe != null && recipe.recipeUsers.Any(u => u.defName == "DrugLab") && recipe.researchPrerequisite != null, $"{d} has a drug lab recipe behind research");
            }
            Check(DefDatabase<ResearchProjectDef>.GetNamed("RPC_Research_MaxDoc5").prerequisites.Contains(DefDatabase<ResearchProjectDef>.GetNamed("RPC_Research_MaxDoc4")), "tech tree chains Mk.4 -> Mk.5");
        }

        [LiveScenario("cycle4-lifecycle")]
        private IEnumerable<int> Scenario_C4_Lifecycle()
        {
            var muffalo = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"), null);
            Check(!EngramUtility.CanReceive(muffalo, out _) && !ShardUtility.CanSlot(muffalo, Thing("RPC_SkillShard_Plants"), out _), "animals can't take engrams or shards");
            var plain = Colonist(Center);
            Check(!ShardUtility.CanSlot(plain, Thing("RPC_SoulkillerShard"), out _), "the soulkiller needs the port too");

            // engram shock ends: the new person wakes up
            var h = Spawn(PawnKindDefOf.Villager, enemy, Center + new IntVec3(4, 0, 0)); // (not Dummy: that one is restrained and never gets up)
            EngramUtility.Apply(h, Lore("DavidMartinez"));
            yield return 20;
            Check(h.Downed, "down after the engram");
            h.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_EngramShock).TryGetComp<HediffComp_Disappears>().ticksToDisappear = 30;
            yield return 200;
            Check(!h.health.hediffSet.HasHediff(RPDefOf.RPC_EngramShock) && !h.Downed, "when the shock wears off they get up");
            Check(h.Name.ToStringFull == "David Martinez", "and are still David");

            // a colony engram into a prisoner frees them
            var prisoner = Dummy(Center + new IntVec3(-4, 0, 0));
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            var ours = Lore("JackieWelles");
            ours.fromColony = true;
            EngramUtility.Apply(prisoner, ours);
            yield return 20;
            Check(prisoner.Faction == Faction.OfPlayer && !prisoner.IsPrisoner && prisoner.Downed, "one of ours written into a prisoner: free and ours, still down");

            // re-writing a body keeps one shock, not two
            EngramUtility.Apply(prisoner, Lore("MorganBlackhand"));
            Check(prisoner.health.hediffSet.hediffs.Count(x => x.def == RPDefOf.RPC_EngramShock) == 1, "a second engram doesn't stack the shock");
            Check(prisoner.Name.ToStringFull == "Morgan Blackhand", "second engram wins");

            // a colonist overwritten by a stranger is no longer a colonist
            var colonist = Colonist(Center + new IntVec3(0, 0, 4));
            int before = map.mapPawns.FreeColonistsSpawnedCount;
            EngramUtility.Apply(colonist, Lore("JohnnySilverhand"));
            Check(map.mapPawns.FreeColonistsSpawnedCount == before - 1, "a colonist overwritten by a stranger drops off the colony list");

            // lore engrams are valid quest rewards (the standard generator caps a reward at 5500)
            foreach (string id in LoreIds)
            {
                var def = Thing("RPC_Engram_" + id);
                Check(ThingSetMakerUtility.CanGenerate(def) && def.PlayerAcquirable && def.BaseMarketValue <= 5500f, $"{id}: can be generated as a reward within the 5500 cap ({def.BaseMarketValue})");
            }

            // exotic traders: shards come out as real items
            var gen = new StockGenerator_Shard { countRange = new IntRange(2, 2), soulkillerChance = 1f };
            var things = gen.GenerateThings(default).ToList();
            Check(things.Count == 3 && things.Any(t => t.def.ShardExt().soulkiller) && things.All(t => t.def.HasModExtension<ShardExtension>()), "shard stock generates shards (and the soulkiller when it rolls)");
            Check(new StockGenerator_Shard { countRange = new IntRange(1, 1), soulkillerChance = 0f }.GenerateThings(default).All(t => !t.def.ShardExt().soulkiller), "no soulkiller unless it rolls");

            // engram data survives a Scribe round trip
            string path = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "rpc_engram_roundtrip.xml");
            EngramData loaded = null;
            var original = Lore("AdamSmasher");
            original.fromColony = true;
            try
            {
                Scribe.saver.InitSaving(path, "test");
                Scribe_Deep.Look(ref original, "engram");
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loaded, "engram");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            Check(loaded != null && loaded.FullName == "Adam Smasher" && loaded.fromColony && loaded.skills.Count == 12 && loaded.traits.Count == 3
                  && loaded.childhood == original.childhood && loaded.adulthood == original.adulthood && loaded.resistance == original.resistance, "engram data round-trips through Scribe");
        }

        [LiveScenario("cycle4-surgery")]
        private IEnumerable<int> Scenario_C4_RealSurgery()
        {
            // the real thing: a doctor, a bed, medicine, a queued bill
            var doctor = Colonist(Center + new IntVec3(-3, 0, 0));
            for (int tries = 0; tries < 15 && doctor.WorkTypeIsDisabled(WorkTypeDefOf.Doctor); tries++) doctor = Colonist(Center + new IntVec3(-3, 0, 0)); // some backstories forbid doctoring
            Check(!doctor.WorkTypeIsDisabled(WorkTypeDefOf.Doctor), "a doctor who can doctor");
            doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 14;
            doctor.workSettings.EnableAndInitialize();
            foreach (var w in DefDatabase<WorkTypeDef>.AllDefsListForReading) doctor.workSettings.SetPriority(w, 0);
            doctor.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1);
            var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, Center + new IntVec3(2, 0, 0), map, Rot4.North);
            for (int i = 0; i < 3; i++)
            {
                var med = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                med.stackCount = 3;
                GenSpawn.Spawn(med, Center + new IntVec3(-2 - i, 0, 2), map);
            }

            var patient = WithPort(Colonist(Center + new IntVec3(2, 0, 1)));
            patient.health.AddHediff(HediffDefOf.Anesthetic);
            patient.Position = bed.Position;
            yield return 5;
            var medicine = Thing("RPC_SkillShard_Medicine");
            var install = DefDatabase<RecipeDef>.GetNamed("RPC_InsertShard_RPC_SkillShard_Medicine");
            var remove = DefDatabase<RecipeDef>.GetNamed("RPC_RemoveShard_RPC_SkillShard_Medicine");
            int attempts = 0, failedOps = 0;
            bool inconclusive = false; // the doctor never got to it in time: vanilla scheduling, not something to fail the mod for
            // real surgeries can fail (the ingredients are lost, nothing is applied): do it again, like a player would
            while (!ShardUtility.Slotted(patient).Contains(medicine) && attempts < 6)
            {
                attempts++;
                var shard = ThingMaker.MakeThing(medicine);
                GenSpawn.Spawn(shard, Center + new IntVec3(-2, 0, -2), map);
                HealthCardUtility.CreateSurgeryBill(patient, install, null, new List<Thing> { shard });
                if (attempts == 1) Check(patient.BillStack.Bills.Count == 1, "the install bill is created for the patient");
                for (int i = 0; i < 150 && patient.BillStack.Bills.Count > 0; i++) yield return 60;
                if (patient.BillStack.Bills.Count > 0)
                {
                    Note($"INCONCLUSIVE: the doctor never finished the operation (job {doctor.CurJobDef?.defName}, patient in bed {patient.InBed()} downed {patient.Downed}, shard {shard.Spawned}/{shard.Destroyed})");
                    inconclusive = true;
                    break;
                }
                if (!ShardUtility.Slotted(patient).Contains(medicine)) failedOps++;
                Check(shard.Destroyed, "the shard item is consumed by the operation");
            }
            if (inconclusive) yield break;
            Check(ShardUtility.Slotted(patient).Contains(medicine), $"a doctor really performs the shard operation ({attempts} tries, {failedOps} failed surgeries)");

            // and takes it back out
            attempts = 0;
            while (ShardUtility.Slotted(patient).Contains(medicine) && attempts < 6)
            {
                attempts++;
                HealthCardUtility.CreateSurgeryBill(patient, remove, null, null);
                for (int i = 0; i < 150 && patient.BillStack.Bills.Count > 0; i++) yield return 60;
                if (patient.BillStack.Bills.Count > 0) { Note("INCONCLUSIVE: the doctor never finished the removal"); yield break; }
            }
            Check(ShardUtility.Slotted(patient).Count == 0, $"a doctor really removes it ({attempts} tries)");
            Check(map.listerThings.ThingsOfDef(medicine).Count == 1, "and the shard comes back as an item");
        }
    }
}
