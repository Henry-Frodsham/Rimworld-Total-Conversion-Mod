using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimPunkCE
{
    /// <summary>Trauma Team numbers (Defs/Factions/TraumaTeam.xml).</summary>
    public class TraumaTeamTuningDef : Def
    {
        public int coverageCostSilver = 600;
        public float coverageDays = 15f;
        public IntRange squadSize = new IntRange(3, 4);
        public PawnKindDef operatorKind;
        /// <summary>Extract once no hostiles are this close to the patient...</summary>
        public float clearRadius = 18f;
        /// <summary>...or after this long on the ground, whichever comes first.</summary>
        public int maxTicksOnGround = 5000;
        /// <summary>The squad is on the ground at least this long (shuttle lands, they reach the patient) before an all-clear extract.</summary>
        public int minTicksOnGround = 900;
        /// <summary>Patient comes back after this long (about a day).</summary>
        public int returnTicks = 60000;
        /// <summary>Carrying the patient to the shuttle gives up after this long (patient is extracted directly).</summary>
        public int maxCarryTicks = 2500;
        /// <summary>The shuttle leaves this long after boarding starts, loading any stragglers.</summary>
        public int maxBoardTicks = 1500;

        // billing (TraumaBilling.cs): every extraction is billed on return, on top of the coverage
        /// <summary>Per condition treated (injuries, blood loss, tended illnesses).</summary>
        public int feePerHediff = 35;
        /// <summary>The per-condition part of a bill never goes above this.</summary>
        public int maxTreatmentFee = 1200;
        /// <summary>Surcharge when the squad had to fight, plus per hostile they engaged.</summary>
        public int combatFee = 300;
        public int combatFeePerHostile = 75;
        /// <summary>Extracting and curing a cyberpsycho: the biggest line on any bill.</summary>
        public int cyberpsychosisFee = 2500;
        // debt
        public float debtDailyInterest = 0.02f;
        public float debtGraceDays = 30f;
        public float debtFirstWarningDays = 10f;
        public float debtFinalWarningDays = 3f;
        /// <summary>A payment of at least this share of the debt restarts the grace period.</summary>
        public float minPaymentFraction = 0.25f;
        public float collectionRepeatDays = 15f;
        public float collectionPointsFactor = 1.25f;
        public float collectionMinPoints = 400f;

        // NPC coverage (TraumaCoverageUtility): rare, only high-value members of corporations / NetWatch, never gangs
        public float npcLeaderChance = 0.2f;
        public float npcEliteChance = 0.04f;
        /// <summary>Non-leader pawns need at least this combat power to be considered.</summary>
        public float npcEliteMinCombatPower = 150f;
        /// <summary>At most this many NPC dispatches on the go at once.</summary>
        public int npcMaxConcurrentDispatches = 1;
        /// <summary>Attacking Trauma Team on purpose makes them hostile this long (TraumaHostility.cs).</summary>
        public float hostilityDays = 30f;
        /// <summary>At most this share of one raid is covered (rounded down: 3% = none under 34 raiders).</summary>
        public float npcRaidMaxCoveredFraction = 0.03f;
    }

    /// <summary>
    /// Game-level RimPunk state:
    ///  - faction rivalries (applied once per game, kept hostile);
    ///  - Trauma Team dispatches: a covered colonist goes down -> a squad lands by shuttle, fights everyone hostile
    ///    to the patient, extracts them, and returns them healed about a day later (prompts/factions.md).
    /// </summary>
    public partial class GameComponent_RimPunk : GameComponent
    {
        private bool rivalriesApplied;
        private List<TraumaDispatch> dispatches = new List<TraumaDispatch>();
        private List<TraumaReturn> returns = new List<TraumaReturn>();

        private static GameComponent_RimPunk instance;

        public GameComponent_RimPunk(Game game) { instance = this; }

        public static GameComponent_RimPunk Instance => Current.Game == null ? null : instance;

        public static TraumaTeamTuningDef Tuning => DefDatabase<TraumaTeamTuningDef>.GetNamedSilentFail("RPC_TraumaTeamTuning");

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // idempotent: re-applied on every load (and daily below) so rivals stay hostile and broken saves heal
            if (Find.FactionManager == null) return;
            // service factions added in later versions are created in older saves (MaxTac, Trauma Team)
            EnsureFaction(CyberFactionKind.MaxTac, "RPC_Faction_MaxTac");
            EnsureFaction(CyberFactionKind.TraumaTeam, "RPC_Faction_TraumaTeam");
            EnsureFaction(CyberFactionKind.DebtCollector, "RPC_Faction_DebtCollectors");
            CyberFactionUtility.ApplyRivalries();
        }

        private static void EnsureFaction(CyberFactionKind kind, string defName)
        {
            if (CyberFactionUtility.FirstOfKind(kind) != null) return;
            var def = DefDatabase<FactionDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            var faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def)); // makes initial relations with every faction
            Find.FactionManager.Add(faction);
            Diag.Event("factions", $"added missing faction {faction.Name} to this save");
        }

        // ---------------- Trauma Team ----------------

        public static bool IsCovered(Pawn p) => p?.health?.hediffSet?.HasHediff(RPDefOf.RPC_TraumaTeamCoverage) == true;

        public void Notify_PawnDowned(Pawn patient)
        {
            if (!IsCovered(patient) || !patient.Spawned || patient.Dead) return;
            if (dispatches.Any(d => d.patient == patient)) return;
            if (RefuseForDebt(patient)) return; // coverage is kept: it works again once the debt is paid
            if (RefuseForHostility(patient)) return;
            var tt = CyberFactionUtility.FirstOfKind(CyberFactionKind.TraumaTeam);
            var tuning = Tuning;
            if (tt == null || tuning?.operatorKind == null) return;

            var map = patient.Map;
            // an NPC client (a covered corpo boss): the squad is contracted to the client's faction and fights as part of
            // it, so it's hostile to the player when the client is. Trauma Team's own faction and goodwill are untouched.
            bool npc = patient.Faction == null || !patient.Faction.IsPlayer;
            if (npc && dispatches.Count(x => x.npc) >= tuning.npcMaxConcurrentDispatches)
            {
                Diag.Event("trauma", $"npc dispatch for {Diag.Describe(patient)} skipped: {tuning.npcMaxConcurrentDispatches} already running");
                return;
            }
            var squadFaction = npc && patient.Faction != null ? patient.Faction : tt;
            // the team is hostile to whoever is hostile to the patient, friendly to everyone else
            if (!npc)
                foreach (var other in Find.FactionManager.AllFactionsListForReading)
                {
                    if (other == tt || other.def.hidden) continue;
                    bool enemy = patient.Faction != null && other != patient.Faction && other.HostileTo(patient.Faction);
                    var want = enemy ? FactionRelationKind.Hostile : (other == patient.Faction ? FactionRelationKind.Ally : FactionRelationKind.Neutral);
                    if (other.IsPlayer) continue; // never touch the player relation (always friendly)
                    if (tt.RelationKindWith(other) != want) CyberFactionUtility.ForceRelation(tt, other, want);
                }

            var squad = new List<Pawn>();
            int size = tuning.squadSize.RandomInRange;
            for (int i = 0; i < size; i++)
                squad.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(tuning.operatorKind, squadFaction, mustBeCapableOfViolence: true)));
            // the squad flies in on a Trauma Team shuttle, unloads, and the shuttle waits to take the patient
            var ship = SendShuttle(tt, map, patient.Position, squad, RPDefOf.RPC_Ship_TraumaShuttle);
            ship.AddJobs(ShipJobDefOf.Unload);
            var wait = (ShipJob_WaitForever)ShipJobMaker.MakeShipJob(ShipJobDefOf.WaitForever);
            wait.showGizmos = false; // the player can't send it away
            ship.AddJob(wait);

            var hediff = patient.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_TraumaTeamCoverage);
            if (hediff != null) patient.health.RemoveHediff(hediff); // one dispatch per coverage

            dispatches.Add(new TraumaDispatch { patient = patient, squad = squad, ship = ship, startTick = Find.TickManager.TicksGame, npc = npc });
            Diag.Event("trauma", $"{(npc ? "npc " : "")}dispatch for {Diag.Describe(patient)}: {squad.Count} operators ({squadFaction?.Name})");
            if (!npc)
                Find.LetterStack.ReceiveLetter("RPC_TraumaTeamLetter".Translate(), "RPC_TraumaTeamLetterText".Translate(patient.LabelShort), LetterDefOf.PositiveEvent, patient);
            else if (squadFaction != null && squadFaction.HostileTo(Faction.OfPlayer))
                Find.LetterStack.ReceiveLetter("RPC_TraumaTeamEnemyLetter".Translate(), "RPC_TraumaTeamEnemyLetterText".Translate(patient.LabelShort, squadFaction.Name), LetterDefOf.ThreatSmall, patient);
            else
                Messages.Message("RPC_TraumaTeamNpcMessage".Translate(patient.LabelShort), patient, MessageTypeDefOf.NeutralEvent);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager.TicksGame;
            if (now % 2500 == 0) DiagChecks.RunAll();
            if (now % 60000 == 0) CyberFactionUtility.ApplyRivalries();
            if (now % 60000 == 0) DebtDailyTick();
            if (now % 60000 == 0) TraumaHostilityDailyTick();
            if (now % 60 == 0) CombatOrdersTick();
            if (now % 250 != 0) return;
            TickMaxTac(now);
            TickDebt(now);
            var tuning = Tuning;
            if (tuning == null) return;

            for (int i = dispatches.Count - 1; i >= 0; i--)
                if (TickDispatch(dispatches[i], now, tuning)) dispatches.RemoveAt(i);

            for (int i = returns.Count - 1; i >= 0; i--)
            {
                var r = returns[i];
                if (now < r.returnTick) continue;
                returns.RemoveAt(i);
                ReturnPatient(r, tuning);
            }
        }

        /// <summary>Any hostile (to the patient) that a standing operator could attack: within their weapon range and in line of sight.</summary>
        private static bool AnyHostileInSquadRange(TraumaDispatch d)
        {
            var patient = d.patient;
            foreach (var op in d.squad)
            {
                if (op == null || !op.Spawned || op.Dead || op.Downed) continue;
                var map = op.Map;
                float range = Mathf.Max(op.CurrentEffectiveVerb?.EffectiveRange ?? 0f, 12f);
                foreach (var e in map.mapPawns.AllPawnsSpawned)
                {
                    if (e.Dead || e.Downed || e == patient || d.squad.Contains(e)) continue;
                    if (!e.HostileTo(patient) && !e.HostileTo(op)) continue;
                    if (!e.Position.InHorDistOf(op.Position, range)) continue;
                    if (GenSight.LineOfSight(op.Position, e.Position, map, skipFirstCell: true)) return true;
                }
            }
            return false;
        }

        private static bool AnyHostileNear(Pawn patient, float radius)
        {
            var map = patient.MapHeld;
            if (map == null) return false;
            var at = patient.PositionHeld;
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || p.Downed || p == patient) continue;
                if (!p.HostileTo(patient)) continue;
                if (p.Position.InHorDistOf(at, radius)) return true;
            }
            return false;
        }

        /// <summary>
        /// One dispatch, every 250 ticks. Phases: fight (until clear / time up) -> an operator carries the patient onto the
        /// shuttle -> everyone walks back and boards -> the shuttle flies off. Returns true when the dispatch is over.
        /// </summary>
        private bool TickDispatch(TraumaDispatch d, int now, TraumaTeamTuningDef tuning)
        {
            var patient = d.patient;
            var carrier = CarrierOf(patient);
            bool carriedBySquad = carrier != null && d.squad.Contains(carrier);
            // a colonist carrying the patient (a rescue) still counts as on the map
            bool patientGone = !d.patientAboard && (patient == null || patient.Dead || patient.MapHeld == null);
            bool shipLost = d.ship == null || d.ship.Disposed || (d.ship.shipThing?.Destroyed ?? true);
            bool shipDown = !shipLost && d.ship.ShipExistsAndIsSpawned;

            if (shipLost)
            {
                Diag.Event("trauma", "dispatch ending: shuttle lost, extraction failed");
                if (carriedBySquad) carrier.carryTracker.TryDropCarriedThing(carrier.Position, ThingPlaceMode.Near, out _);
                RemoveSquad(d.squad);
                if (!d.patientAboard && !patientGone) Messages.Message("RPC_TraumaTeamFailed".Translate(patient.LabelShort), patient, MessageTypeDefOf.NegativeEvent);
                return true;
            }
            // nothing happens while the shuttle is still in the air
            if (!shipDown) return false;
            if (d.boardTick < 0) AssignSquadLord(d.squad, SquadFaction(d), patient);
            // a squad hostile to the colony doesn't ask colonists to step back
            if (!(SquadFaction(d)?.HostileTo(Faction.OfPlayer) ?? false)) EnforceExclusionZone(d);
            Callouts(d, now);

            // fight: operators are ordered onto the nearest hostile threatening the patient
            if (d.extractTick < 0 && d.boardTick < 0 && !patientGone && now % 1000 == 0) LogSquadState("trauma", d.squad);
            // the colony turned on Trauma Team mid-dispatch: they fight, don't extract, and leave when their time is up
            if (BetrayedDispatch(d) && d.boardTick < 0)
            {
                if (d.extractTick >= 0 && !d.patientAboard)
                {
                    if (carriedBySquad) carrier.carryTracker.TryDropCarriedThing(carrier.Position, ThingPlaceMode.Near, out _);
                    d.extractTick = -1;
                    Diag.Event("trauma", $"extraction of {Diag.Describe(patient)} called off: Trauma Team is hostile");
                }
                if (now - d.startTick > tuning.maxTicksOnGround || patientGone) d.boardTick = now;
            }
            if (d.extractTick < 0 && d.boardTick < 0 && !BetrayedDispatch(d))
            {
                bool timeUp = now - d.startTick > tuning.maxTicksOnGround;
                // secure the patient as soon as no operator on the ground can see a hostile within their weapon range
                bool landed = d.squad.Any(p => p != null && p.Spawned && !p.Dead) && now - d.startTick >= tuning.minTicksOnGround;
                bool clear = !patientGone && landed && !AnyHostileInSquadRange(d);
                if (patientGone)
                {
                    Diag.Event("trauma", $"no extraction: patientGone={patientGone} patientDowned={patient?.Downed}, squad boarding");
                    d.boardTick = now;
                }
                else if (timeUp || clear)
                {
                    Diag.Event("trauma", $"extracting {Diag.Describe(patient)}: timeUp={timeUp} clear={clear}");
                    d.extractTick = now;
                }
            }

            // carry the patient aboard. Only a patient carried onto the shuttle is extracted: no teleporting.
            if (d.extractTick >= 0 && !d.patientAboard && d.boardTick < 0)
            {
                if (patientGone)
                {
                    Diag.Event("trauma", "patient lost during extraction, squad boarding");
                    d.boardTick = now;
                }
                else if (now - d.extractTick > tuning.maxCarryTicks)
                {
                    Diag.Event("trauma", $"carry timed out: extraction of {Diag.Describe(patient)} failed, squad boarding");
                    if (carriedBySquad) carrier.carryTracker.TryDropCarriedThing(carrier.Position, ThingPlaceMode.Near, out _);
                    Messages.Message("RPC_TraumaTeamFailed".Translate(patient.LabelShort), patient, MessageTypeDefOf.NegativeEvent);
                    d.boardTick = now;
                }
                else
                {
                    // Trauma Team takes the patient off whoever else is carrying them (a colonist rescue)
                    if (carrier != null && !carriedBySquad && carrier.Spawned)
                    {
                        carrier.carryTracker.TryDropCarriedThing(carrier.Position, ThingPlaceMode.Near, out _);
                        carrier.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                    }
                    EnsureCarrier(d, patient);
                }
            }
            if (d.patientAboard && d.boardTick < 0) d.boardTick = now;

            if (d.boardTick >= 0 && BoardStep(d.ship, d.squad, d.boardTick, now, tuning.maxBoardTicks))
            {
                Diag.Event("trauma", $"shuttle leaving (patientAboard={d.patientAboard})");
                return true;
            }
            return false;
        }

        /// <summary>Every 60 ticks: Trauma Team squads in the fight phase and MaxTac squads get attack orders (vanilla assist AI is slow to engage).</summary>
        private void CombatOrdersTick()
        {
            var tuning = Tuning;
            if (tuning != null)
                foreach (var d in dispatches)
                {
                    if (d.extractTick >= 0 || d.boardTick >= 0 || d.patient == null || d.patient.Dead || d.patient.MapHeld == null) continue;
                    if (d.ship == null || !d.ship.ShipExistsAndIsSpawned) continue;
                    FightForPatient(d, d.patient, tuning.clearRadius * 2f);
                }
            foreach (var d in maxTac)
            {
                if (d.boardTick >= 0 || d.target == null || d.target.Dead || !d.target.Spawned) continue;
                if (d.ship == null || !d.ship.ShipExistsAndIsSpawned || !CyberpsychosisUtility.InCyberpsychosis(d.target)) continue;
                foreach (var p in d.squad) Hunt(p, d.target);
            }
        }

        /// <summary>
        /// Each operator attacks the nearest pawn hostile to the patient that is either near the patient or within
        /// selfRadius of the operator (someone shooting at them from range).
        /// </summary>
        private static void FightForPatient(TraumaDispatch d, Pawn patient, float radius, float selfRadius = 30f)
        {
            var map = patient.MapHeld;
            if (map == null) return;
            var at = patient.PositionHeld;
            foreach (var p in d.squad)
            {
                if (p == null || !p.Spawned || p.Dead || p.Downed || p.InMentalState || p.Map != map) continue;
                var cur = p.CurJob;
                if (cur != null && cur.playerForced)
                {
                    if ((cur.def == JobDefOf.AttackMelee || cur.def == JobDefOf.AttackStatic)
                        && cur.targetA.Thing is Pawn ct && ct.Spawned && !ct.Dead && !ct.Downed) continue;
                    if (cur.def == JobDefOf.Goto && p.pather.Moving) continue;
                }
                Pawn best = null;
                float bestDist = float.MaxValue;
                foreach (var e in map.mapPawns.AllPawnsSpawned)
                {
                    if (e.Dead || e.Downed || e == patient || d.squad.Contains(e) || !e.HostileTo(patient)) continue;
                    if (!e.Position.InHorDistOf(at, radius) && !e.Position.InHorDistOf(p.Position, selfRadius)) continue;
                    float dist = e.Position.DistanceToSquared(p.Position);
                    if (dist < bestDist) { best = e; bestDist = dist; }
                }
                if (best == null) continue;
                d.engaged.Add(best.thingIDNumber); // billed as a combat surcharge
                Hunt(p, best, killIncapped: false);
            }
        }

        /// <summary>Diagnostics: what each squad member is doing (job, target, weapon, ammo).</summary>
        private static void LogSquadState(string category, List<Pawn> squad)
        {
            if (!Diag.Enabled) return;
            var parts = new List<string>();
            foreach (var p in squad)
            {
                if (p == null || !p.Spawned) continue;
                var weapon = p.equipment?.Primary;
                var ammo = weapon?.TryGetComp<CombatExtended.CompAmmoUser>();
                string ammoText = ammo == null ? "-" : (ammo.HasMagazine ? ammo.CurMagCount.ToString() : "") + (ammo.HasAmmoOrMagazine ? "" : " EMPTY");
                parts.Add($"{p.LabelShort}: {(p.Downed ? "DOWNED " : "")}job={p.CurJobDef?.defName ?? "none"} target={(p.CurJob?.targetA.Thing as Pawn)?.LabelShort ?? "-"} enemy={(p.mindState?.enemyTarget as Pawn)?.LabelShort ?? "-"} weapon={weapon?.def.defName ?? "none"} ammo={ammoText}");
            }
            Diag.Event(category, "squad: " + string.Join(" | ", parts));
        }

        private static Pawn CarrierOf(Pawn patient) => (patient?.ParentHolder as Pawn_CarryTracker)?.pawn;

        /// <summary>Keeps one able operator on the job of carrying the patient to the shuttle.</summary>
        private static void EnsureCarrier(TraumaDispatch d, Pawn patient)
        {
            var c = d.carrier;
            if (c != null && c.Spawned && !c.Dead && !c.Downed && c.CurJobDef == RPDefOf.RPC_TraumaBoardShuttle && c.CurJob.targetB.Thing == patient) return;
            if (!patient.Spawned) return; // being carried already
            d.carrier = null;
            Pawn best = null;
            float bestDist = float.MaxValue;
            foreach (var p in d.squad)
            {
                if (p == null || !p.Spawned || p.Dead || p.Downed || p.Map != patient.Map) continue;
                if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) continue;
                if (!p.CanReach(patient, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                float dist = p.Position.DistanceToSquared(patient.Position);
                if (dist < bestDist) { best = p; bestDist = dist; }
            }
            if (best == null)
            {
                if (!d.noCarrierLogged) Diag.Event("trauma", $"no operator can reach {Diag.Describe(patient)}");
                d.noCarrierLogged = true;
                return;
            }
            // a doctor or rescuer holding the patient's reservation is interrupted: Trauma Team takes over
            var holder = patient.Map.reservationManager.FirstRespectedReserver(patient, best);
            if (holder != null && !d.squad.Contains(holder)) holder.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            if (!best.CanReserve(patient))
            {
                if (!d.noCarrierLogged) Diag.Event("trauma", $"{Diag.Describe(patient)} is reserved by {Diag.Describe(holder)}, waiting");
                d.noCarrierLogged = true;
                return;
            }
            var job = JobMaker.MakeJob(RPDefOf.RPC_TraumaBoardShuttle, d.ship.shipThing, patient);
            job.count = 1;
            job.playerForced = true;
            best.jobs.StartJob(job, JobCondition.InterruptForced);
            d.carrier = best;
            Diag.Event("trauma", $"{Diag.Describe(best)} carrying {Diag.Describe(patient)} to the shuttle");
        }

        /// <summary>Called by JobDriver_TraumaBoardShuttle when the carrier reaches the shuttle with the patient.</summary>
        public void Notify_PatientAboard(Pawn patient, Map map)
        {
            var d = dispatches.FirstOrDefault(x => x.patient == patient);
            if (d == null) return;
            ExtractToWorld(patient, Find.TickManager.TicksGame + (Tuning?.returnTicks ?? 60000), map, d.engaged.Count);
            d.patientAboard = true;
        }

        /// <summary>
        /// The patient is held off-map as a world pawn until the return shuttle. Registered first so vanilla doesn't treat
        /// them as a free world pawn (it would hand a colonist to a random NPC faction, see Patch_WorldPawns_GetSituation).
        /// </summary>
        private void ExtractToWorld(Pawn patient, int returnTick, Map map, int hostilesEngaged = 0)
        {
            int tile = map != null ? (int)map.Tile : -1;
            var r = new TraumaReturn
            {
                patient = patient, returnTick = returnTick, mapTile = tile, homeFaction = patient.Faction,
                wasCyberpsycho = CyberpsychosisUtility.InCyberpsychosis(patient), hostilesEngaged = hostilesEngaged,
            };
            returns.Add(r);
            // stabilised on board: world pawns keep ticking, so an untreated patient could bleed out off-map
            // (a death there offers a funeral); with Trauma Team they're safe, like a kidnapped colonist
            r.hediffsTreated += StabilizePatient(patient);
            bool npc = patient.Faction == null || !patient.Faction.IsPlayer;
            // an NPC client held prisoner by the colony is taken back: no longer the colony's prisoner
            if (npc && patient.guest != null && patient.HostFaction != null) patient.guest.SetGuestStatus(null);
            if (patient.Spawned)
            {
                FleckMaker.ThrowSmoke(patient.DrawPos, patient.Map, 1.5f);
                patient.DeSpawn();
            }
            if (!patient.IsWorldPawn()) Find.WorldPawns.PassToWorld(patient, PawnDiscardDecideMode.KeepForever);
            Diag.Event("trauma", $"extracted {Diag.Describe(patient)}, return at t{returnTick}");
            Messages.Message((npc ? "RPC_TraumaTeamExtractedNpc" : "RPC_TraumaTeamExtracted").Translate(patient.LabelShort),
                npc ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent);
        }

        /// <summary>Trauma Team and MaxTac shuttles on the ground can't be damaged (Patch_Thing_TakeDamage_ProtectShuttles).</summary>
        public static bool IsProtectedShip(Thing t)
        {
            var inst = instance;
            if (inst == null || Current.Game == null) return false;
            for (int i = 0; i < inst.dispatches.Count; i++)
                if (inst.dispatches[i].ship?.shipThing == t) return true;
            for (int i = 0; i < inst.maxTac.Count; i++)
                if (inst.maxTac[i].ship?.shipThing == t) return true;
            return false;
        }

        /// <summary>Removes injuries and blood loss, tends the rest. Returns how many conditions were treated (billed per condition).</summary>
        private static int StabilizePatient(Pawn patient)
        {
            var hs = patient.health?.hediffSet;
            if (hs == null) return 0;
            int n = CountConditions(patient);
            // Painducer damage still held back would land after the return (it waits while off-map)
            var tracker = patient.GetCyberTracker();
            if (tracker != null && tracker.DelayedDamagePending > 0f)
            {
                Diag.Event("trauma", $"cleared {tracker.DelayedDamagePending:0.#} delayed damage on {Diag.Describe(patient)}");
                tracker.ClearDelayed();
            }
            foreach (var inj in hs.hediffs.OfType<Hediff_Injury>().ToList()) patient.health.RemoveHediff(inj);
            foreach (var h in hs.hediffs.Where(h => h.def == HediffDefOf.BloodLoss).ToList()) patient.health.RemoveHediff(h);
            foreach (var h in hs.hediffs.ToList())
                if (h.TendableNow()) h.Tended(1f, 1f);
            return n;
        }

        /// <summary>
        /// Billable conditions: each injured body part once (not each wound: a frag grenade leaves hundreds), blood loss,
        /// and each kind of other tendable condition (illness, infection...).
        /// </summary>
        public static int CountConditions(Pawn pawn)
        {
            var hs = pawn.health?.hediffSet;
            if (hs == null) return 0;
            int parts = hs.hediffs.OfType<Hediff_Injury>().Select(h => h.Part).Distinct().Count();
            int bloodLoss = hs.hediffs.Any(h => h.def == HediffDefOf.BloodLoss) ? 1 : 0;
            int other = hs.hediffs.Where(h => !(h is Hediff_Injury) && h.def != HediffDefOf.BloodLoss && h.TendableNow()).Select(h => h.def).Distinct().Count();
            return parts + bloodLoss + other;
        }

        /// <summary>True while a patient is off-map with Trauma Team (they count as borrowed, not free).</summary>
        public static bool IsAwayWithTraumaTeam(Pawn p)
        {
            var inst = instance;
            if (inst == null || Current.Game == null || inst.returns.Count == 0) return false;
            for (int i = 0; i < inst.returns.Count; i++)
                if (inst.returns[i].patient == p) return true;
            return false;
        }

        /// <summary>A shuttle (Royalty) carrying <paramref name="contents"/>, landing as close to <paramref name="near"/> as it fits.
        /// <paramref name="shipDef"/>: Trauma Team's own (RPC_Ship_TraumaShuttle) or, by default, the vanilla shuttle.</summary>
        private static TransportShip SendShuttle(Faction tt, Map map, IntVec3 near, IEnumerable<Thing> contents, TransportShipDef shipDef = null)
        {
            shipDef ??= TransportShipDefOf.Ship_Shuttle;
            var shuttle = ThingMaker.MakeThing(shipDef.shipThing);
            shuttle.SetFaction(tt);
            var ship = TransportShipMaker.MakeTransportShip(shipDef, contents, shuttle);
            var size = shipDef.shipThing.Size + new IntVec2(2, 2);
            if (!near.IsValid || !DropCellFinder.TryFindDropSpotNear(near, map, out var cell, allowFogged: false, canRoofPunch: false, maxRadius: 25, allowIndoors: false, size: size))
                cell = DropCellFinder.GetBestShuttleLandingSpot(map, tt);
            ship.ArriveAt(cell, map.Parent);
            return ship;
        }

        /// <summary>Unloaded squad members join one assist lord around <paramref name="patient"/> (Trauma Team patient, MaxTac target).</summary>
        private static Faction SquadFaction(TraumaDispatch d) =>
            d.squad.FirstOrDefault(p => p != null && !p.Dead)?.Faction ?? d.ship?.shipThing?.Faction;

        private static void AssignSquadLord(List<Pawn> squad, Faction tt, Pawn patient)
        {
            if (tt == null) return;
            Lord lord = null;
            foreach (var p in squad)
                if (p != null && p.Spawned && p.GetLord() != null) { lord = p.GetLord(); break; }
            foreach (var p in squad)
            {
                if (p == null || !p.Spawned || p.Dead || p.GetLord() != null) continue;
                if (lord == null)
                {
                    var spot = patient != null && patient.Spawned ? patient.Position : p.Position;
                    // hostile to the colony (a contracted squad for an enemy client): hold the spot, no assist-colony duty
                    LordJob job = tt.HostileTo(Faction.OfPlayer) ? new LordJob_DefendPoint(spot, addFleeToil: false) : new LordJob_AssistColony(tt, spot);
                    lord = LordMaker.MakeNewLord(tt, job, p.Map, new List<Pawn> { p });
                }
                else lord.AddPawn(p);
            }
        }

        /// <summary>
        /// Squad walks back aboard. Once nobody is still walking (or time is up) stragglers and downed members are loaded
        /// and the shuttle leaves. True when it left.
        /// </summary>
        private static bool BoardStep(TransportShip ship, List<Pawn> squad, int boardTick, int now, int maxBoardTicks)
        {
            bool anyWalking = false;
            foreach (var p in squad)
            {
                if (p == null || !p.Spawned || p.Dead || p.Downed) continue;
                anyWalking = true;
                if (p.CurJobDef != RPDefOf.RPC_TraumaBoardShuttle)
                {
                    var job = JobMaker.MakeJob(RPDefOf.RPC_TraumaBoardShuttle, ship.shipThing);
                    job.playerForced = true;
                    p.jobs.StartJob(job, JobCondition.InterruptForced);
                }
            }
            if (anyWalking && now - boardTick <= maxBoardTicks) return false;
            BoardAndLeave(ship, squad);
            return true;
        }

        /// <summary>Living squad members get on the shuttle and it flies off (they leave the map with it).</summary>
        private static void BoardAndLeave(TransportShip ship, List<Pawn> squad)
        {
            var map = ship.shipThing.Map;
            foreach (var p in squad)
            {
                if (p == null || !p.Spawned || p.Dead) continue;
                if (p.carryTracker?.CarriedThing != null) p.carryTracker.TryDropCarriedThing(p.Position, ThingPlaceMode.Near, out _);
                p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap);
                FleckMaker.ThrowSmoke(p.DrawPos, map, 1f);
                p.DeSpawn();
                ship.TransporterComp.innerContainer.TryAddOrTransfer(p);
            }
            var fly = (ShipJob_FlyAway)ShipJobMaker.MakeShipJob(ShipJobDefOf.FlyAway);
            fly.dropMode = TransportShipDropMode.None; // don't unload what we just boarded
            ship.ForceJob(fly);
        }

        private static void RemoveSquad(List<Pawn> squad)
        {
            if (squad == null) return;
            foreach (var p in squad)
            {
                if (p == null || p.Destroyed) continue;
                if (p.Spawned)
                {
                    FleckMaker.ThrowSmoke(p.DrawPos, p.Map, 1.5f);
                    p.DeSpawn();
                }
                else if (p.holdingOwner != null && !p.IsWorldPawn())
                {
                    p.holdingOwner.Remove(p); // still inside a drop pod (timed out before landing)
                }
                if (!p.Dead && !p.IsWorldPawn()) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Decide);
            }
        }

        private void ReturnPatient(TraumaReturn r, TraumaTeamTuningDef tuning)
        {
            var pawn = r.patient;
            if (pawn == null || pawn.Dead || pawn.Spawned)
            {
                Diag.Event("trauma", $"return skipped for {Diag.Describe(pawn)}: null={pawn == null} dead={pawn?.Dead} spawned={pawn?.Spawned}");
                return;
            }
            if (r.homeFaction != null && !r.homeFaction.IsPlayer)
            {
                // an NPC client is patched up and goes home to their faction (stays a world pawn, may show up again)
                if (r.wasCyberpsycho || CyberpsychosisUtility.InCyberpsychosis(pawn)) StripCyberwareBelowSafe(pawn);
                if (r.wasCyberpsycho) CyberpsychosisUtility.ClearOrphanedRage(pawn, addBurnout: true);
                foreach (var h in pawn.health.hediffSet.hediffs.Where(h => h is Hediff_Injury || h.def == HediffDefOf.BloodLoss).ToList())
                    pawn.health.RemoveHediff(h);
                if (pawn.Faction != r.homeFaction && !r.homeFaction.defeated) pawn.SetFaction(r.homeFaction);
                Diag.Event("trauma", $"npc client {Diag.Describe(pawn)} treated and back with {r.homeFaction.Name}");
                return;
            }
            // a cyberpsycho comes back with the chrome that tipped them over removed (most capacity first) until below 50%
            if (r.wasCyberpsycho || CyberpsychosisUtility.InCyberpsychosis(pawn)) StripCyberwareBelowSafe(pawn);
            if (r.wasCyberpsycho) CyberpsychosisUtility.ClearOrphanedRage(pawn, addBurnout: true);
            // patched up by Trauma Team (anything still open is billed too)
            r.hediffsTreated += pawn.health.hediffSet.hediffs.Count(h => h is Hediff_Injury || h.def == HediffDefOf.BloodLoss) > 0
                ? pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Select(h => h.Part).Distinct().Count()
                  + (pawn.health.hediffSet.hediffs.Any(h => h.def == HediffDefOf.BloodLoss) ? 1 : 0)
                : 0;
            foreach (var inj in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
                pawn.health.RemoveHediff(inj);
            foreach (var h in pawn.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.BloodLoss).ToList())
                pawn.health.RemoveHediff(h);

            var map = Find.Maps.FirstOrDefault(m => m.Tile == r.mapTile && m.IsPlayerHome) ?? Find.AnyPlayerHomeMap;
            if (map == null)
            {
                Diag.Event("trauma", $"return skipped for {Diag.Describe(pawn)}: no player home map");
                return;
            }
            if (pawn.IsWorldPawn()) Find.WorldPawns.RemovePawn(pawn);
            var home = r.homeFaction ?? Faction.OfPlayer;
            if (pawn.Faction != home) pawn.SetFaction(home);
            // flown back on a Trauma Team shuttle: lands, drops the patient off, leaves
            var tt = CyberFactionUtility.FirstOfKind(CyberFactionKind.TraumaTeam);
            var ship = SendShuttle(tt, map, IntVec3.Invalid, new[] { (Thing)pawn }, RPDefOf.RPC_Ship_TraumaShuttle);
            ship.AddJobs(ShipJobDefOf.Unload, ShipJobDefOf.FlyAway);
            Diag.Event("trauma", $"returned {Diag.Describe(pawn)}");
            Find.LetterStack.ReceiveLetter("RPC_TraumaTeamReturnLetter".Translate(), "RPC_TraumaTeamReturnLetterText".Translate(pawn.LabelShort), LetterDefOf.PositiveEvent, pawn);
            // the player's colony pays for the service (NPC patients are on their own faction's tab)
            if (home.IsPlayer) BillPatient(r, pawn, map, tuning);
        }

        /// <summary>
        /// Removes chrome, highest capacity cost first, until usage is under the safe threshold (50%): RimPunk cyberware
        /// and other mods' implants alike. A replacement part (e.g. a bionic arm) leaves the part missing, like vanilla's
        /// removal surgery, instead of handing back a natural limb.
        /// </summary>
        private static void StripCyberwareBelowSafe(Pawn pawn)
        {
            if (CyberpsychosisUtility.IsPermanent(pawn)) return; // David's last stand: removing chrome can't help, so they keep it
            var tracker = pawn.GetCyberTracker();
            if (tracker == null) return;
            float safe = RPDefOf.RPC_CyberpsychosisTuning.safeUsage;
            var chrome = new List<(Hediff h, float cost)>();
            foreach (var c in tracker.Cyberware) chrome.Add((c, c.CapacityCost));
            foreach (var f in tracker.ForeignImplants) chrome.Add((f, ForeignCyberwareUtility.CostOf(f)));
            var removed = new List<string>();
            foreach (var (h, cost) in chrome.OrderByDescending(x => x.cost))
            {
                if (tracker.UsageRatio < safe) break;
                if (cost <= 0f || !pawn.health.hediffSet.hediffs.Contains(h)) continue;
                removed.Add(h.def.defName + (h is Hediff_Cyberware cw ? " " + cw.tier : ""));
                if (h is Hediff_AddedPart && h.Part != null) pawn.health.AddHediff(HediffDefOf.MissingBodyPart, h.Part);
                else pawn.health.RemoveHediff(h);
                tracker.Notify_CyberwareChanged();
            }
            if (pawn.MentalState is MentalState_Cyberpsychosis) pawn.MentalState.RecoverFromState();
            Diag.Event("trauma", $"{Diag.Describe(pawn)} stripped of [{string.Join(", ", removed)}], usage now {tracker.UsageRatio:P0}");
        }

        public int PendingReturns => returns.Count;

        /// <summary>Debug: bring every extracted patient back now.</summary>
        public void ForceReturnsNow()
        {
            foreach (var r in returns) r.returnTick = 0;
        }

        /// <summary>Buy coverage for a colonist. Synced in multiplayer.</summary>
        [Multiplayer.API.SyncMethod]
        public static void BuyCoverage(Pawn pawn)
        {
            var tuning = Tuning;
            if (tuning == null || pawn?.Map == null || IsCovered(pawn)) return;
            if (PlayerInDebt)
            {
                Messages.Message("RPC_TraumaRefusedCoverage".Translate(Mathf.CeilToInt(Instance.TraumaDebt)), pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (TraumaHostileToPlayer)
            {
                Messages.Message("RPC_TraumaRefusedCoverageHostile".Translate(DaysUntilTraumaForgives.ToString("0.#")), pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!SilverUtility.TryConsumeSilver(pawn.Map, tuning.coverageCostSilver))
            {
                Messages.Message("RPC_TraumaTeamNoSilver".Translate(tuning.coverageCostSilver), pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }
            var h = HediffMaker.MakeHediff(RPDefOf.RPC_TraumaTeamCoverage, pawn);
            var d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.RoundToInt(tuning.coverageDays * 60000f);
            pawn.health.AddHediff(h);
            Messages.Message("RPC_TraumaTeamBought".Translate(pawn.LabelShort, tuning.coverageDays.ToString("0")), pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref rivalriesApplied, "rivalriesApplied");
            Scribe_Collections.Look(ref dispatches, "traumaDispatches", LookMode.Deep);
            Scribe_Collections.Look(ref returns, "traumaReturns", LookMode.Deep);
            ExposeMaxTac();
            ExposeDebt();
            ExposeTraumaHostility();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                dispatches ??= new List<TraumaDispatch>();
                returns ??= new List<TraumaReturn>();
                dispatches.RemoveAll(d => d.patient == null);
                returns.RemoveAll(r => r.patient == null);
            }
        }
    }

    public class TraumaDispatch : IExposable
    {
        public Pawn patient;
        public List<Pawn> squad = new List<Pawn>();
        public TransportShip ship;
        public int startTick;
        public int extractTick = -1;
        public int boardTick = -1;
        public bool patientAboard;
        public Pawn carrier;
        public bool noCarrierLogged;
        /// <summary>The patient isn't the player's: the squad is contracted to the patient's faction.</summary>
        public bool npc;
        // callouts and exclusion zone (TraumaTeamPresence.cs)
        public bool saidArrive, saidSecure, saidDepart, warnedMessage;
        public int lastContactTick = -99999;
        public HashSet<Pawn> warned = new HashSet<Pawn>();
        /// <summary>Hostiles the squad was ordered onto (thingIDNumbers): the combat surcharge on the bill.</summary>
        public HashSet<int> engaged = new HashSet<int>();

        public void ExposeData()
        {
            Scribe_References.Look(ref patient, "patient");
            Scribe_Collections.Look(ref squad, "squad", LookMode.Reference);
            Scribe_References.Look(ref ship, "ship");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref extractTick, "extractTick", -1);
            Scribe_Values.Look(ref boardTick, "boardTick", -1);
            Scribe_Values.Look(ref patientAboard, "patientAboard");
            Scribe_References.Look(ref carrier, "carrier");
            Scribe_Values.Look(ref npc, "npc");
            Scribe_Values.Look(ref saidArrive, "saidArrive");
            Scribe_Values.Look(ref saidSecure, "saidSecure");
            Scribe_Values.Look(ref saidDepart, "saidDepart");
            Scribe_Values.Look(ref lastContactTick, "lastContactTick", -99999);
            Scribe_Collections.Look(ref engaged, "engaged", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) engaged ??= new HashSet<int>();
        }
    }

    public class TraumaReturn : IExposable
    {
        public Pawn patient;
        public int returnTick;
        public int mapTile;
        /// <summary>Faction to restore on return (null in older saves: the player).</summary>
        public Faction homeFaction;
        /// <summary>Extracted mid-cyberpsychosis: comes back with chrome stripped below the safe threshold.</summary>
        public bool wasCyberpsycho;
        // billing
        public int hediffsTreated;
        public int hostilesEngaged;
        public int billTotal;

        public void ExposeData()
        {
            Scribe_References.Look(ref homeFaction, "homeFaction");
            Scribe_References.Look(ref patient, "patient", saveDestroyedThings: true);
            Scribe_Values.Look(ref returnTick, "returnTick");
            Scribe_Values.Look(ref mapTile, "mapTile");
            Scribe_Values.Look(ref wasCyberpsycho, "wasCyberpsycho");
            Scribe_Values.Look(ref hediffsTreated, "hediffsTreated");
            Scribe_Values.Look(ref hostilesEngaged, "hostilesEngaged");
        }
    }

    public static class SilverUtility
    {
        /// <summary>Silver on the map the player can see (what TryConsumeSilver can take).</summary>
        public static int CountSilver(Map map) =>
            map == null ? 0 : map.listerThings.ThingsOfDef(ThingDefOf.Silver).Where(t => t.Spawned && !t.Position.Fogged(map)).Sum(t => t.stackCount);

        /// <summary>Takes silver from anywhere on the map that belongs to the player (stockpiles, shelves...).</summary>
        public static bool TryConsumeSilver(Map map, int amount)
        {
            var stacks = map.listerThings.ThingsOfDef(ThingDefOf.Silver).Where(t => t.Spawned && !t.Position.Fogged(map)).ToList();
            if (stacks.Sum(t => t.stackCount) < amount) return false;
            int left = amount;
            foreach (var s in stacks)
            {
                int take = Mathf.Min(left, s.stackCount);
                s.SplitOff(take).Destroy();
                left -= take;
                if (left <= 0) break;
            }
            return true;
        }
    }
}
