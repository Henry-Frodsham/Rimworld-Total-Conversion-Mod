using System.Collections.Generic;
using System.Linq;
using RimWorld;
using HarmonyLib;
using Verse;

namespace RimPunkCE
{
    public static class CyberSurgeryUtility
    {
        public static void SpawnItemFor(Hediff_Cyberware hediff, IntVec3 pos, Map map)
        {
            var item = hediff.Ext?.item;
            if (item == null || map == null) return;
            var thing = ThingMaker.MakeThing(item);
            thing.TryGetComp<CompCyberwareTier>()?.SetTier(hediff.tier);
            GenSpawn.Spawn(thing, pos, map);
        }

        public static CyberTier TierFromIngredients(List<Thing> ingredients, ThingDef item, CyberTier fallback)
        {
            if (ingredients != null)
                foreach (var t in ingredients)
                    if (t.def == item && t.TryGetComp<CompCyberwareTier>() is CompCyberwareTier c)
                        return c.tier;
            return fallback;
        }

        public static IEnumerable<BodyPartRecord> CandidateParts(Pawn pawn, CyberSlotDef slot)
        {
            if (slot == null || slot.WholeBody) yield break;
            foreach (var part in pawn.health.hediffSet.GetNotMissingParts())
                if (slot.bodyParts.Contains(part.def)) yield return part;
        }

        /// <summary>"(usage 64% -> 82%)" suffix so the player sees the cyberpsychosis risk before installing.</summary>
        public static string UsageSuffix(Pawn pawn, float addedCost)
        {
            var tracker = pawn.GetCyberTracker();
            if (tracker == null) return "";
            float max = tracker.CapacityMax;
            float before = tracker.CapacityUsed / max;
            float after = (tracker.CapacityUsed + addedCost) / max;
            string s = " (" + "RPC_UsageChange".Translate(before.ToStringPercent(), after.ToStringPercent()) + ")";
            float threshold = CyberpsychosisUtility.HasBurnout(pawn) ? RPDefOf.RPC_CyberpsychosisTuning.safeUsage : RPDefOf.RPC_CyberpsychosisTuning.triggerUsage;
            if (after >= threshold) s += " " + "RPC_CyberpsychosisRisk".Translate();
            return s;
        }
    }

