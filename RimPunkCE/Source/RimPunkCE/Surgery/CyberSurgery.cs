using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            QualityBionicsCompat.SetQuality(thing, hediff.Quality);
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

        /// <summary>Quality of the installed item (Normal without Quality Bionics Remastered).</summary>
        public static QualityCategory QualityFromIngredients(List<Thing> ingredients, ThingDef item)
        {
            if (ingredients != null)
                foreach (var t in ingredients)
                    if (t.def == item) return QualityBionicsCompat.QualityOf(t);
            return QualityCategory.Normal;
        }

        /// <summary>
        /// Parts a slot's cyberware goes on. A part replaced by a bionic limb higher up (vanilla marks the arm and hand
        /// under a bionic arm on the shoulder as missing) maps to that limb's part, see <see cref="EffectivePart"/>.
        /// </summary>
        public static IEnumerable<BodyPartRecord> CandidateParts(Pawn pawn, CyberSlotDef slot)
        {
            if (slot == null || slot.WholeBody) yield break;
            var seen = new HashSet<BodyPartRecord>();
            foreach (var part in pawn.RaceProps.body.AllParts)
            {
                if (!slot.bodyParts.Contains(part.def)) continue;
                var ep = EffectivePart(pawn, part);
                if (ep != null && seen.Add(ep)) yield return ep;
            }
        }

        /// <summary>
        /// Where cyberware for this (natural) part sits: the part itself, or, if it's only "missing" because a bionic
        /// limb was installed on an ancestor, that limb's part (vanilla won't add hediffs to missing parts). Null if the
        /// part is really gone.
        /// </summary>
        public static BodyPartRecord EffectivePart(Pawn pawn, BodyPartRecord part)
        {
            var set = pawn?.health?.hediffSet;
            if (set == null || part == null) return null;
            if (!set.PartIsMissing(part)) return part;
            for (var x = part.parent; x != null; x = x.parent)
            {
                if (set.PartIsMissing(x)) continue;
                return set.HasDirectlyAddedPartFor(x) ? x : null;
            }
            return null;
        }

        /// <summary>The slot's own part for installed cyberware (its part, or the arm / hand below the limb it was moved to).</summary>
        public static BodyPartRecord HomePart(Hediff_Cyberware c)
        {
            var slot = c.Ext?.slot;
            var part = c.Part;
            if (slot == null || part == null || slot.bodyParts.Contains(part.def)) return part;
            var stack = new Stack<BodyPartRecord>(part.parts);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (slot.bodyParts.Contains(d.def)) return d;
                foreach (var child in d.parts) stack.Push(child);
            }
            return part;
        }

        public static bool IsUnder(BodyPartRecord child, BodyPartRecord ancestor)
        {
            for (var x = child; x != null; x = x.parent)
                if (x == ancestor) return true;
            return false;
        }

        /// <summary>"(usage 64% -> 82%)" suffix so the player sees the capacity cost before installing.</summary>
        public static string UsageSuffix(Pawn pawn, float addedCost)
        {
            var tracker = pawn.GetCyberTracker();
            if (tracker == null) return "";
            float max = tracker.CapacityMax;
            float before = tracker.CapacityUsed / max;
            float after = (tracker.CapacityUsed + addedCost) / max;
            return " (" + "RPC_UsageChange".Translate(before.ToStringPercent(), after.ToStringPercent()) + ")";
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
            hediff.Quality = CyberSurgeryUtility.QualityFromIngredients(ingredients, ext?.item);
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
            // the comms implant stays until the basic implants that need it are out
            if (CyberUtility.DependentOn(pawn, recipe.removesHediff) != null) return false;
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

    /// <summary>
    /// MedicalRecipesUtility.RestorePartAndSpawnAllPreviousParts (replacing a limb with a bionic one, removing a part):
    /// vanilla wipes every hediff on the part and below, so arm / hand / leg cyberware there would vanish. Drop each as
    /// its item (keeping the tier) first.
    /// </summary>
    [HarmonyPatch(typeof(MedicalRecipesUtility), nameof(MedicalRecipesUtility.RestorePartAndSpawnAllPreviousParts))]
    public static class Patch_RestorePart_DropCyberware
    {
        public static void Prefix(Pawn pawn, BodyPartRecord part, IntVec3 pos, Map map)
        {
            if (pawn?.health?.hediffSet == null || part == null) return;
            var under = pawn.InstalledCyberware().Where(c => c.Part != null && CyberSurgeryUtility.IsUnder(c.Part, part)).ToList();
            foreach (var c in under)
            {
                CyberSurgeryUtility.SpawnItemFor(c, pos, map);
                pawn.health.RemoveHediff(c);
                Diag.Event("cyberware", $"{Diag.Describe(pawn)}: {c.def.defName} {c.tier} dropped, its limb was replaced");
            }
        }

    }

    /// <summary>
    /// Installing a limb (bionic or natural) keeps the cyberware on it. Vanilla wipes every hediff on the part and
    /// below (RestorePart, then Hediff_AddedPart.PostAdd marks the children missing), so the cyberware is detached
    /// from the hediff list for the surgery (no PostRemoved: launcher ammo, tiers and comp state survive) and put back
    /// afterwards on its <see cref="CyberSurgeryUtility.EffectivePart"/>: a bionic arm on the shoulder carries the
    /// arm and hand cyberware, a natural arm put back takes it home again. Only cyberware whose part is really gone
    /// (failed surgery) drops as its item.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_LimbInstall_KeepCyberware
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Recipe_InstallArtificialBodyPart), nameof(Recipe_InstallArtificialBodyPart.ApplyOnPawn));
            yield return AccessTools.Method(typeof(Recipe_InstallNaturalBodyPart), nameof(Recipe_InstallNaturalBodyPart.ApplyOnPawn));
        }

        public static void Prefix(Pawn pawn, BodyPartRecord part, out List<KeyValuePair<Hediff_Cyberware, BodyPartRecord>> __state)
        {
            __state = null;
            var set = pawn?.health?.hediffSet;
            if (set == null || part == null) return;
            foreach (var c in pawn.InstalledCyberware().ToList())
            {
                if (c.Part == null || !CyberSurgeryUtility.IsUnder(c.Part, part)) continue;
                (__state ??= new List<KeyValuePair<Hediff_Cyberware, BodyPartRecord>>()).Add(new KeyValuePair<Hediff_Cyberware, BodyPartRecord>(c, CyberSurgeryUtility.HomePart(c)));
                set.hediffs.Remove(c);
            }
            if (__state != null) set.DirtyCache();
        }

        public static void Finalizer(Pawn pawn, BodyPartRecord part, List<KeyValuePair<Hediff_Cyberware, BodyPartRecord>> __state)
        {
            if (__state == null || pawn?.health?.hediffSet == null) return;
            var set = pawn.health.hediffSet;
            var kept = new List<string>();
            foreach (var kv in __state)
            {
                var c = kv.Key;
                var target = CyberSurgeryUtility.EffectivePart(pawn, kv.Value);
                if (target != null)
                {
                    c.Part = target;
                    set.hediffs.Add(c);
                    kept.Add($"{c.def.defName} {c.tier} on {target.Label}");
                }
                else
                {
                    CyberSurgeryUtility.SpawnItemFor(c, pawn.PositionHeld, pawn.MapHeld);
                    c.PostRemoved();
                    Diag.Event("cyberware", $"{Diag.Describe(pawn)}: {c.def.defName} {c.tier} dropped, {kv.Value?.Label} is gone");
                }
            }
            set.DirtyCache();
            if (!pawn.Dead) pawn.health.CheckForStateChange(null, null);
            pawn.GetCyberTracker()?.Notify_CyberwareChanged();
            if (kept.Count > 0) Diag.Event("cyberware", $"{Diag.Describe(pawn)}: limb installed on {part?.Label}, kept {string.Join(", ", kept)}");
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
        internal static void AddMedicine(RecipeDef r, RecipeDef template, int count = 2)
        {
            var ing = new IngredientCount();
            AllowCategoryDeferred(ing.filter, "Medicine");
            ing.SetBaseCount(count);
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
