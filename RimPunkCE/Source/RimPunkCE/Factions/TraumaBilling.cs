using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>What one Trauma Team extraction costs on top of the coverage (TraumaTeamTuningDef fees).</summary>
    public struct TraumaBill
    {
        public int treated, treatmentFee, hostiles, combatFee, psychoFee;
        public int Total => treatmentFee + combatFee + psychoFee;

        public static TraumaBill For(TraumaTeamTuningDef t, int treated, int hostiles, bool cyberpsycho) => new TraumaBill
        {
            treated = treated,
            treatmentFee = Mathf.Min(treated * t.feePerHediff, t.maxTreatmentFee),
            hostiles = hostiles,
            combatFee = hostiles > 0 ? t.combatFee + hostiles * t.combatFeePerHostile : 0,
            psychoFee = cyberpsycho ? t.cyberpsychosisFee : 0,
        };

        public string Lines()
        {
            var sb = new StringBuilder();
            if (treatmentFee > 0) sb.AppendLine("RPC_TraumaBillTreatment".Translate(treated, treatmentFee));
            if (combatFee > 0) sb.AppendLine("RPC_TraumaBillCombat".Translate(hostiles, combatFee));
            if (psychoFee > 0) sb.AppendLine("RPC_TraumaBillPsycho".Translate(psychoFee));
            sb.Append("RPC_TraumaBillTotal".Translate(Total));
            return sb.ToString();
        }
    }

    /// <summary>
    /// Trauma Team billing (prompts/factions.md): coverage is bought up front, every extraction is billed on return
    /// (per condition treated, a combat surcharge if the squad had to fight, a large fee for curing a cyberpsycho).
    /// A bill the colony can't pay in full becomes debt: daily compound interest, no Trauma Team service until it's
    /// paid, and once it has gone unpaid for debtGraceDays the debt is sold to a collection agency that raids the colony
    /// (again every collectionRepeatDays). A payment of at least minPaymentFraction of the debt restarts the clock.
    /// </summary>
    public partial class GameComponent_RimPunk
    {
        private float traumaDebt;
        /// <summary>Tick the collectors are sent (-1: no debt).</summary>
        private int nextCollectionTick = -1;
        /// <summary>0: none, 1: first warning sent, 2: final notice sent (for the current deadline).</summary>
        private int debtWarnStage;
        private int collectionsSent;
        private readonly Dictionary<Pawn, int> refusedMessageTick = new Dictionary<Pawn, int>();

        /// <summary>True while a collector raid is being started (lets Patch_RaidEnemy_FactionCanBeGroupSource through).</summary>
        public static bool SendingCollectors;

        public float TraumaDebt => traumaDebt;
        public int NextCollectionTick => nextCollectionTick;

        /// <summary>The player's colony owes Trauma Team: no dispatches, no new coverage.</summary>
        public static bool PlayerInDebt => Current.Game != null && instance != null && instance.traumaDebt >= 1f;

        public static float DaysUntilCollection
        {
            get
            {
                var inst = instance;
                if (inst == null || inst.nextCollectionTick < 0) return -1f;
                return Mathf.Max(0f, (inst.nextCollectionTick - Find.TickManager.TicksGame) / 60000f);
            }
        }

        public static Faction Collectors => CyberFactionUtility.FirstOfKind(CyberFactionKind.DebtCollector);

        /// <summary>True if Trauma Team refuses this patient (the player's colony is in debt). Messages at most every ~2 hours per pawn.</summary>
        private bool RefuseForDebt(Pawn patient)
        {
            if (!PlayerInDebt || patient.Faction == null || !patient.Faction.IsPlayer) return false;
            int now = Find.TickManager.TicksGame;
            if (!refusedMessageTick.TryGetValue(patient, out int last) || now - last > 5000)
            {
                refusedMessageTick[patient] = now;
                Messages.Message("RPC_TraumaRefusedDebt".Translate(patient.LabelShort, Mathf.CeilToInt(traumaDebt)), patient, MessageTypeDefOf.NegativeEvent);
                Diag.Event("trauma", $"dispatch refused for {Diag.Describe(patient)}: colony owes {traumaDebt:0}");
            }
            return true;
        }

        /// <summary>Bills a returned player patient: paid from silver on <paramref name="map"/> if there is enough, otherwise it all goes on the debt.</summary>
        private void BillPatient(TraumaReturn r, Pawn patient, Map map, TraumaTeamTuningDef tuning)
        {
            var bill = TraumaBill.For(tuning, r.hediffsTreated, r.hostilesEngaged, r.wasCyberpsycho);
            r.billTotal = bill.Total;
            if (bill.Total <= 0) return;
            if (map != null && SilverUtility.TryConsumeSilver(map, bill.Total))
            {
                Diag.Event("trauma", $"billed {bill.Total} for {Diag.Describe(patient)} (treated {bill.treated}, hostiles {bill.hostiles}, psycho {r.wasCyberpsycho}): paid");
                Find.LetterStack.ReceiveLetter("RPC_TraumaBillLetter".Translate(), "RPC_TraumaBillPaidText".Translate(patient.LabelShort, bill.Lines()),
                    LetterDefOf.NeutralEvent, patient);
                return;
            }
            Diag.Event("trauma", $"billed {bill.Total} for {Diag.Describe(patient)} (treated {bill.treated}, hostiles {bill.hostiles}, psycho {r.wasCyberpsycho}): can't pay, added to debt");
            AddDebt(bill.Total);
            Find.LetterStack.ReceiveLetter("RPC_TraumaDebtLetter".Translate(), "RPC_TraumaDebtLetterText".Translate(patient.LabelShort, bill.Lines(),
                    Mathf.CeilToInt(traumaDebt), (tuning.debtDailyInterest * 100f).ToString("0.#"), tuning.debtGraceDays.ToString("0")),
                LetterDefOf.NegativeEvent, patient);
        }

        public void AddDebt(float amount)
        {
            if (amount <= 0f) return;
            var tuning = Tuning;
            if (traumaDebt < 1f) RestartCollectionClock(tuning);
            traumaDebt += amount;
        }

        private void RestartCollectionClock(TraumaTeamTuningDef tuning)
        {
            nextCollectionTick = Find.TickManager.TicksGame + Mathf.RoundToInt((tuning?.debtGraceDays ?? 30f) * 60000f);
            debtWarnStage = 0;
        }

        /// <summary>Pays what it can toward the debt from silver on <paramref name="map"/>. Synced in multiplayer.</summary>
        [Multiplayer.API.SyncMethod]
        public static void PayDebt(Map map)
        {
            var inst = Instance;
            if (inst == null || map == null || inst.traumaDebt < 1f) return;
            int owed = Mathf.CeilToInt(inst.traumaDebt);
            int pay = Mathf.Min(owed, SilverUtility.CountSilver(map));
            if (pay <= 0 || !SilverUtility.TryConsumeSilver(map, pay))
            {
                Messages.Message("RPC_TraumaDebtNoSilver".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            inst.ApplyPayment(pay);
        }

        public void ApplyPayment(int pay)
        {
            var tuning = Tuning;
            float before = traumaDebt;
            traumaDebt -= pay;
            if (traumaDebt < 1f)
            {
                ClearDebt();
                Diag.Event("trauma", $"debt paid off ({pay})");
                Messages.Message("RPC_TraumaDebtCleared".Translate(), MessageTypeDefOf.PositiveEvent, false);
                return;
            }
            bool resets = pay >= before * (tuning?.minPaymentFraction ?? 0.25f);
            if (resets) RestartCollectionClock(tuning);
            Diag.Event("trauma", $"debt payment {pay}, {traumaDebt:0} left, clock reset={resets}");
            Messages.Message((resets ? "RPC_TraumaDebtPaidReset" : "RPC_TraumaDebtPaidPartial").Translate(pay, Mathf.CeilToInt(traumaDebt),
                Mathf.CeilToInt(tuning?.debtGraceDays ?? 30f), ((tuning?.minPaymentFraction ?? 0.25f) * 100f).ToString("0")), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>Debt gone: service restored, collectors stand down (raiders still on the map leave: their lord exits on non-hostility).</summary>
        public void ClearDebt()
        {
            traumaDebt = 0f;
            nextCollectionTick = -1;
            debtWarnStage = 0;
            collectionsSent = 0;
            var col = Collectors;
            if (col != null && col.HostileTo(Faction.OfPlayer)) CyberFactionUtility.ForceRelation(col, Faction.OfPlayer, FactionRelationKind.Neutral);
        }

        /// <summary>Once a day: interest, and collectors stay hostile while they're owed (goodwill drifts back otherwise).</summary>
        private void DebtDailyTick()
        {
            if (traumaDebt < 1f) return;
            var tuning = Tuning;
            float before = traumaDebt;
            traumaDebt *= 1f + (tuning?.debtDailyInterest ?? 0.02f);
            Diag.Event("trauma", $"debt interest: {before:0} -> {traumaDebt:0}, collection in {DaysUntilCollection:0.0} days");
            var col = Collectors;
            if (collectionsSent > 0 && col != null && !col.HostileTo(Faction.OfPlayer)) CyberFactionUtility.ForceRelation(col, Faction.OfPlayer, FactionRelationKind.Hostile);
        }

        /// <summary>Every 250 ticks: deadline warnings, then the collectors.</summary>
        private void TickDebt(int now)
        {
            if (traumaDebt < 1f || nextCollectionTick < 0) return;
            var tuning = Tuning;
            float daysLeft = (nextCollectionTick - now) / 60000f;
            if (debtWarnStage < 1 && daysLeft <= (tuning?.debtFirstWarningDays ?? 10f))
            {
                debtWarnStage = 1;
                Find.LetterStack.ReceiveLetter("RPC_TraumaDebtWarning".Translate(), "RPC_TraumaDebtWarningText".Translate(Mathf.CeilToInt(traumaDebt), Mathf.CeilToInt(daysLeft)),
                    LetterDefOf.NegativeEvent);
            }
            else if (debtWarnStage < 2 && daysLeft <= (tuning?.debtFinalWarningDays ?? 3f))
            {
                debtWarnStage = 2;
                Find.LetterStack.ReceiveLetter("RPC_TraumaDebtFinal".Translate(), "RPC_TraumaDebtFinalText".Translate(Mathf.CeilToInt(traumaDebt), Mathf.CeilToInt(daysLeft)),
                    LetterDefOf.ThreatSmall);
            }
            if (now < nextCollectionTick) return;
            if (SendCollectors(tuning))
            {
                collectionsSent++;
                nextCollectionTick = now + Mathf.RoundToInt((tuning?.collectionRepeatDays ?? 15f) * 60000f);
                debtWarnStage = 1; // repeats get only the final notice
            }
            else nextCollectionTick = now + 2500; // no map / no faction yet: try again in an hour
        }

        /// <summary>The collection agency raids the player's home: hostile from now until the debt is paid.</summary>
        public bool SendCollectors(TraumaTeamTuningDef tuning)
        {
            var col = Collectors;
            var map = Find.AnyPlayerHomeMap;
            if (col == null || map == null)
            {
                Diag.Event("trauma", $"can't send collectors: faction={(col != null)} map={(map != null)}");
                return false;
            }
            if (!col.HostileTo(Faction.OfPlayer)) CyberFactionUtility.ForceRelation(col, Faction.OfPlayer, FactionRelationKind.Hostile);
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = col;
            parms.forced = true;
            parms.points = Mathf.Max(parms.points * (tuning?.collectionPointsFactor ?? 1.25f), tuning?.collectionMinPoints ?? 400f);
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            parms.customLetterLabel = "RPC_TraumaCollectorsLetter".Translate();
            parms.customLetterText = "RPC_TraumaCollectorsLetterText".Translate(Mathf.CeilToInt(traumaDebt)) + "\n\n{BASETEXT}";
            bool ok;
            SendingCollectors = true;
            try { ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms); }
            finally { SendingCollectors = false; }
            Diag.Event("trauma", $"debt collectors sent ({parms.points:0} points, debt {traumaDebt:0}): {(ok ? "ok" : "FAILED")}");
            return ok;
        }

        public void Debug_SetNextCollection(int tick)
        {
            nextCollectionTick = tick;
            debtWarnStage = 0;
        }

        public void Debug_DailyTick() => DebtDailyTick();

        private void ExposeDebt()
        {
            Scribe_Values.Look(ref traumaDebt, "traumaDebt");
            Scribe_Values.Look(ref nextCollectionTick, "nextCollectionTick", -1);
            Scribe_Values.Look(ref debtWarnStage, "debtWarnStage");
            Scribe_Values.Look(ref collectionsSent, "collectionsSent");
        }
    }
}
