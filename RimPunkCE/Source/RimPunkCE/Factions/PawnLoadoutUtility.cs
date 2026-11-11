using System.Collections.Generic;
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
                if (loadout.addArmor) ChromePlatingUtility.AddGeneratedArmor(pawn, loadout);
                foreach (var extra in loadout.extraHediffs) pawn.health.AddHediff(extra);
                ArmLauncherUtility.StockGenerated(pawn); // a launcher arm spawns loaded, with spare shells
            }
            if (loadout != null) QuickhackLoadoutUtility.LearnRandom(pawn, loadout.minTier, loadout.maxTier);
            ApplyStreetName(pawn);
            TraumaCoverageUtility.TryGiveNpcCoverage(pawn);
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
                    foreach (var def in Candidates(option))
                    {
                        if (TryInstall(pawn, def, loadout.minTier, loadout.maxTier)) break;
                    }
                }
            }
        }

        /// <summary>Cyberdecks are only rolled where quickhacks exist; new worlds not created yet follow the default setting.</summary>
        private static bool Allowed(HediffDef def)
        {
            if (def.Ext()?.osType != CyberOsType.Cyberdeck) return true;
            var features = WorldFeatures.Current;
            return features != null ? features.Quickhacks : RimPunkMod.Settings?.newWorldQuickhacks ?? true;
        }

        /// <summary>Models to try, in order: a random family (equal odds each), then its models in random order; flat options just shuffle.</summary>
        private static IEnumerable<HediffDef> Candidates(CyberwareLoadoutOption option)
        {
            if (option.groups.NullOrEmpty())
            {
                foreach (var def in option.hediffs.Where(Allowed).InRandomOrder()) yield return def;
                yield break;
            }
            foreach (var group in option.groups.Select(g => g.hediffs.Where(Allowed).ToList()).Where(g => g.Count > 0).InRandomOrder())
                foreach (var def in group.InRandomOrder()) yield return def;
        }

        public static bool TryInstall(Pawn pawn, HediffDef def, CyberTier minTier, CyberTier maxTier)
        {
            var ext = def.Ext();
            if (ext == null) return false;
            // a basic implant brings the comms implant it needs
            var needed = CyberUtility.MissingRequirement(pawn, ext);
            if (needed != null && !TryInstall(pawn, needed, minTier, maxTier)) return false;
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
