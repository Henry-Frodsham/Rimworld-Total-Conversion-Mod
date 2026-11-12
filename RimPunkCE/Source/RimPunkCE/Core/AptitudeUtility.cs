using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Every humanlike has exactly one capacity aptitude trait (Gonk..Legend, one TraitDef with 5 degrees),
    /// and Netrunner is rolled independently (pawn_traits.md). Both TraitDefs have commonality 0 so vanilla
    /// random trait generation never picks them; we add them here.
    /// </summary>
    public static class AptitudeUtility
    {
        /// <summary>Chance a newly generated pawn is a Netrunner.</summary>
        public const float NetrunnerChance = 0.1f;

        public static void EnsureAptitudeTrait(Pawn pawn)
        {
            var traits = pawn?.story?.traits;
            if (traits == null || !pawn.RaceProps.Humanlike) return;
            if (traits.HasTrait(RPDefOf.RPC_CyberAptitude)) return;

            // seeded per pawn so every multiplayer client picks the same degree, even for old saves
            Rand.PushState(Gen.HashCombineInt(pawn.thingIDNumber, 0x41505431));
            try
            {
                int degree = PawnGenerator.RandomTraitDegree(RPDefOf.RPC_CyberAptitude);
                traits.GainTrait(new Trait(RPDefOf.RPC_CyberAptitude, degree));
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>Called once at pawn generation (Patch_PawnGenerator_GenerateTraits).</summary>
        public static void OnPawnGenerated(Pawn pawn)
        {
            var traits = pawn?.story?.traits;
            if (traits == null || !pawn.RaceProps.Humanlike) return;
            EnsureAptitudeTrait(pawn);
            if (!traits.HasTrait(RPDefOf.RPC_Netrunner) && Rand.Chance(NetrunnerChance))
                traits.GainTrait(new Trait(RPDefOf.RPC_Netrunner));
        }

        public static bool IsGonk(Pawn pawn) => pawn?.story?.traits?.HasTrait(RPDefOf.RPC_CyberAptitude, AptitudeDegree.Gonk) == true;
    }

    /// <summary>
    /// On RPC_CyberwareCapacity: applies the settings multiplier, then Gonk's hard 20
    /// (nothing raises a Gonk's capacity, not even the chrome compressor).
    /// </summary>
    public class StatPart_CyberwareCapacity : StatPart
    {
        public float gonkCapacity = 20f;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is not Pawn pawn) return;
            if (AptitudeUtility.IsGonk(pawn))
            {
                val = gonkCapacity + ImmunoblockerUtility.CapacityBonus(pawn); // the one thing that raises a gonk's capacity
                return;
            }
            val *= RimPunkMod.Settings?.capacityMultiplier ?? 1f;
            val += ImmunoblockerUtility.CapacityBonus(pawn); // flat: a +200 drug is +200 whatever the capacity setting
            val += ShardUtility.CapacityBonus(pawn); // capacity shards: flat too (and, like everything else, nothing for a Gonk)
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (req.Thing is not Pawn pawn) return null;
            string first = AptitudeUtility.IsGonk(pawn) ? (string)"RPC_GonkCapacityExplanation".Translate(gonkCapacity) : null;
            float m = RimPunkMod.Settings?.capacityMultiplier ?? 1f;
            if (first == null && m != 1f) first = "RPC_CapacitySettingExplanation".Translate(m.ToStringPercent());
            float drug = ImmunoblockerUtility.CapacityBonus(pawn);
            if (drug > 0f)
            {
                string blocker = "RPC_ImmunoblockerExplanation".Translate(drug);
                first = first == null ? blocker : first + "\n" + blocker;
            }
            float shard = AptitudeUtility.IsGonk(pawn) ? 0f : ShardUtility.CapacityBonus(pawn);
            if (shard > 0f)
            {
                string line = "RPC_ShardExplanation".Translate(shard);
                first = first == null ? line : first + "\n" + line;
            }
            return first;
        }
    }
}
