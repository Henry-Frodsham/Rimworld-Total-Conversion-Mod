using System.Linq;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>Applied after pawn generation: faction cyberware loadouts and street names.</summary>
    public static class PawnLoadoutUtility
    {
        public static void OnPawnGenerated(Pawn pawn)
        {
            if (pawn?.RaceProps?.Humanlike != true) return;
            var loadout = pawn.kindDef?.GetModExtension<CyberwareLoadoutExtension>();
            if (loadout != null)
            {
                if (loadout.forceAptitudeDegree != -99 && pawn.story?.traits != null)
                {
                    var existing = pawn.story.traits.GetTrait(RPDefOf.RPC_CyberAptitude);
                    if (existing != null) pawn.story.traits.RemoveTrait(existing);
                    pawn.story.traits.GainTrait(new Trait(RPDefOf.RPC_CyberAptitude, loadout.forceAptitudeDegree));
                }
                ApplyLoadout(pawn, loadout);
            }
            ApplyStreetName(pawn);
            if (loadout != null || pawn.Faction.Ext() != null)
                Diag.Event("pawngen", $"{Diag.Describe(pawn)} kind {pawn.kindDef.defName}: weapon {pawn.equipment?.Primary?.def.defName ?? "none"}, " +
                                      $"cyberware [{string.Join(", ", pawn.InstalledCyberware().Select(c => c.def.defName + " " + c.tier))}], usage {pawn.GetCyberTracker()?.UsageRatio:P0}");
        }

        public static void ApplyLoadout(Pawn pawn, CyberwareLoadoutExtension loadout)
        {
            foreach (var option in loadout.options)
            {
                for (int n = 0; n < option.count; n++)
                {
                    if (!Rand.Chance(option.chance)) continue;
                    foreach (var def in option.hediffs.InRandomOrder())
                    {
                        if (TryInstall(pawn, def, loadout.minTier, loadout.maxTier)) break;
                    }
                }
            }
        }

        public static bool TryInstall(Pawn pawn, HediffDef def, CyberTier minTier, CyberTier maxTier)
        {
            var ext = def.Ext();
            if (ext == null) return false;
            BodyPartRecord part = null;
            if (!ext.slot.WholeBody)
            {
                part = CyberSurgeryUtility.CandidateParts(pawn, ext.slot).Where(p => CyberUtility.CanInstall(pawn, def, p, out _)).RandomElementWithFallback();
                if (part == null) return false;
            }
            else if (!CyberUtility.CanInstall(pawn, def, null, out _)) return false;

            var lo = ext.minTier > minTier ? ext.minTier : minTier;
            var hi = ext.maxTier < maxTier ? ext.maxTier : maxTier;
            if (hi < lo) hi = lo;
            var h = (Hediff_Cyberware)HediffMaker.MakeHediff(def, pawn, part);
            h.tier = (CyberTier)Rand.RangeInclusive((int)lo, (int)hi);
            pawn.health.AddHediff(h, part);
            return true;
        }

        private static StreetNamesDef streetNames;

        public static void ApplyStreetName(Pawn pawn)
        {
            var ext = pawn.Faction.Ext();
            if (ext == null || !(pawn.Name is NameTriple name) || !Rand.Chance(ext.streetNameChance)) return;
            streetNames ??= DefDatabase<StreetNamesDef>.GetNamedSilentFail("RPC_StreetNames");
            if (streetNames == null || streetNames.names.NullOrEmpty()) return;
            pawn.Name = new NameTriple(name.First, streetNames.names.RandomElement(), name.Last);
        }
    }
}