    /// <summary>
    /// Installs cyberware. Unlike vanilla Recipe_InstallImplant it allows parts that already carry added parts
    /// (a bionic arm doesn't block arm cyberware), enforces CyberSlotDef rules, supports whole-body systems
    /// (no part), and keeps the item's tier.
    /// </summary>
    public class Recipe_InstallCyberware : Recipe_Surgery
    {
        private CyberwareExtension Ext => recipe.addsHediff.Ext();

        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            var ext = recipe.addsHediff.Ext();
            foreach (var part in CyberSurgeryUtility.CandidateParts(pawn, ext?.slot))
                if (CyberUtility.CanInstall(pawn, recipe.addsHediff, part, out _))
                    yield return part;
        }

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part)) return false;
            if (thing is not Pawn pawn || pawn.GetCyberTracker() == null) return false;
            if (!recipe.targetsBodyPart) return CyberUtility.CanInstall(pawn, recipe.addsHediff, null, out _);
            return true;
        }

        public override string GetLabelWhenUsedOn(Pawn pawn, BodyPartRecord part) =>
            base.GetLabelWhenUsedOn(pawn, part) + CyberSurgeryUtility.UsageSuffix(pawn, Ext?.capacityCost ?? 0f);

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill)) return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }
            if (!CyberUtility.CanInstall(pawn, recipe.addsHediff, part, out string reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }
            var ext = Ext;
            var hediff = (Hediff_Cyberware)HediffMaker.MakeHediff(recipe.addsHediff, pawn, part);
            hediff.tier = CyberSurgeryUtility.TierFromIngredients(ingredients, ext?.item, ext?.minTier ?? CyberTier.T1);
            pawn.health.AddHediff(hediff, part);
            CyberpsychosisUtility.Notify_Event(pawn, CyberpsychosisEvent.CyberwareInstalled);
        }
    }

    /// <summary>Removes cyberware and drops the item with its tier. The way to end cyberpsychosis.</summary>
    public class Recipe_RemoveCyberware : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            foreach (var c in pawn.InstalledCyberware())
                if (c.def == recipe.removesHediff && c.Part != null)
                    yield return c.Part;
        }

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part)) return false;
            if (thing is not Pawn pawn) return false;
            if (!recipe.targetsBodyPart) return pawn.health.hediffSet.HasHediff(recipe.removesHediff);
            return true;
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill)) return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }
            var hediff = pawn.InstalledCyberware().FirstOrDefault(c => c.def == recipe.removesHediff && c.Part == part);
            if (hediff == null) return;
            CyberSurgeryUtility.SpawnItemFor(hediff, billDoer?.Position ?? pawn.PositionHeld, billDoer?.Map ?? pawn.MapHeld);
            pawn.health.RemoveHediff(hediff);
        }
    }

    /// <summary>
    /// Raises installed cyberware from <see cref="UpgradeRecipeExtension.fromTier"/> to the next tier.
    /// One recipe per item per tier step, generated by <see cref="CyberwareRecipeGenerator"/>.
    /// </summary>
    public class Recipe_UpgradeCyberware : Recipe_Surgery
    {
        private UpgradeRecipeExtension Up => recipe.GetModExtension<UpgradeRecipeExtension>();

        private bool Matches(Hediff_Cyberware c) => c.def == Up.cyberware && c.tier == Up.fromTier;

        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            foreach (var c in pawn.InstalledCyberware())
                if (Matches(c) && c.Part != null) yield return c.Part;
        }

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part)) return false;
            if (thing is not Pawn pawn) return false;
            if (!recipe.targetsBodyPart) return pawn.InstalledCyberware().Any(Matches);
            return true;
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill)) return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }
            var hediff = pawn.InstalledCyberware().FirstOrDefault(c => Matches(c) && c.Part == part);
            hediff?.SetTier(Up.fromTier + 1);
        }
    }

    public class UpgradeRecipeExtension : DefModExtension
    {
        public HediffDef cyberware;
        public CyberTier fromTier;
    }

    /// <summary>
    /// Generates install / remove / upgrade surgeries for every HediffDef with a CyberwareExtension
    /// (runs in DefGenerator.GenerateImpliedDefs_PreResolve). Shared surgery settings are copied from
    /// vanilla's InstallBionicArm so they follow balance changes in the base game.
    /// Upgrade cost: step k (1-based) = the item's costList x 2^(k-1) (first upgrade = production cost).
    /// </summary>
    public static class CyberwareRecipeGenerator
    {
        public static string ResearchDefNameFor(CyberTier tier) => "RPC_Cyberware_" + tier;

        public static IEnumerable<RecipeDef> ImpliedRecipeDefs()
        {
            var template = DefDatabase<RecipeDef>.GetNamedSilentFail("InstallBionicArm");
            var users = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.race != null && d.race.Humanlike).ToList();

            foreach (var hd in DefDatabase<HediffDef>.AllDefsListForReading.ToList())
            {
                var ext = hd.GetModExtension<CyberwareExtension>();
                if (ext?.item == null || ext.slot == null) continue;
                bool wholeBody = ext.slot.WholeBody;

                var install = NewSurgery(template, users, ext, wholeBody, "RPC_Install_" + hd.defName,
                    "RPC_InstallLabel".Translate(hd.label), "RPC_InstallJob".Translate(hd.label), typeof(Recipe_InstallCyberware));
                install.addsHediff = hd;
                AddIngredient(install, ext.item, 1);
                AddMedicine(install, template);
                install.descriptionHyperlinks = new List<DefHyperlink> { new DefHyperlink(ext.item), new DefHyperlink(hd) };
                yield return install;

                var remove = NewSurgery(template, users, ext, wholeBody, "RPC_Remove_" + hd.defName,
                    "RPC_RemoveLabel".Translate(hd.label), "RPC_RemoveJob".Translate(hd.label), typeof(Recipe_RemoveCyberware));
                remove.removesHediff = hd;
                remove.isViolation = true;
                AddMedicine(remove, template);
                yield return remove;

                for (var from = ext.minTier; from < ext.maxTier; from++)
                {
                    var to = from + 1;
                    int step = to - ext.minTier;
                    var up = NewSurgery(template, users, ext, wholeBody, "RPC_Upgrade_" + hd.defName + "_" + to,
                        "RPC_UpgradeLabel".Translate(hd.label, to.Label()), "RPC_UpgradeJob".Translate(hd.label), typeof(Recipe_UpgradeCyberware));
                    up.researchPrerequisite = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(ResearchDefNameFor(to));
                    up.modExtensions = new List<DefModExtension> { new UpgradeRecipeExtension { cyberware = hd, fromTier = from } };
                    int mult = 1 << (step - 1);
                    if (ext.item.costList != null)
                        foreach (var cost in ext.item.costList)
                            AddIngredient(up, cost.thingDef, cost.count * mult);
                    yield return up;
                }
            }
        }

        private static RecipeDef NewSurgery(RecipeDef template, List<ThingDef> users, CyberwareExtension ext, bool wholeBody,
            string defName, string label, string jobString, System.Type worker)
        {
            var r = new RecipeDef
            {
                defName = defName,
                label = label,
                description = label,
                jobString = jobString,
                workerClass = worker,
                workAmount = ext.surgeryWorkAmount,
                recipeUsers = new List<ThingDef>(users),
                targetsBodyPart = !wholeBody,
                appliedOnFixedBodyParts = wholeBody ? new List<BodyPartDef>() : new List<BodyPartDef>(ext.slot.bodyParts),
                anesthetize = true,
                modContentPack = RimPunkMod.ContentPack,
            };
            if (template != null)
            {
                r.effectWorking = template.effectWorking;
                r.soundWorking = template.soundWorking;
                r.workSpeedStat = template.workSpeedStat;
                r.workSkill = template.workSkill;
                r.surgerySuccessChanceFactor = template.surgerySuccessChanceFactor;
                r.deathOnFailedSurgeryChance = template.deathOnFailedSurgeryChance;
                r.developmentalStageFilter = template.developmentalStageFilter;
                r.skillRequirements = template.skillRequirements?.Select(s => new SkillRequirement { skill = s.skill, minLevel = s.minLevel }).ToList();
            }
            return r;
        }

        private static void AddIngredient(RecipeDef r, ThingDef def, int count)
        {
            if (def == null || count <= 0) return;
            var ing = new IngredientCount();
            ing.filter.SetAllow(def, true);
            ing.SetBaseCount(count);
            r.ingredients.Add(ing);
            r.fixedIngredientFilter.SetAllow(def, true);
        }

        /// <summary>Vanilla surgeries take 2 medicine. Category filters are resolved later (ThingFilter.ResolveReferences),
        /// so fill the raw category list instead of SetAllow, which needs the resolved category tree.</summary>
        private static void AddMedicine(RecipeDef r, RecipeDef template)
        {
            var ing = new IngredientCount();
            AllowCategoryDeferred(ing.filter, "Medicine");
            ing.SetBaseCount(2);
            r.ingredients.Add(ing);
            AllowCategoryDeferred(r.fixedIngredientFilter, "Medicine");
        }

        private static readonly AccessTools.FieldRef<ThingFilter, List<string>> CategoriesRef =
            AccessTools.FieldRefAccess<ThingFilter, List<string>>("categories");

        private static void AllowCategoryDeferred(ThingFilter filter, string category)
        {
            var list = CategoriesRef(filter) ??= new List<string>();
            if (!list.Contains(category)) list.Add(category);
        }
    }
}
