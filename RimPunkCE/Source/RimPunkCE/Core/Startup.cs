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
            CyberwareDefCache.Build();
            InjectTrackerIntoHumanlikes();

            if (MP.enabled) MP.RegisterAll(Assembly.GetExecutingAssembly());

            RPLog.Debug($"loaded: {CyberwareDefCache.AllCyberware.Count} cyberware defs");
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
