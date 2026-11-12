using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    public class EngramSkill : IExposable
    {
        public SkillDef skill;
        public int level;
        public Passion passion;
        public float xp;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref skill, "skill");
            Scribe_Values.Look(ref level, "level");
            Scribe_Values.Look(ref passion, "passion");
            Scribe_Values.Look(ref xp, "xp");
        }
    }

    public class EngramTrait : IExposable
    {
        public TraitDef def;
        public int degree;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref degree, "degree");
        }
    }

    /// <summary>A person on a shard: everything an engram writes over a pawn (name, backstory, traits, skills). Never appearance.</summary>
    public class EngramData : IExposable
    {
        public string first, nick, last;
        public BackstoryDef childhood, adulthood;
        public List<EngramTrait> traits = new List<EngramTrait>();
        public List<EngramSkill> skills = new List<EngramSkill>();

        /// <summary>The person came from the player's colony: their new body joins at once.</summary>
        public bool fromColony;

        /// <summary>Recruitment resistance and slave will the new body gets when arrested.</summary>
        public float resistance = 15f;
        public float will = 3f;

        /// <summary>Lore figure's def, if any (for the log and tests).</summary>
        public EngramPersonDef source;

        public string FullName => new NameTriple(first ?? "", string.IsNullOrEmpty(nick) ? first : nick, last ?? "").ToStringFull;

        public void ExposeData()
        {
            Scribe_Values.Look(ref first, "first");
            Scribe_Values.Look(ref nick, "nick");
            Scribe_Values.Look(ref last, "last");
            Scribe_Defs.Look(ref childhood, "childhood");
            Scribe_Defs.Look(ref adulthood, "adulthood");
            Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);
            Scribe_Collections.Look(ref skills, "skills", LookMode.Deep);
            Scribe_Values.Look(ref fromColony, "fromColony");
            Scribe_Values.Look(ref resistance, "resistance", 15f);
            Scribe_Values.Look(ref will, "will", 3f);
            Scribe_Defs.Look(ref source, "source");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                traits ??= new List<EngramTrait>();
                skills ??= new List<EngramSkill>();
                traits.RemoveAll(t => t?.def == null);
                skills.RemoveAll(s => s?.skill == null);
            }
        }

        public EngramData Clone()
        {
            var copy = (EngramData)MemberwiseClone();
            copy.traits = traits.Select(t => new EngramTrait { def = t.def, degree = t.degree }).ToList();
            copy.skills = skills.Select(s => new EngramSkill { skill = s.skill, level = s.level, passion = s.passion, xp = s.xp }).ToList();
            return copy;
        }

        /// <summary>What a soulkiller shard takes from <paramref name="pawn"/>. The capacity aptitude stays with the body.</summary>
        public static EngramData FromPawn(Pawn pawn)
        {
            var d = new EngramData();
            if (pawn.Name is NameTriple n)
            {
                d.first = n.First;
                d.nick = n.Nick;
                d.last = n.Last;
            }
            else
            {
                d.first = pawn.Name?.ToStringShort ?? pawn.LabelShort;
                d.nick = d.first;
                d.last = "";
            }
            d.childhood = pawn.story?.Childhood;
            d.adulthood = pawn.story?.Adulthood;
            if (pawn.story?.traits != null)
                foreach (var t in pawn.story.traits.allTraits)
                    if (t.def != RPDefOf.RPC_CyberAptitude && t.sourceGene == null)
                        d.traits.Add(new EngramTrait { def = t.def, degree = t.Degree });
            if (pawn.skills != null)
                foreach (var s in pawn.skills.skills)
                    d.skills.Add(new EngramSkill { skill = s.def, level = s.levelInt, passion = s.passion, xp = s.xpSinceLastLevel });
            d.fromColony = pawn.Faction != null && pawn.Faction.IsPlayer;
            var range = pawn.kindDef?.initialResistanceRange;
            d.resistance = range.HasValue ? range.Value.Average : 15f;
            var will = pawn.kindDef?.initialWillRange;
            d.will = will.HasValue ? will.Value.Average : 3f;
            return d;
        }
    }

    /// <summary>
    /// A lore figure on an engram (Defs/Shards/Engrams.xml). Skills, traits and backstory describe who they were; resistance and
    /// will are how hard they are to bring round (recruit resistance 10-26 is a normal prisoner, will 1-4).
    /// </summary>
    public class EngramPersonDef : Def
    {
        public string firstName, nickName, lastName;
        public BackstoryDef childhood, adulthood;
        public List<EngramTrait> traits = new List<EngramTrait>();
        public List<EngramSkill> skills = new List<EngramSkill>();
        public float resistance = 15f;
        public float will = 3f;
        public bool fromColony;

        public EngramData ToData() => new EngramData
        {
            first = firstName,
            nick = string.IsNullOrEmpty(nickName) ? firstName : nickName,
            last = lastName ?? "",
            childhood = childhood,
            adulthood = adulthood,
            traits = traits.Select(t => new EngramTrait { def = t.def, degree = t.degree }).ToList(),
            skills = skills.Select(s => new EngramSkill { skill = s.skill, level = s.level, passion = s.passion, xp = s.xp }).ToList(),
            fromColony = fromColony,
            resistance = resistance,
            will = will,
            source = this,
        };
    }

    public class CompProperties_Engram : CompProperties
    {
        /// <summary>Set for the lore engrams; the soulkiller's generic engram carries its own data.</summary>
        public EngramPersonDef person;
        public CompProperties_Engram() => compClass = typeof(CompEngram);
    }

    public class CompEngram : ThingComp
    {
        private EngramData data;

        public CompProperties_Engram Props => (CompProperties_Engram)props;

        public EngramData Data
        {
            get => data ??= Props.person?.ToData();
            set => data = value;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // lore engrams rebuild their person from the def; a soulkiller's engram only exists in this save
            Scribe_Deep.Look(ref data, "data");
        }

        public override string TransformLabel(string label) => Data == null ? label : label + ": " + Data.FullName;

        public override string CompInspectStringExtra() => Data == null ? null : Summary(Data, false);

        public override string GetDescriptionPart() => Data == null ? null : Summary(Data, true);

        public static string Summary(EngramData d, bool full)
        {
            var sb = new StringBuilder();
            sb.Append(d.FullName);
            var back = new[] { d.childhood?.title, d.adulthood?.title }.Where(s => !s.NullOrEmpty());
            if (back.Any()) sb.Append(" - " + string.Join(", ", back));
            if (!full) return sb.ToString();
            sb.AppendLine();
            var top = d.skills.Where(s => s.level > 0).OrderByDescending(s => s.level).Take(4)
                .Select(s => $"{s.skill.skillLabel} {s.level}" + (s.passion == Passion.Major ? "!!" : s.passion == Passion.Minor ? "!" : ""));
            sb.AppendLine("RPC_Engram_Skills".Translate(string.Join(", ", top)));
            if (d.traits.Count > 0)
                sb.AppendLine("RPC_Engram_Traits".Translate(string.Join(", ", d.traits.Select(t => t.def.DataAtDegree(t.degree).LabelCap.ToString()))));
            sb.Append((d.fromColony ? "RPC_Engram_FromColony" : "RPC_Engram_Stranger").Translate());
            return sb.ToString();
        }
    }

    /// <summary>Exotic traders now and then carry one lore figure's engram. Quest rewards use the defs' own commonality (thingSetMakerTags).</summary>
    public class StockGenerator_Engram : StockGenerator
    {
        public float chance = 0.12f;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (!Rand.Chance(chance)) yield break;
            var pool = EngramUtility.LoreEngramDefs.ToList();
            if (pool.Count > 0) yield return ThingMaker.MakeThing(pool.RandomElement());
        }

        public override bool HandlesThingDef(ThingDef thingDef) => thingDef.GetCompProperties<CompProperties_Engram>()?.person != null;
    }

    public static class EngramUtility
    {
        /// <summary>How long a freshly written body stays down.</summary>
        public const int ShockTicks = 30000; // 12 hours

        public static ThingDef GenericEngram => DefDatabase<ThingDef>.GetNamedSilentFail("RPC_Engram");

        public static IEnumerable<ThingDef> AllEngramDefs =>
            DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.GetCompProperties<CompProperties_Engram>() != null);

        public static IEnumerable<ThingDef> LoreEngramDefs =>
            DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.GetCompProperties<CompProperties_Engram>()?.person != null);

        public static Thing MakeEngram(EngramData data)
        {
            var thing = ThingMaker.MakeThing(GenericEngram);
            thing.TryGetComp<CompEngram>().Data = data;
            return thing;
        }

        /// <summary>The soulkiller: kills <paramref name="pawn"/> and leaves an engram of them where they fell.</summary>
        public static void Soulkill(Pawn pawn, Pawn surgeon)
        {
            if (pawn == null || pawn.Dead) return;
            var data = EngramData.FromPawn(pawn);
            var map = pawn.MapHeld;
            var pos = pawn.PositionHeld;
            var engram = MakeEngram(data);
            Diag.Event("engram", $"{Diag.Describe(pawn)} soulkilled into an engram of {data.FullName} (colony {data.fromColony})");
            pawn.Kill(null);
            if (map != null && pos.IsValid)
            {
                GenPlace.TryPlaceThing(engram, pos, map, ThingPlaceMode.Near);
                Messages.Message("RPC_Soulkilled".Translate(data.FullName), new TargetInfo(pos, map), MessageTypeDefOf.NegativeEvent);
            }
            else if (!engram.Destroyed)
            {
                // nowhere to put it (caravan): drop it with the body so it isn't lost
                pawn.Corpse?.GetDirectlyHeldThings()?.TryAdd(engram);
            }
        }

        public static bool CanReceive(Pawn pawn, out string reason)
        {
            reason = null;
            if (pawn == null || pawn.Dead || pawn.RaceProps?.Humanlike != true || pawn.story == null || pawn.skills == null)
            {
                reason = "RPC_Engram_NotHumanlike".Translate();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Overwrites who the pawn is (name, backstory, traits, skills) but never how they look. The new person is down for 12 hours.
        /// They join the colony straight away only if the engram came from the player's colony; otherwise a colonist is no longer ours,
        /// a prisoner stays a prisoner, and anyone can be arrested and recruited with the engram's own resistance and will.
        /// </summary>
        public static void Apply(Pawn pawn, EngramData data, Pawn surgeon = null)
        {
            if (!CanReceive(pawn, out _) || data == null) return;
            var oldName = pawn.LabelShort;

            pawn.Name = new NameTriple(data.first ?? "", string.IsNullOrEmpty(data.nick) ? data.first : data.nick, data.last ?? "");
            if (data.childhood != null && !(pawn.DevelopmentalStage.Adult() && data.childhood.spawnCategories.Contains("Child"))) pawn.story.Childhood = data.childhood;
            if (data.adulthood != null) pawn.story.Adulthood = data.adulthood;

            var traits = pawn.story.traits;
            foreach (var t in traits.allTraits.ToList())
                if (t.def != RPDefOf.RPC_CyberAptitude && t.sourceGene == null && !t.ScenForced) traits.RemoveTrait(t);
            foreach (var et in data.traits)
                if (et.def != null && !traits.HasTrait(et.def)) traits.GainTrait(new Trait(et.def, et.degree));

            foreach (var rec in pawn.skills.skills)
            {
                var e = data.skills.Find(s => s.skill == rec.def);
                rec.Level = e?.level ?? 0;
                rec.passion = e?.passion ?? Passion.None;
                rec.xpSinceLastLevel = Mathf.Clamp(e?.xp ?? 0f, 0f, Mathf.Max(0f, rec.XpRequiredForLevelUp - 1f));
                rec.xpSinceMidnight = 0f;
            }
            pawn.skills.Notify_SkillDisablesChanged();
            pawn.skills.DirtyAptitudes();
            pawn.Notify_DisabledWorkTypesChanged();

            var tracker = pawn.GetCyberTracker();
            if (tracker != null)
            {
                tracker.engramResistance = data.resistance;
                tracker.engramWill = data.will;
            }

            // the new person wakes up in someone else's body: down for 12 hours
            pawn.jobs?.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
            if (pawn.Drafted) pawn.drafter.Drafted = false;
            var shock = pawn.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_EngramShock);
            if (shock != null) pawn.health.RemoveHediff(shock);
            pawn.health.AddHediff(RPDefOf.RPC_EngramShock);

            if (data.fromColony)
            {
                if (pawn.Faction != Faction.OfPlayer || pawn.IsPrisoner) RecruitUtility.Recruit(pawn, Faction.OfPlayer, surgeon);
            }
            else if (pawn.IsPrisonerOfColony)
            {
                pawn.guest.resistance = data.resistance;
                pawn.guest.will = data.will;
            }
            else if (pawn.Faction == Faction.OfPlayer && !pawn.IsSlave)
            {
                pawn.SetFaction(null);
            }

            Diag.Event("engram", $"{oldName}#{pawn.thingIDNumber} is now {data.FullName} (colony {data.fromColony}, resistance {data.resistance}, will {data.will})");
            Messages.Message("RPC_EngramWritten".Translate(oldName, data.FullName), pawn, MessageTypeDefOf.NeutralEvent);
        }
    }

    /// <summary>Operation: writes an engram over the patient. The engram is consumed.</summary>
    public class Recipe_InstallEngram : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part)) return false;
            return thing is Pawn pawn && EngramUtility.CanReceive(pawn, out _);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill)) return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }
            var comp = ingredients?.Select(t => t.TryGetComp<CompEngram>()).FirstOrDefault(c => c != null);
            if (comp?.Data == null) return;
            EngramUtility.Apply(pawn, comp.Data.Clone(), billDoer);
        }
    }

    /// <summary>
    /// SetGuestStatus rolls resistance and will from the pawn kind. An engram's person has their own, so overwrite what it rolled
    /// (the engram holder's own values stay in the tracker).
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class Patch_GuestStatus_EngramResistance
    {
        private static readonly AccessTools.FieldRef<Pawn_GuestTracker, Pawn> PawnRef = AccessTools.FieldRefAccess<Pawn_GuestTracker, Pawn>("pawn");

        public static void Postfix(Pawn_GuestTracker __instance, GuestStatus guestStatus)
        {
            if (guestStatus != GuestStatus.Prisoner) return;
            var tracker = PawnRef(__instance)?.GetCyberTracker();
            if (tracker == null || tracker.engramResistance < 0f) return;
            __instance.resistance = tracker.engramResistance;
            if (tracker.engramWill >= 0f) __instance.will = tracker.engramWill;
        }
    }
}
