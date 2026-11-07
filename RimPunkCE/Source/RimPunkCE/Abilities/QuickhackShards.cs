using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Quickhack shard: used by a pawn with a cyberdeck, teaches (or upgrades to) its quickhack at the shard's tier,
    /// then is consumed (CompUseEffect_DestroySelf), like a psycast neuroformer.
    /// </summary>
    public class CompProperties_UseEffectLearnQuickhack : CompProperties_UseEffect
    {
        public AbilityDef quickhack;
        public CyberTier tier = CyberTier.T1;
        public CompProperties_UseEffectLearnQuickhack() => compClass = typeof(CompUseEffect_LearnQuickhack);
    }

    public class CompUseEffect_LearnQuickhack : CompUseEffect
    {
        public CompProperties_UseEffectLearnQuickhack Props => (CompProperties_UseEffectLearnQuickhack)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            var tracker = usedBy.GetCyberTracker();
            if (tracker == null) return;
            tracker.LearnQuickhack(Props.quickhack, Props.tier);
            Diag.Event("quickhack", $"{Diag.Describe(usedBy)} learned {Props.quickhack.defName} {Props.tier}");
            if (PawnUtility.ShouldSendNotificationAbout(usedBy))
                Messages.Message("RPC_LearnedQuickhack".Translate(usedBy.LabelShort, Props.quickhack.label, Props.tier.Label()), usedBy, MessageTypeDefOf.PositiveEvent);
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            var tracker = p.GetCyberTracker();
            if (tracker == null || !tracker.HasCyberdeck) return "RPC_NeedsCyberdeck".Translate();
            if (tracker.LearnedQuickhacks.TryGetValue(Props.quickhack, out var have) && have >= Props.tier)
                return "RPC_QuickhackAlreadyKnown".Translate(have.Label());
            return base.CanBeUsedBy(p);
        }
    }

    /// <summary>Faction netrunners: a pawn generated with a cyberdeck learns a few quickhacks at its loadout's tiers.</summary>
    public static class QuickhackLoadoutUtility
    {
        public static IEnumerable<AbilityDef> AllQuickhacks =>
            DefDatabase<AbilityDef>.AllDefsListForReading.Where(d => d.HasModExtension<QuickhackExtension>() && d != QuickhackDefOf.RPC_Quickhack_BlackwallGateway);

        public static void LearnRandom(Pawn pawn, CyberTier minTier, CyberTier maxTier)
        {
            var tracker = pawn.GetCyberTracker();
            if (tracker == null || !tracker.HasCyberdeck) return;
            int count = Rand.RangeInclusive(2, 3) + (int)maxTier / 2;
            var pool = AllQuickhacks.Where(d =>
            {
                var e = d.GetModExtension<QuickhackExtension>();
                return !e.aiNever && e.minTier <= maxTier;
            }).InRandomOrder().Take(count);
            foreach (var d in pool)
            {
                var e = d.GetModExtension<QuickhackExtension>();
                var lo = e.minTier > minTier ? e.minTier : minTier;
                var hi = e.maxTier < maxTier ? e.maxTier : maxTier;
                if (hi < lo) hi = lo;
                tracker.LearnQuickhack(d, (CyberTier)Rand.RangeInclusive((int)lo, (int)hi));
            }
        }
    }
}
