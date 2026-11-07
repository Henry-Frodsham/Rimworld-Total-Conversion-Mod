using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>
    /// Attacking Trauma Team on purpose (a colonist or turret of the player's damaging an operator or a Trauma Team
    /// shuttle it was aiming at) makes them hostile for hostilityDays: the squad on the ground fights back and won't
    /// extract the patient, the shuttle can be damaged, and no dispatches or coverage until it's over. Stray shots,
    /// splash, and colonists in a mental state don't count. NPC-contracted squads (in their client's faction) aren't
    /// Trauma Team's faction, so shooting them is just combat.
    /// </summary>
    public partial class GameComponent_RimPunk
    {
        private int traumaHostileUntil = -1;

        /// <summary>Lets Patch_Faction_TryAffectGoodwillWith drop Trauma Team's goodwill (it's otherwise never-hostile).</summary>
        public static bool AllowTraumaHostility;

        public static bool TraumaHostileToPlayer => Current.Game != null && instance != null && instance.traumaHostileUntil >= 0;

        public static float DaysUntilTraumaForgives =>
            instance == null || instance.traumaHostileUntil < 0 ? 0f : Mathf.Max(0f, (instance.traumaHostileUntil - Find.TickManager.TicksGame) / 60000f);

        public static bool IsTraumaTeamTarget(Thing t) =>
            t is Pawn p ? p.Faction != null && p.Faction.IsTraumaTeam()
                        : t != null && t.def == RPDefOf.RPC_TraumaShuttle && t.Faction != null && t.Faction.IsTraumaTeam();

        /// <summary>The player meant to hit this Trauma Team pawn / shuttle (it was the attack's intended target).</summary>
        public static bool IsIntentionalAttack(Thing victim, DamageInfo dinfo)
        {
            if (!IsTraumaTeamTarget(victim)) return false;
            var by = dinfo.Instigator;
            if (by == null || by.Faction != Faction.OfPlayer) return false;
            if (by is Pawn a && a.InMentalState) return false;
            return dinfo.IntendedTarget == victim;
        }

        public void Notify_AttackedTraumaTeam(Thing attacker, Thing victim)
        {
            var tt = CyberFactionUtility.FirstOfKind(CyberFactionKind.TraumaTeam);
            if (tt == null) return;
            var tuning = Tuning;
            bool already = traumaHostileUntil >= 0;
            traumaHostileUntil = Find.TickManager.TicksGame + Mathf.RoundToInt((tuning?.hostilityDays ?? 30f) * 60000f);
            if (already) return; // each new attack restarts the clock
            AllowTraumaHostility = true;
            try { CyberFactionUtility.ForceRelation(tt, Faction.OfPlayer, FactionRelationKind.Hostile); }
            finally { AllowTraumaHostility = false; }
            Diag.Event("trauma", $"{Diag.Describe(attacker as Pawn)} ({attacker?.LabelShort}) attacked {victim.LabelShort} on purpose: Trauma Team hostile for {tuning?.hostilityDays ?? 30f:0} days");
            Find.LetterStack.ReceiveLetter("RPC_TraumaHostileLetter".Translate(),
                "RPC_TraumaHostileLetterText".Translate(attacker?.LabelShort ?? "?", victim.LabelShort, (tuning?.hostilityDays ?? 30f).ToString("0")),
                LetterDefOf.ThreatBig, victim);
        }

        /// <summary>Daily: the blacklist runs out, relations go back to neutral.</summary>
        private void TraumaHostilityDailyTick()
        {
            if (traumaHostileUntil < 0) return;
            var tt = CyberFactionUtility.FirstOfKind(CyberFactionKind.TraumaTeam);
            if (Find.TickManager.TicksGame < traumaHostileUntil)
            {
                // keep it hostile (goodwill can drift back on its own)
                if (tt != null && !tt.HostileTo(Faction.OfPlayer))
                {
                    AllowTraumaHostility = true;
                    try { CyberFactionUtility.ForceRelation(tt, Faction.OfPlayer, FactionRelationKind.Hostile); }
                    finally { AllowTraumaHostility = false; }
                }
                return;
            }
            EndTraumaHostility();
        }

        public void EndTraumaHostility()
        {
            traumaHostileUntil = -1;
            var tt = CyberFactionUtility.FirstOfKind(CyberFactionKind.TraumaTeam);
            if (tt != null && tt.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)
                CyberFactionUtility.ForceRelation(tt, Faction.OfPlayer, FactionRelationKind.Neutral);
            Diag.Event("trauma", "Trauma Team hostility over");
            Find.LetterStack.ReceiveLetter("RPC_TraumaForgiveLetter".Translate(), "RPC_TraumaForgiveLetterText".Translate(), LetterDefOf.PositiveEvent);
        }

        /// <summary>True if Trauma Team refuses this patient because they're hostile to the player.</summary>
        private bool RefuseForHostility(Pawn patient)
        {
            if (!TraumaHostileToPlayer || patient.Faction == null || !patient.Faction.IsPlayer) return false;
            int now = Find.TickManager.TicksGame;
            if (!refusedMessageTick.TryGetValue(patient, out int last) || now - last > 5000)
            {
                refusedMessageTick[patient] = now;
                Messages.Message("RPC_TraumaRefusedHostile".Translate(patient.LabelShort, DaysUntilTraumaForgives.ToString("0.#")), patient, MessageTypeDefOf.NegativeEvent);
                Diag.Event("trauma", $"dispatch refused for {Diag.Describe(patient)}: Trauma Team hostile");
            }
            return true;
        }

        /// <summary>A squad on the ground when the player turned on them: no extraction of a player patient (drops them if carrying).</summary>
        private static bool BetrayedDispatch(TraumaDispatch d) =>
            TraumaHostileToPlayer && !d.npc && d.patient?.Faction != null && d.patient.Faction.IsPlayer;

        private void ExposeTraumaHostility() => Scribe_Values.Look(ref traumaHostileUntil, "traumaHostileUntil", -1);
    }
}
