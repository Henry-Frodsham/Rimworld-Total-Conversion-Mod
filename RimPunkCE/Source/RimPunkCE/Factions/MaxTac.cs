using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>A MaxTac squad sent to put down one cyberpsycho.</summary>
    public class MaxTacDispatch : IExposable
    {
        public Pawn target;
        public List<Pawn> squad = new List<Pawn>();
        public TransportShip ship;
        public int startTick;
        public int boardTick = -1;
        public bool saidArrive;

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target", saveDestroyedThings: true);
            Scribe_Collections.Look(ref squad, "squad", LookMode.Reference);
            Scribe_References.Look(ref ship, "ship");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref boardTick, "boardTick", -1);
            Scribe_Values.Look(ref saidArrive, "saidArrive");
        }
    }

    /// <summary>
    /// MaxTac (prompts/factions.md): once a cyberpsycho has killed maxTacKills people during one episode, a MaxTac squad
    /// lands by shuttle next to them and kills them (downed or not). They leave when the target is dead, gone, no longer
    /// in cyberpsychosis, or after maxTacMaxTicks. MaxTac members are immune to cyberpsychosis.
    /// </summary>
    public partial class GameComponent_RimPunk
    {
        private List<MaxTacDispatch> maxTac = new List<MaxTacDispatch>();

        public bool MaxTacComingFor(Pawn p) => maxTac.Any(d => d.target == p);

        public void CallMaxTac(Pawn target, string reason)
        {
            if (target == null || !target.Spawned || target.Dead || MaxTacComingFor(target)) return;
            var tuning = RPDefOf.RPC_CyberpsychosisTuning;
            var faction = CyberFactionUtility.FirstOfKind(CyberFactionKind.MaxTac);
            if (faction == null || tuning?.maxTacOperatorKind == null)
            {
                Diag.Event("maxtac", $"can't dispatch for {Diag.Describe(target)}: faction={(faction != null)} kind={(tuning?.maxTacOperatorKind != null)}");
                return;
            }
            int size = tuning.maxTacSquadSize.RandomInRange;
            var squad = new List<Pawn>();
            for (int i = 0; i < size; i++)
            {
                var kind = i == 0 && tuning.maxTacLeaderKind != null ? tuning.maxTacLeaderKind : tuning.maxTacOperatorKind;
                squad.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, mustBeCapableOfViolence: true)));
            }
            var ship = SendShuttle(faction, target.Map, target.Position, squad, RPDefOf.RPC_Ship_MaxTacShuttle);
            ship.AddJobs(ShipJobDefOf.Unload);
            var wait = (ShipJob_WaitForever)ShipJobMaker.MakeShipJob(ShipJobDefOf.WaitForever);
            wait.showGizmos = false;
            ship.AddJob(wait);
            maxTac.Add(new MaxTacDispatch { target = target, squad = squad, ship = ship, startTick = Find.TickManager.TicksGame });
            Diag.Event("maxtac", $"dispatch for {Diag.Describe(target)} ({reason}): {size} officers");
            Find.LetterStack.ReceiveLetter("RPC_MaxTacLetter".Translate(), "RPC_MaxTacLetterText".Translate(target.LabelShort, reason), LetterDefOf.ThreatBig, target);
        }

        private void TickMaxTac(int now)
        {
            if (maxTac.Count == 0) return;
            var tuning = RPDefOf.RPC_CyberpsychosisTuning;
            for (int i = maxTac.Count - 1; i >= 0; i--)
            {
                var d = maxTac[i];
                bool shipLost = d.ship == null || d.ship.Disposed || (d.ship.shipThing?.Destroyed ?? true);
                if (shipLost)
                {
                    Diag.Event("maxtac", "dispatch ending: shuttle lost");
                    RemoveSquad(d.squad);
                    maxTac.RemoveAt(i);
                    continue;
                }
                if (!d.ship.ShipExistsAndIsSpawned) continue; // still landing
                var map = d.ship.shipThing.Map;
                var t = d.target;
                if (!d.saidArrive && d.squad.FirstOrDefault(s => s != null && s.Spawned) is Pawn officer)
                {
                    d.saidArrive = true;
                    BarkUtility.Say(officer, BarkKeys.MaxTacArrive);
                }
                if (d.boardTick < 0)
                {
                    bool dead = t == null || t.Dead;
                    bool gone = !dead && t.MapHeld != map;
                    bool cured = !dead && !gone && !CyberpsychosisUtility.InCyberpsychosis(t);
                    bool timeUp = now - d.startTick > tuning.maxTacMaxTicks;
                    if (dead || gone || cured || timeUp)
                    {
                        Diag.Event("maxtac", $"mission over for {Diag.Describe(t)}: dead={dead} gone={gone} psychosisEnded={cured} timeUp={timeUp}");
                        if (dead && t != null) Messages.Message("RPC_MaxTacDone".Translate(t.LabelShort), MessageTypeDefOf.NeutralEvent);
                        d.boardTick = now;
                    }
                    else
                    {
                        AssignSquadLord(d.squad, d.ship.shipThing.Faction, t); // attack orders: CombatOrdersTick
                    }
                }
                if (d.boardTick >= 0 && BoardStep(d.ship, d.squad, d.boardTick, now, tuning.maxTacBoardTicks))
                {
                    Diag.Event("maxtac", "shuttle leaving");
                    maxTac.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Keeps a squad member attacking the target: shoot from here, move to a firing position, or melee.
        /// <paramref name="killIncapped"/>: keep attacking once the target is downed (MaxTac) or stop (Trauma Team).
        /// </summary>
        private static void Hunt(Pawn p, Pawn target, bool killIncapped = true)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || p.InMentalState || p.Map != target.Map) return;
            var cur = p.CurJob;
            // never cut a CE reload short: re-issuing the attack restarted the reload forever (stuck at 0 rounds)
            if (cur != null && cur.def == CombatExtended.CE_JobDefOf.ReloadWeapon) return;
            if (cur != null && cur.playerForced)
            {
                if (cur.targetA.Thing == target && (cur.def == JobDefOf.AttackMelee || cur.def == JobDefOf.AttackStatic)) return;
                if (cur.def == JobDefOf.Goto && p.pather.Moving) return;
            }
            var verb = p.TryGetAttackVerb(target, allowManualCastWeapons: true);
            if (verb == null) return;
            Job job;
            if (verb.IsMeleeAttack)
            {
                if (!p.CanReach(target, PathEndMode.Touch, Danger.Deadly)) return;
                job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            }
            else if (verb.CanHitTargetFrom(p.Position, target))
            {
                job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                job.endIfCantShootTargetFromCurPos = true;
            }
            else if (CastPositionFinder.TryFindCastPosition(new CastPositionRequest
                     {
                         caster = p,
                         target = target,
                         verb = verb,
                         maxRangeFromTarget = verb.EffectiveRange,
                         wantCoverFromTarget = false,
                     }, out var dest))
            {
                job = JobMaker.MakeJob(JobDefOf.Goto, dest);
            }
            else return;
            job.killIncappedTarget = killIncapped;
            job.playerForced = true;
            job.expiryInterval = 600;
            job.checkOverrideOnExpire = true;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        private void ExposeMaxTac()
        {
            Scribe_Collections.Look(ref maxTac, "maxTacDispatches", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                maxTac ??= new List<MaxTacDispatch>();
                maxTac.RemoveAll(d => d == null);
            }
        }
    }
}
