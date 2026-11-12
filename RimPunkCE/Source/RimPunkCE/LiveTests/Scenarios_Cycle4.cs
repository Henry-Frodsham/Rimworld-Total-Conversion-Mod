using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>Cycle 4: neural shards, soulkiller + engrams, and the bounce back / maxdoc health items.</summary>
    public partial class GameComponent_RimPunkLiveTests
    {
        private static readonly string[] LoreIds = { "JackieWelles", "JohnnySilverhand", "AdamSmasher", "MorganBlackhand", "DavidMartinez" };

        private static EngramData Lore(string id) => Thing("RPC_Engram_" + id).GetCompProperties<CompProperties_Engram>().person.ToData();

        private Pawn WithPort(Pawn p)
        {
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_CommsImplant"), CyberTier.T1);
            return p;
        }

        private static List<Thing> Engrams(Map map) => map.listerThings.AllThings.Where(t => t.TryGetComp<CompEngram>() != null).ToList();

        [LiveScenario("cycle4-defs")]
        private IEnumerable<int> Scenario_C4_Defs()
        {
            var skillShards = ShardUtility.AllShards.Where(d => d.ShardExt().skill != null).ToList();
            foreach (var skill in DefDatabase<SkillDef>.AllDefsListForReading)
                Check(skillShards.Any(d => d.ShardExt().skill == skill && d.ShardExt().skillLevels == 5), $"a +5 shard for {skill.defName}");
            var farmer = Thing("RPC_SkillShard_Plants").ShardExt();
            Check(farmer.statFactors.GetStatFactorFromList(StatDefOf.PlantWorkSpeed) == 1.25f && farmer.statFactors.GetStatFactorFromList(StatDefOf.PlantHarvestYield) == 1.25f, "farmer's shard: +25% planting speed and harvest yield");
            Check(Thing("RPC_CapacityShard").ShardExt().capacityBonus == 25f, "capacity shard is +25");
            Check(Thing("RPC_SoulkillerShard").ShardExt().soulkiller, "soulkiller shard exists");
            foreach (var shard in ShardUtility.AllShards)
            {
                Check(DefDatabase<RecipeDef>.GetNamedSilentFail("RPC_InsertShard_" + shard.defName) != null, $"insert surgery for {shard.defName}");
                if (!shard.ShardExt().soulkiller) Check(DefDatabase<RecipeDef>.GetNamedSilentFail("RPC_RemoveShard_" + shard.defName) != null, $"remove surgery for {shard.defName}");
            }
            var engramRecipe = DefDatabase<RecipeDef>.GetNamed("RPC_InsertEngram");
            Check(engramRecipe.Worker is Recipe_InstallEngram && engramRecipe.recipeUsers.Contains(ThingDefOf.Human), "engram operation exists for humans");
            foreach (var id in LoreIds)
            {
                var def = Thing("RPC_Engram_" + id);
                var person = def.GetCompProperties<CompProperties_Engram>().person;
                Check(person != null && person.skills.Count == 12 && person.childhood != null && person.adulthood != null, $"{id}: full persona");
                Check(def.thingSetMakerTags?.Contains("RewardStandardLowFreq") == true && def.generateCommonality < 0.2f, $"{id}: rare quest reward");
                Check(engramRecipe.fixedIngredientFilter.Allows(def), $"{id}: engram operation accepts it");
            }
            Check(EngramUtility.GenericEngram.generateCommonality == 0f, "soulkiller engram is never a reward");
            foreach (string kind in new[] { "Orbital_Exotic", "Caravan_Outlander_Exotic" })
            {
                var gens = DefDatabase<TraderKindDef>.GetNamed(kind).stockGenerators;
                Check(gens.Any(g => g is StockGenerator_Engram) && gens.Any(g => g is StockGenerator_Shard), $"{kind} stocks shards and engrams");
            }
            Check(!DefDatabase<TraderKindDef>.GetNamed("Orbital_BulkGoods").stockGenerators.Any(g => g is StockGenerator_Engram), "ordinary traders don't carry engrams");
            int hits = 0;
            var gen = new StockGenerator_Engram { chance = 0.12f };
            for (int i = 0; i < 600; i++) hits += gen.GenerateThings(default).Count();
            Check(hits > 25 && hits < 140, $"exotic traders carry an engram rarely ({hits}/600)");
            yield break;
        }

        [LiveScenario("cycle4-shards")]
        private IEnumerable<int> Scenario_C4_Shards()
        {
            var p = Colonist(Center);
            var farmer = Thing("RPC_SkillShard_Plants");
            var capacity = Thing("RPC_CapacityShard");
            var melee = Thing("RPC_SkillShard_Melee");
            var tracker = p.GetCyberTracker();
            Check(!ShardUtility.CanSlot(p, farmer, out string why) && !why.NullOrEmpty(), "no shard port until basic cyberware is installed");
            WithPort(p);
            Check(ShardUtility.CanSlot(p, farmer, out _), "the comms implant opens the shard ports");

            var plants = p.skills.GetSkill(SkillDefOf.Plants);
            plants.Level = 5;
            ShardUtility.Slot(p, farmer);
            // (a colonist whose backstory forbids plants reads level 0 whatever the bonus: check the bonus itself)
            Check(plants.Aptitude == 5 && (plants.TotallyDisabled || plants.Level == 10), $"farmer's shard adds 5 plants levels ({plants.Level}, aptitude {plants.Aptitude})");
            Check(Mathf.Approximately(tracker.GetStatFactor(StatDefOf.PlantWorkSpeed), 1.25f) && Mathf.Approximately(tracker.GetStatFactor(StatDefOf.PlantHarvestYield), 1.25f), "planting speed and harvest yield x1.25");
            ShardUtility.Unslot(p, farmer, p.Position, map);
            Check(plants.Aptitude == 0 && (plants.TotallyDisabled || plants.Level == 5), "removing it takes the levels back");
            Check(Mathf.Approximately(tracker.GetStatFactor(StatDefOf.PlantWorkSpeed), 1f), "and the speed");

            float capBefore = tracker.CapacityMax;
            ShardUtility.Slot(p, farmer);
            ShardUtility.Slot(p, capacity);
            Check(ShardUtility.Slotted(p).Count == 2, "two shards at once");
            Check(AptitudeUtility.IsGonk(p) ? Mathf.Approximately(tracker.CapacityMax, capBefore) : Mathf.Approximately(tracker.CapacityMax, capBefore + 25f), $"capacity shard: +25 max ({capBefore} -> {tracker.CapacityMax})");
            Check(!ShardUtility.CanSlot(p, melee, out _), "a third shard is refused");
            Check(!ShardUtility.CanSlot(p, farmer, out _), "the same shard twice is refused");
            ShardUtility.Slot(p, melee);
            Check(ShardUtility.Slotted(p).Count == 2, "slotting a third does nothing");

            // multiplayer-safe entry points: the pawn fetches a shard from the floor, removal is instant
            ShardUtility.OrderRemove(p, capacity);
            Check(ShardUtility.Slotted(p).Count == 1, "removal needs no surgery");
            Check(map.listerThings.ThingsOfDef(capacity).Count == 1, "the removed shard drops as an item");
            ShardUtility.OrderRemove(p, farmer);
            var shard = ThingMaker.MakeThing(melee);
            GenSpawn.Spawn(shard, Center + new IntVec3(4, 0, 0), map);
            ShardUtility.OrderSlot(p, shard);
            yield return 400;
            Check(ShardUtility.Slotted(p).Contains(melee) && shard.Destroyed, "the pawn walks over and slots it");

            // forced insertion and removal by operation, on someone who didn't ask
            var victim = WithPort(Dummy(Center + new IntVec3(0, 0, 5)));
            var insert = DefDatabase<RecipeDef>.GetNamed("RPC_InsertShard_RPC_SkillShard_Social");
            Check(insert.Worker.AvailableOnNow(victim), "insert operation is offered on a pawn with a port");
            insert.Worker.ApplyOnPawn(victim, null, null, new List<Thing> { ThingMaker.MakeThing(Thing("RPC_SkillShard_Social")) }, null);
            Check(ShardUtility.Slotted(victim).Contains(Thing("RPC_SkillShard_Social")), "an operation slots a shard into anyone");
            var remove = DefDatabase<RecipeDef>.GetNamed("RPC_RemoveShard_RPC_SkillShard_Social");
            Check(remove.Worker.AvailableOnNow(victim), "remove operation is offered once it's in");
            remove.Worker.ApplyOnPawn(victim, null, null, new List<Thing>(), null);
            Check(ShardUtility.Slotted(victim).Count == 0, "an operation takes it back out");
            var noPort = Dummy(Center + new IntVec3(0, 0, -5));
            Check(!insert.Worker.AvailableOnNow(noPort), "no operation without a port");

            // losing the port drops what's in it
            var q = WithPort(Colonist(Center + new IntVec3(-4, 0, 0)));
            ShardUtility.Slot(q, farmer);
            q.health.RemoveHediff(q.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_CommsImplant));
            Check(ShardUtility.Slotted(q).Count == 0, "removing the comms implant drops the shards");
        }

        [LiveScenario("cycle4-soulkiller")]
        private IEnumerable<int> Scenario_C4_Soulkiller()
        {
            var soulkiller = Thing("RPC_SoulkillerShard");
            var p = WithPort(Colonist(Center));
            p.skills.GetSkill(SkillDefOf.Plants).Level = 9;
            p.skills.GetSkill(SkillDefOf.Plants).passion = Passion.Major;
            string name = p.Name.ToStringFull;
            var childhood = p.story.Childhood;
            int before = Engrams(map).Count;
            Check(ShardUtility.CanSlot(p, soulkiller, out _), "soulkiller can be slotted with a port");
            ShardUtility.Slot(p, soulkiller);
            Check(p.Dead, "the soulkiller kills its wearer");
            var made = Engrams(map);
            Check(made.Count == before + 1, "and leaves an engram");
            var data = made.Last().TryGetComp<CompEngram>().Data;
            Check(data.FullName == name, $"engram carries their name ({data.FullName})");
            Check(data.skills.First(s => s.skill == SkillDefOf.Plants).level == 9 && data.skills.First(s => s.skill == SkillDefOf.Plants).passion == Passion.Major, "and their skills and passions");
            Check(data.childhood == childhood && data.fromColony, "and their backstory, remembering they were one of ours");
            Check(made.Last().LabelCap.ToString().Contains(data.first), "the item is labelled with the person");

            var enemyPawn = WithPort(Dummy(Center + new IntVec3(3, 0, 0)));
            var slayRecipe = DefDatabase<RecipeDef>.GetNamed("RPC_InsertShard_RPC_SoulkillerShard");
            slayRecipe.Worker.ApplyOnPawn(enemyPawn, null, null, new List<Thing> { ThingMaker.MakeThing(soulkiller) }, null);
            Check(enemyPawn.Dead, "forced into an enemy by operation, it kills them too");
            var enemyData = Engrams(map).Last().TryGetComp<CompEngram>().Data;
            Check(!enemyData.fromColony, "an enemy's engram is not from the colony");
            yield break;
        }

        private struct Look
        {
            public Gender gender;
            public long age;
            public HeadTypeDef head;
            public BodyTypeDef body;
            public HairDef hair;
            public Color hairColor;
            public BeardDef beard;

            public Look(Pawn p)
            {
                gender = p.gender;
                age = p.ageTracker.AgeBiologicalTicks;
                head = p.story.headType;
                body = p.story.bodyType;
                hair = p.story.hairDef;
                hairColor = p.story.HairColor;
                beard = p.style?.beardDef;
            }

            public bool Same(Look o) => gender == o.gender && age == o.age && head == o.head && body == o.body && hair == o.hair && hairColor == o.hairColor && beard == o.beard;
        }

        [LiveScenario("cycle4-engrams")]
        private IEnumerable<int> Scenario_C4_Engrams()
        {
            var jackie = Lore("JackieWelles");
            Check(jackie.FullName == "Jackie Welles", $"lore name ({jackie.FullName})");
            Check(Lore("AdamSmasher").FullName == "Adam Smasher", "Adam Smasher reads as Adam Smasher");
            var order = LoreIds.Select(id => Lore(id)).OrderBy(d => d.resistance).ToList();
            Check(order[0].last == "Welles" && order[order.Count - 1].last == "Smasher", "Jackie is the easiest to recruit, Smasher the hardest");
            Check(jackie.skills.First(s => s.skill == SkillDefOf.Social).level == 15, "lore skills are on the engram");

            // 1. written over a colonist: not one of ours, so they leave the colony
            var p = WithPort(Colonist(Center));
            var look = new Look(p);
            var aptitude = p.story.traits.GetTrait(RPDefOf.RPC_CyberAptitude).Degree;
            EngramUtility.Apply(p, jackie.Clone());
            Check(p.Name.ToStringFull == "Jackie Welles", "name overwritten");
            Check(p.story.Adulthood.defName == "RPC_Adult_JackieWelles" && p.story.Childhood.defName == "RPC_Child_JackieWelles", "backstory overwritten");
            Check(p.skills.GetSkill(SkillDefOf.Social).levelInt == 15 && p.skills.GetSkill(SkillDefOf.Social).passion == Passion.Major && p.skills.GetSkill(SkillDefOf.Plants).levelInt == 1, "skills and passions overwritten");
            Check(p.story.traits.HasTrait(TraitDefOf.Kind) && p.story.traits.allTraits.Count(t => t.def != RPDefOf.RPC_CyberAptitude) == 3, "traits overwritten");
            Check(p.story.traits.GetTrait(RPDefOf.RPC_CyberAptitude)?.Degree == aptitude, "the body's chrome tolerance stays");
            Check(look.Same(new Look(p)), "appearance is untouched");
            Check(p.health.hediffSet.HasHediff(RPDefOf.RPC_EngramShock), "engram shock applied");
            var disappears = p.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_EngramShock).TryGetComp<HediffComp_Disappears>();
            Check(disappears != null && disappears.ticksToDisappear > 29000 && disappears.ticksToDisappear <= 30000, $"for 12 hours ({disappears?.ticksToDisappear} ticks)");
            Check(p.Faction == null, "not one of ours: they are no longer a colonist");
            yield return 30;
            Check(p.Downed && !p.Awake(), "down and out");
            p.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            Check(p.IsPrisonerOfColony && Mathf.Approximately(p.guest.resistance, jackie.resistance) && Mathf.Approximately(p.guest.will, jackie.will),
                $"arrested: resistance {p.guest.resistance} will {p.guest.will} from the lore (expected {jackie.resistance}/{jackie.will})");

            // 2. an enemy given Smasher is as stubborn as he was
            var smasher = Lore("AdamSmasher");
            var h = Dummy(Center + new IntVec3(0, 0, 4));
            EngramUtility.Apply(h, smasher.Clone());
            yield return 30;
            Check(h.Faction == enemy && h.Downed, "an enemy stays an enemy, down for the shock");
            h.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            Check(Mathf.Approximately(h.guest.resistance, smasher.resistance) && smasher.resistance > 30f, $"Smasher's resistance is {h.guest.resistance}");

            // 3. a prisoner stays a prisoner, with the new person's numbers
            var prisoner = Dummy(Center + new IntVec3(4, 0, 0));
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            var johnny = Lore("JohnnySilverhand");
            EngramUtility.Apply(prisoner, johnny.Clone());
            Check(prisoner.IsPrisonerOfColony && Mathf.Approximately(prisoner.guest.resistance, johnny.resistance), "a prisoner stays a prisoner with the engram's resistance");

            // 4. an engram of one of our own: they join at once, still down
            var stranger = Dummy(Center + new IntVec3(-4, 0, 0));
            var ours = jackie.Clone();
            ours.fromColony = true;
            EngramUtility.Apply(stranger, ours);
            yield return 30;
            Check(stranger.Faction == Faction.OfPlayer && !stranger.IsPrisoner, "an engram from the colony joins the colony immediately");
            Check(stranger.Downed, "but still down for 12 hours");

            // 5. the operation
            var recipe = DefDatabase<RecipeDef>.GetNamed("RPC_InsertEngram");
            var third = Dummy(Center + new IntVec3(0, 0, -4));
            Check(recipe.Worker.AvailableOnNow(third), "operation offered");
            var item = ThingMaker.MakeThing(Thing("RPC_Engram_DavidMartinez"));
            recipe.Worker.ApplyOnPawn(third, null, null, new List<Thing> { item }, null);
            Check(third.Name.ToStringFull == "David Martinez" && third.health.hediffSet.HasHediff(RPDefOf.RPC_EngramShock), "the operation writes the engram");
        }

        [LiveScenario("cycle4-health")]
        private IEnumerable<int> Scenario_C4_HealthItems()
        {
            for (int n = 1; n <= 5; n++)
            {
                foreach (string prefix in new[] { "RPC_BounceBack_", "RPC_MaxDoc_" })
                {
                    var def = Thing(prefix + n);
                    Check(def.costList.Any(c => c.thingDef.defName == "Neutroamine" && c.count > 0) && def.costList.Any(c => c.thingDef == ThingDefOf.Steel && c.count > 0), $"{def.defName} costs neutroamine and steel");
                    Check(def.recipeMaker.researchPrerequisite != null && def.recipeMaker.researchPrerequisite.tab.defName == "RPC_CyberwareTab", $"{def.defName} has a tech tree entry");
                }
            }
            Check(Thing("RPC_BounceBack_1").ingestible.ingestSound.defName == "Ingest_Inject", "bounce back uses the go-juice sound");
            Check(Thing("RPC_MaxDoc_1").ingestible.ingestSound.defName == "Ingest_Snort", "maxdoc uses the yayo sound");
            Check(Thing("RPC_MaxDoc_3").ingestible.baseIngestTicks > Thing("RPC_BounceBack_3").ingestible.baseIngestTicks, "the inhaler takes longer than the injector");
            Check(Hediff("RPC_BounceBackHigh_5").CompProps<HediffCompProperties_PartRegen>().totalFraction == 0.25f, "bounce back tops out at 25% regeneration");
            Check(Thing("RPC_MaxDoc_5").ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_HealParts>().Single().fraction == 0.35f, "maxdoc tops out at 35% instant healing");
            Check(Thing("RPC_MaxDoc_5").ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_Adrenaline>().Count() == 0
                  && Hediff("RPC_BounceBackHigh_5").stages[0].statFactors != null
                  && Thing("RPC_BounceBack_5").ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_Adrenaline>().Count() == 1, "only the injector gives adrenaline and speed");

            // maxdoc: instant, 35% of the part, seals bleeding, nothing else
            var p = Colonist(Center);
            var tracker = p.GetCyberTracker();
            var torso = p.RaceProps.body.corePart;
            var arm = p.RaceProps.body.AllParts.First(b => b.def.defName == "Shoulder");
            Wound(p, torso, 20f);
            Wound(p, arm, 6f);
            Check(p.health.hediffSet.BleedRateTotal > 0f, "wounded and bleeding");
            float torsoBefore = Injury(p, torso);
            float torsoAllBefore = InjuryAll(p, torso);
            float adrenalineBefore = tracker.Adrenaline; // taking the wounds already built some
            Use(p, Thing("RPC_MaxDoc_5"));
            float torsoHealed = torsoAllBefore - InjuryAll(p, torso);
            float torsoScarred = Injury(p, torso) < torsoBefore - torsoHealed - 0.01f ? torsoBefore - torsoHealed - Injury(p, torso) : 0f; // healed into a permanent scar
            float torsoWant = Mathf.Min(torsoBefore, torso.def.GetMaxHealth(p) * 0.35f); // apparel can soak part of the wound
            Check(torsoHealed <= torso.def.GetMaxHealth(p) * 0.35f + 0.6f && (Mathf.Abs(torsoHealed - torsoWant) < 0.6f || torsoScarred > 0f), $"maxdoc Mk.5 restores 35% of the torso at once ({torsoHealed:0.#} of {torsoWant:0.#}; wound was {torsoBefore:0.#}, left: {string.Join("+", p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Part == torso).Select(i => i.def.defName + ":" + i.Severity.ToString("0.#") + (i.CanHealNaturally() || i.CanHealFromTending() ? "" : "!")))})");
            Check(p.health.hediffSet.BleedRateTotal <= 0.0001f, "and seals every wound");
            Check(Mathf.Approximately(tracker.Adrenaline, adrenalineBefore) && !p.health.hediffSet.HasHediff(Hediff("RPC_BounceBackHigh_5")), "no adrenaline, no regeneration");
            Check(Thing("RPC_MaxDoc_5").ingestible.outcomeDoers.All(d => d is IngestionOutcomeDoer_HealParts || d is IngestionOutcomeDoer_SealWounds), "and nothing else in the dose: no speed");

            // bounce back: brief regeneration, adrenaline, speed
            var q = Colonist(Center + new IntVec3(5, 0, 0));
            var qTracker = q.GetCyberTracker();
            Wound(q, q.RaceProps.body.corePart, 20f);
            float qBefore = Injury(q, q.RaceProps.body.corePart);
            float qAllBefore = InjuryAll(q, q.RaceProps.body.corePart);
            Use(q, Thing("RPC_BounceBack_5"));
            Check(q.health.hediffSet.BleedRateTotal <= 0.0001f, "bounce back seals the bleeding too");
            if (RimPunkMod.Settings.adrenalineEnabled) Check(qTracker.AdrenalineFraction > 0.35f, $"adrenaline rush ({qTracker.AdrenalineFraction:P0})");
            var high = q.health.hediffSet.GetFirstHediffOfDef(Hediff("RPC_BounceBackHigh_5"));
            Check(high != null, "regeneration hediff applied");
            Check(Injury(q, q.RaceProps.body.corePart) >= qBefore - 0.1f, "regeneration is gradual, not instant");
            yield return 1300;
            float healed = qAllBefore - InjuryAll(q, q.RaceProps.body.corePart);
            bool scarred = Injury(q, q.RaceProps.body.corePart) < qBefore - healed - 0.01f; // the rest of the wound set as a scar
            float want = Mathf.Min(qBefore, q.RaceProps.body.corePart.def.GetMaxHealth(q) * 0.25f); // apparel can soak part of the wound
            Check(healed <= q.RaceProps.body.corePart.def.GetMaxHealth(q) * 0.25f + 1.2f && (Mathf.Abs(healed - want) < 1.2f || scarred), $"over its duration it regrew 25% of the torso ({healed:0.#} of {want:0.#}; wound was {qBefore:0.#}, torso max {q.RaceProps.body.corePart.def.GetMaxHealth(q):0.#}, left: {string.Join("+", q.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Part == q.RaceProps.body.corePart).Select(i => i.def.defName + ":" + i.Severity.ToString("0.#")))})");
            Check(!q.health.hediffSet.HasHediff(Hediff("RPC_BounceBackHigh_5")), "and wore off: brief");
        }

        private static void Wound(Pawn p, BodyPartRecord part, float amount) =>
            p.TakeDamage(new DamageInfo(DamageDefOf.Cut, amount, 0f, -1f, null, part));

        /// <summary>Everything on the part, scars included.</summary>
        private static float InjuryAll(Pawn p, BodyPartRecord part) =>
            p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Part == part).Sum(i => i.Severity);

        /// <summary>What a heal can still take: vanilla turns the rest of a deep wound into a permanent scar as it heals, which no medicine touches.</summary>
        private static float Injury(Pawn p, BodyPartRecord part) =>
            p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Part == part && (i.CanHealNaturally() || i.CanHealFromTending())).Sum(i => i.Severity); // generated colonists carry old scars, which no medicine heals

        /// <summary>Takes a dose as if it had been ingested: the outcome doers, without the walk.</summary>
        private static void Use(Pawn p, ThingDef drug)
        {
            var thing = ThingMaker.MakeThing(drug);
            foreach (var doer in drug.ingestible.outcomeDoers) doer.DoIngestionOutcome(p, thing, 1);
        }
    }
}
