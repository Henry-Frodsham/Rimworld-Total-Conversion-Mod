using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>
    /// Marks a ThingDef as a neural shard (cycle 4). A slotted shard lives in <see cref="CompCyberTracker.shards"/> as its def:
    /// effects are read from here every time they are asked for, so a shard is just XML (gen_shards.py).
    /// </summary>
    public class ShardExtension : DefModExtension
    {
        /// <summary>Skill this shard raises by <see cref="skillLevels"/> levels (on top of the real level, like an aptitude).</summary>
        public SkillDef skill;
        public int skillLevels;

        /// <summary>Flat cyberware capacity added to the maximum (not for a Gonk, nothing raises a Gonk).</summary>
        public float capacityBonus;

        /// <summary>The soulkiller shard is never stored: slotting it kills the pawn and leaves an engram of them.</summary>
        public bool soulkiller;

        public List<StatModifier> statOffsets;
        public List<StatModifier> statFactors;
    }

    /// <summary>On the generated install / remove surgeries: which shard they handle.</summary>
    public class ShardRecipeExtension : DefModExtension
    {
        public ThingDef shard;
    }

    public static class ShardUtility
    {
        /// <summary>Shards a pawn can carry at once.</summary>
        public const int MaxSlots = 2;

        public static ShardExtension ShardExt(this ThingDef def) => def?.GetModExtension<ShardExtension>();

        public static IEnumerable<ThingDef> AllShards => DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.HasModExtension<ShardExtension>());

        public static IReadOnlyList<ThingDef> Slotted(Pawn pawn) => (IReadOnlyList<ThingDef>)pawn?.GetCyberTracker()?.shards ?? System.Array.Empty<ThingDef>();

        /// <summary>The "basic cyberware" a shard port hangs off: the comms implant every basic implant needs.</summary>
        public static bool HasShardPort(Pawn pawn) =>
            RPDefOf.RPC_CommsImplant != null && pawn?.health?.hediffSet?.HasHediff(RPDefOf.RPC_CommsImplant) == true;

        public static int SkillBonus(CompCyberTracker tracker, SkillDef skill)
        {
            int bonus = 0;
            var list = tracker.shards;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i].ShardExt();
                if (e != null && e.skill == skill) bonus += e.skillLevels;
            }
            return bonus;
        }

        public static float CapacityBonus(Pawn pawn)
        {
            var list = pawn?.GetCyberTracker()?.shards;
            if (list == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < list.Count; i++) sum += list[i].ShardExt()?.capacityBonus ?? 0f;
            return sum;
        }

        /// <summary>Whether this pawn can take <paramref name="shard"/> right now (port, free slot, no twin). Soulkiller needs only the port.</summary>
        public static bool CanSlot(Pawn pawn, ThingDef shard, out string reason)
        {
            reason = null;
            var tracker = pawn?.GetCyberTracker();
            if (tracker == null || pawn.RaceProps?.Humanlike != true) { reason = "RPC_Shard_NotHumanlike".Translate(); return false; }
            if (!HasShardPort(pawn)) { reason = "RPC_Shard_NeedsPort".Translate(RPDefOf.RPC_CommsImplant?.label ?? "comms implant"); return false; }
            var ext = shard.ShardExt();
            if (ext == null) return false;
            if (ext.soulkiller) return true;
            if (tracker.shards.Contains(shard)) { reason = "RPC_Shard_AlreadySlotted".Translate(shard.label); return false; }
            if (tracker.shards.Count >= MaxSlots) { reason = "RPC_Shard_SlotsFull".Translate(MaxSlots); return false; }
            return true;
        }

        /// <summary>Slots the shard (the item itself is consumed by the caller). A soulkiller shard kills <paramref name="pawn"/> instead.</summary>
        public static void Slot(Pawn pawn, ThingDef shard, Pawn surgeon = null)
        {
            var ext = shard.ShardExt();
            var tracker = pawn?.GetCyberTracker();
            if (ext == null || tracker == null) return;
            if (ext.soulkiller)
            {
                EngramUtility.Soulkill(pawn, surgeon);
                return;
            }
            if (tracker.shards.Contains(shard) || tracker.shards.Count >= MaxSlots) return;
            tracker.shards.Add(shard);
            Changed(pawn, shard);
            Diag.Event("shard", $"{Diag.Describe(pawn)} slotted {shard.defName}");
        }

        /// <summary>Takes the shard out and drops its item at <paramref name="pos"/>.</summary>
        public static bool Unslot(Pawn pawn, ThingDef shard, IntVec3 pos, Map map)
        {
            var tracker = pawn?.GetCyberTracker();
            if (tracker == null || !tracker.shards.Remove(shard)) return false;
            Changed(pawn, shard);
            if (map != null)
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(shard), pos, map, ThingPlaceMode.Near);
            Diag.Event("shard", $"{Diag.Describe(pawn)} removed {shard.defName}");
            return true;
        }

        /// <summary>Port gone (the comms implant was removed by force): slotted shards drop out, a pawn can't keep running them.</summary>
        public static void DropAll(Pawn pawn)
        {
            var tracker = pawn?.GetCyberTracker();
            if (tracker == null) return;
            foreach (var s in tracker.shards.ToList()) Unslot(pawn, s, pawn.PositionHeld, pawn.MapHeld);
        }

        private static void Changed(Pawn pawn, ThingDef shard)
        {
            var ext = shard.ShardExt();
            if (ext?.statOffsets != null) foreach (var m in ext.statOffsets) m.stat.Worker.ClearCacheForThing(pawn);
            if (ext?.statFactors != null) foreach (var m in ext.statFactors) m.stat.Worker.ClearCacheForThing(pawn);
            RPStatDefOf.RPC_CyberwareCapacity.Worker.ClearCacheForThing(pawn);
            pawn.skills?.DirtyAptitudes();
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
            pawn.GetCyberTracker()?.Notify_CyberwareChanged();
        }

        // ---------------------------------------------------------------- player actions (multiplayer: synced, run on every client)

        /// <summary>The pawn walks to the shard and slots it (no surgery).</summary>
        [SyncMethod]
        public static void OrderSlot(Pawn pawn, Thing shard)
        {
            if (pawn == null || shard == null || shard.Destroyed || !shard.Spawned) return;
            if (!CanSlot(pawn, shard.def, out _)) return;
            var job = JobMaker.MakeJob(RPDefOf.RPC_SlotShard, shard);
            job.count = 1;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>Slot out of the tab: removal is instant, the item drops at the pawn's feet.</summary>
        [SyncMethod]
        public static void OrderRemove(Pawn pawn, ThingDef shard)
        {
            if (pawn == null || shard == null || !pawn.Spawned) return;
            Unslot(pawn, shard, pawn.Position, pawn.Map);
        }

        /// <summary>Tab install for an engram: walks to the item and queues nothing else; see Recipe_InstallEngram for the operation.</summary>
        public static ThingDef Soulkiller => DefDatabase<ThingDef>.GetNamedSilentFail("RPC_SoulkillerShard");
    }

    // ------------------------------------------------------------------------------------------------ patches

    /// <summary>Shards raise skills the way the Anomaly aptitudes do. Vanilla only reads hediff aptitudes with Anomaly active, so the shard bonus is added here.</summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Aptitude), MethodType.Getter)]
    public static class Patch_SkillRecord_Aptitude_Shards
    {
        public static void Postfix(SkillRecord __instance, ref int __result)
        {
            var pawn = __instance.Pawn;
            if (pawn == null) return;
            var tracker = pawn.GetCyberTracker();
            if (tracker == null || tracker.shards.Count == 0) return;
            __result += ShardUtility.SkillBonus(tracker, __instance.def);
        }
    }

    /// <summary>Removing the comms implant by force (surgery on a pawn that ignores the dependency check, a limb replacement) drops the shards with it.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    public static class Patch_RemoveHediff_DropShards
    {
        public static void Postfix(Pawn_HealthTracker __instance, Hediff hediff)
        {
            if (hediff?.def != RPDefOf.RPC_CommsImplant) return;
            var pawn = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn")(__instance);
            if (pawn != null && !pawn.Dead && !ShardUtility.HasShardPort(pawn)) ShardUtility.DropAll(pawn);
        }
    }

    // ------------------------------------------------------------------------------------------------ slotting job

    /// <summary>Walk to a shard on the map, spend a moment on the port, and it's slotted.</summary>
    public class JobDriver_SlotShard : JobDriver
    {
        private const int SlotTicks = 90;

        private Thing Shard => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(Shard, job, 1, 1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !ShardUtility.CanSlot(pawn, Shard.def, out _));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var wait = Toils_General.Wait(SlotTicks);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;
            var slot = ToilMaker.MakeToil("SlotShard");
            slot.initAction = () =>
            {
                var shard = Shard;
                if (shard == null || shard.Destroyed) return;
                var def = shard.def;
                shard.SplitOff(1).Destroy();
                ShardUtility.Slot(pawn, def);
            };
            slot.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return slot;
        }
    }

    // ------------------------------------------------------------------------------------------------ surgeries (forced insert / remove)

    /// <summary>Operation: puts a shard into any pawn with a shard port, willing or not. A soulkiller shard kills the patient.</summary>
    public class Recipe_InstallShard : Recipe_Surgery
    {
        private ThingDef Shard => recipe.GetModExtension<ShardRecipeExtension>()?.shard;

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part)) return false;
            return thing is Pawn pawn && Shard != null && ShardUtility.CanSlot(pawn, Shard, out _);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill)) return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }
            if (!ShardUtility.CanSlot(pawn, Shard, out string reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }
            ShardUtility.Slot(pawn, Shard, billDoer);
        }
    }

    /// <summary>Operation: takes a slotted shard out of any pawn.</summary>
    public class Recipe_RemoveShard : Recipe_Surgery
    {
        private ThingDef Shard => recipe.GetModExtension<ShardRecipeExtension>()?.shard;

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part)) return false;
            return thing is Pawn pawn && Shard != null && ShardUtility.Slotted(pawn).Contains(Shard);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill)) return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }
            ShardUtility.Unslot(pawn, Shard, billDoer?.Position ?? pawn.PositionHeld, billDoer?.Map ?? pawn.MapHeld);
        }
    }

    /// <summary>
    /// Generates the shard surgeries for every shard def, for every humanlike race (same pattern as cyberware,
    /// see <see cref="CyberwareRecipeGenerator"/>): "insert X" with the shard as its ingredient, "remove X".
    /// </summary>
    public static class ShardRecipeGenerator
    {
        public static IEnumerable<RecipeDef> ImpliedRecipeDefs()
        {
            var template = DefDatabase<RecipeDef>.GetNamedSilentFail("InstallBionicArm");
            var users = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.race != null && d.race.Humanlike).ToList();
            foreach (var shard in ShardUtility.AllShards.ToList())
            {
                var ext = shard.ShardExt();
                var insert = NewSurgery(template, users, "RPC_InsertShard_" + shard.defName, "RPC_InsertShardLabel".Translate(shard.label),
                    "RPC_InsertShardJob".Translate(shard.label), typeof(Recipe_InstallShard), shard);
                insert.isViolation = ext.soulkiller;
                var ing = new IngredientCount();
                ing.filter.SetAllow(shard, true);
                ing.SetBaseCount(1);
                insert.ingredients.Add(ing);
                insert.fixedIngredientFilter.SetAllow(shard, true);
                CyberwareRecipeGenerator.AddMedicine(insert, template, 1);
                insert.descriptionHyperlinks = new List<DefHyperlink> { new DefHyperlink(shard) };
                yield return insert;

                if (ext.soulkiller) continue; // never stored, nothing to take out
                var remove = NewSurgery(template, users, "RPC_RemoveShard_" + shard.defName, "RPC_RemoveShardLabel".Translate(shard.label),
                    "RPC_RemoveShardJob".Translate(shard.label), typeof(Recipe_RemoveShard), shard);
                remove.isViolation = true;
                CyberwareRecipeGenerator.AddMedicine(remove, template, 1);
                yield return remove;
            }

            // every engram (the soulkiller's generic one and each lore figure's) is an ingredient of the one operation
            var engramDefs = EngramUtility.AllEngramDefs.ToList();
            if (engramDefs.Count == 0) yield break;
            var engram = NewSurgery(template, users, "RPC_InsertEngram", "RPC_InsertEngramLabel".Translate(), "RPC_InsertEngramJob".Translate(),
                typeof(Recipe_InstallEngram), null);
            engram.workAmount = 3000f;
            engram.isViolation = true;
            var eing = new IngredientCount();
            foreach (var d in engramDefs)
            {
                eing.filter.SetAllow(d, true);
                engram.fixedIngredientFilter.SetAllow(d, true);
            }
            eing.SetBaseCount(1);
            engram.ingredients.Add(eing);
            CyberwareRecipeGenerator.AddMedicine(engram, template, 2);
            engram.descriptionHyperlinks = engramDefs.Select(d => new DefHyperlink(d)).ToList();
            yield return engram;
        }

        private static RecipeDef NewSurgery(RecipeDef template, List<ThingDef> users, string defName, string label, string jobString, System.Type worker, ThingDef shard)
        {
            var r = new RecipeDef
            {
                defName = defName,
                label = label,
                description = label,
                jobString = jobString,
                workerClass = worker,
                workAmount = 900f,
                recipeUsers = new List<ThingDef>(users),
                targetsBodyPart = false,
                hideBodyPartNames = true,
                anesthetize = true,
                modExtensions = new List<DefModExtension> { new ShardRecipeExtension { shard = shard } },
                modContentPack = RimPunkMod.ContentPack,
            };
            if (template != null)
            {
                r.effectWorking = template.effectWorking;
                r.soundWorking = template.soundWorking;
                r.workSpeedStat = template.workSpeedStat;
                r.workSkill = template.workSkill;
                r.workSkillLearnFactor = template.workSkillLearnFactor;
                r.surgeryOutcomeEffect = template.surgeryOutcomeEffect;
                r.surgerySuccessChanceFactor = 1.4f;
                r.deathOnFailedSurgeryChance = 0f;
                r.developmentalStageFilter = template.developmentalStageFilter;
            }
            return r;
        }
    }

    [HarmonyPatch(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PreResolve))]
    public static class Patch_DefGenerator_ShardRecipes
    {
        public static void Postfix(bool hotReload)
        {
            foreach (var def in ShardRecipeGenerator.ImpliedRecipeDefs())
                DefGenerator.AddImpliedDef(def, hotReload);
        }
    }

    /// <summary>Exotic traders stock skill and capacity shards (and, rarely, the soulkiller) through the usual trade tag.</summary>
    public class StockGenerator_Shard : StockGenerator
    {
        /// <summary>Chance the soulkiller shard is in a given stock.</summary>
        public float soulkillerChance = 0.12f;
        public const string Tag = "RPC_NeuralShard";

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            var pool = ShardUtility.AllShards.Where(d => !d.ShardExt().soulkiller).ToList();
            if (pool.Count == 0) yield break;
            int count = countRange.RandomInRange;
            for (int i = 0; i < count; i++)
                yield return ThingMaker.MakeThing(pool.RandomElement());
            var sk = ShardUtility.Soulkiller;
            if (sk != null && Rand.Chance(soulkillerChance)) yield return ThingMaker.MakeThing(sk);
        }

        public override bool HandlesThingDef(ThingDef thingDef) => thingDef.HasModExtension<ShardExtension>();
    }
}
