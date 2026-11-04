using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>Runs once after all defs are loaded: tracker injection, caches, multiplayer. Harmony is applied earlier, in the Mod constructor.</summary>
    [StaticConstructorOnStartup]
    public static class Startup
    {
        public const string HarmonyId = "mackos.bakos.rimpunkce";

        static Startup()
        {
            Diag.Start();
            CyberwareDefCache.Build();
            InjectTrackerIntoHumanlikes();
            TextureOverrides.ApplyAll();
            if (RimPunkMod.Settings.cyberpunkFactionsDefault) MakeCyberpunkFactionsDefault();

            if (MP.enabled) MP.RegisterAll(Assembly.GetExecutingAssembly());

            RPLog.Debug($"loaded: {CyberwareDefCache.AllCyberware.Count} cyberware defs");
            StartupChecks();
        }

        /// <summary>Content sanity checks written to diag.log (CE conversion, pawn kind weapons, ...).</summary>
        private static void StartupChecks()
        {
            if (!Diag.Enabled) return;
            var guns = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_Gun_")).ToList();
            var melee = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_Melee_")).ToList();
            var factions = DefDatabase<FactionDef>.AllDefsListForReading.Where(d => d.HasModExtension<CyberFactionExtension>()).ToList();
            Diag.Event("startup", $"{CyberwareDefCache.AllCyberware.Count} cyberware, {guns.Count} guns, {melee.Count} melee, {factions.Count} factions, " +
                                  $"{DefDatabase<RecipeDef>.AllDefsListForReading.Count(r => r.defName.StartsWith("RPC_Install_"))} install surgeries, " +
                                  $"{DefDatabase<RecipeDef>.AllDefsListForReading.Count(r => r.defName.StartsWith("RPC_Upgrade_"))} upgrade surgeries");
            foreach (var g in guns)
            {
                if (g.GetCompProperties<CombatExtended.CompProperties_AmmoUser>() == null) Diag.Check("startup", $"{g.defName}: no CE ammo user (CE patch didn't apply)");
                if (!(g.Verbs.FirstOrDefault() is CombatExtended.VerbPropertiesCE)) Diag.Check("startup", $"{g.defName}: verb is not CE ({g.Verbs.FirstOrDefault()?.GetType().Name})");
            }
            var allTags = new HashSet<string>(DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.weaponTags != null).SelectMany(d => d.weaponTags));
            foreach (var k in DefDatabase<PawnKindDef>.AllDefsListForReading.Where(k => k.defName.StartsWith("RPC_")))
            {
                if (k.weaponTags != null && !k.weaponTags.Any(allTags.Contains)) Diag.Check("startup", $"{k.defName}: no weapon has any of its tags ({string.Join(",", k.weaponTags)})");
                if (k.defaultFactionDef == null && k.defName != "RPC_RogueCyberpsycho") Diag.Check("startup", $"{k.defName}: no default faction");
            }
            Diag.Flush();
        }

        /// <summary>
        /// Corps and gangs are the default world factions; vanilla / DLC humanlike factions stay available in the
        /// world-generation faction list but start unselected (prompts/factions.md).
        /// </summary>
        private static void MakeCyberpunkFactionsDefault()
        {
            foreach (var def in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                if (def.isPlayer || def.hidden || !def.humanlikeFaction || def.maxConfigurableAtWorldCreation <= 0) continue;
                if (def.HasModExtension<CyberFactionExtension>()) continue;
                if (def == FactionDefOf.Empire) continue; // stays on: reskinned as the NUSA (Patches/Empire_NUSA.xml), royalty content needs it
                def.startingCountAtWorldCreation = 0;
                def.requiredCountAtGameStart = 0;
            }
        }

        /// <summary>Adds CompCyberTracker to every humanlike race, including other mods' races.</summary>
        private static void InjectTrackerIntoHumanlikes()
        {
            int count = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race == null || !def.race.Humanlike) continue;
                def.comps ??= new System.Collections.Generic.List<CompProperties>();
                if (def.comps.Any(c => c is CompProperties_CyberTracker)) continue;
                def.comps.Add(new CompProperties_CyberTracker());
                count++;
            }
            RPLog.Debug($"cyber tracker added to {count} humanlike races");
        }
    }
}
