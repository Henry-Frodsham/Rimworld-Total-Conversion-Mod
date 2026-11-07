using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimPunkCE
{
    public enum GigType
    {
        Assassinate, // kill the target
        Sabotage,    // destroy the server rack
        Steal,       // get the data shard back to base or into a caravan
        Rescue,      // free the hostage, who joins the colony
    }

    public enum GigState { Offered, Active, Completed, Failed }

    /// <summary>Gig numbers (RPC_GigTuning in Defs/Gigs/Gigs.xml).</summary>
    public class GigTuningDef : Def
    {
        public int maxOffers = 3;
        public FloatRange daysBetweenOffers = new FloatRange(3f, 6f);
        public float offerExpiryDays = 8f;
        public float deadlineDays = 12f;
        public IntRange siteDistance = new IntRange(4, 12);
        /// <summary>Reward = threat points x this x type factor, then clamped.</summary>
        public float rewardPerThreatPoint = 1.2f;
        public IntRange rewardClamp = new IntRange(500, 6000);
        public float siteThreatFactor = 0.8f;
        public float minSiteThreat = 300f;
        public int goodwillGain = 12;
        public int goodwillLossTarget = -15;
        public int goodwillLossFail = -8;
        public ThingDef serverRack;
        public ThingDef dataShard;
        public SitePartDef objectivePart;
        public SitePartDef basePart;
        public HediffDef restrained;
        public JobDef freeHostageJob;

        public float RewardFactor(GigType t)
        {
            switch (t)
            {
                case GigType.Assassinate: return 1.1f;
                case GigType.Sabotage: return 0.9f;
                case GigType.Steal: return 1f;
                default: return 0.8f; // the rescued pawn joins, so the money is smaller
            }
        }
    }

    public class Gig : IExposable, ILoadReferenceable
    {
        public int id;
        public GigType type;
        public GigState state = GigState.Offered;
        /// <summary>Null when the contact isn't fronting for a faction the player can deal with.</summary>
        public Faction issuer;
        public Faction target;
        public int reward;
        public int offerExpiresTick;
        public int deadlineTick;
        public Site site;
        public Map localMap;
        /// <summary>The person to kill or free, or the rack / shard. Null until the site map exists.</summary>
        public Thing objective;
        public bool objectiveSpawned;
        public bool hostageFreed;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref type, "type");
            Scribe_Values.Look(ref state, "state");
            Scribe_References.Look(ref issuer, "issuer");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref reward, "reward");
            Scribe_Values.Look(ref offerExpiresTick, "offerExpiresTick");
            Scribe_Values.Look(ref deadlineTick, "deadlineTick");
            Scribe_References.Look(ref site, "site");
            Scribe_References.Look(ref localMap, "localMap");
            Scribe_References.Look(ref objective, "objective");
            Scribe_Values.Look(ref objectiveSpawned, "objectiveSpawned");
            Scribe_Values.Look(ref hostageFreed, "hostageFreed");
        }

        public string GetUniqueLoadID() => "RPC_Gig_" + id;

        public string TypeLabel => ("RPC_Gig_Type_" + type).Translate();

        public string IssuerLabel => issuer?.Name ?? "RPC_Gig_AnonymousClient".Translate();
    }

    /// <summary>
    /// Gigs MVP: a contact (Gigs tab, needs a powered comms console) offers jobs against corporations and gangs. Accepting
    /// puts a guarded site on the world map with the objective on it. Pay is dropped at home. The full fixer system is
    /// cycle 2: there's one anonymous contact for now, and a gig is just this state plus the site.
    /// </summary>
    public class GameComponent_Gigs : GameComponent
    {
        private List<Gig> gigs = new List<Gig>();
        private int nextId = 1;
        private int nextOfferTick = -1;

        public static GameComponent_Gigs Instance => Current.Game?.GetComponent<GameComponent_Gigs>();

        public static GigTuningDef Tuning => DefDatabase<GigTuningDef>.GetNamedSilentFail("RPC_GigTuning");

        public IReadOnlyList<Gig> Gigs => gigs;

        public GameComponent_Gigs(Game game) { }

        // ---------------------------------------------------------------- targets

        private static bool IsCorpOrGang(Faction f) => f != null && !f.defeated && !f.IsPlayer && f.Ext()?.kind is CyberFactionKind.Corporation or CyberFactionKind.Gang;

        /// <summary>Only factions already hostile to the player: taking a contract shouldn't start a new war by itself.</summary>
        public static List<Faction> TargetCandidates() =>
            Find.FactionManager.AllFactionsListForReading.Where(f => IsCorpOrGang(f) && f.HostileTo(Faction.OfPlayer) && BestKind(f) != null).ToList();

        private static PawnKindDef BestKind(Faction f) =>
            DefDatabase<PawnKindDef>.AllDefsListForReading.Where(k => k.defaultFactionDef == f.def && k.RaceProps?.Humanlike == true && !k.defName.EndsWith("Trader"))
                .OrderByDescending(k => k.combatPower).ThenBy(k => k.defName).FirstOrDefault();

        /// <summary>A contact with a known client, if one is on speaking terms with the player and wants this target hurt.</summary>
        private static Faction PickIssuer(Faction target)
        {
            var options = Find.FactionManager.AllFactionsListForReading
                .Where(f => f != target && IsCorpOrGang(f) && !f.HostileTo(Faction.OfPlayer)).ToList();
            var rivals = options.Where(f => f.HostileTo(target)).ToList();
            if (rivals.Count > 0) return rivals.RandomElement();
            return options.Count > 0 && Rand.Chance(0.5f) ? options.RandomElement() : null;
        }

        // ---------------------------------------------------------------- offers

        public Gig TryAddOffer(GigType? type = null, Faction forcedTarget = null)
        {
            var tuning = Tuning;
            var candidates = forcedTarget != null ? new List<Faction> { forcedTarget } : TargetCandidates();
            if (tuning == null || candidates.Count == 0 || Find.AnyPlayerHomeMap == null)
            {
                Diag.Event("gigs", $"no offer: {candidates.Count} hostile corp/gang targets");
                return null;
            }
            var target = candidates.RandomElement();
            var t = type ?? (GigType)Rand.RangeInclusive(0, 3);
            float points = StorytellerUtility.DefaultThreatPointsNow(Find.AnyPlayerHomeMap);
            int reward = Mathf.RoundToInt(Mathf.Clamp(points * tuning.rewardPerThreatPoint * tuning.RewardFactor(t), tuning.rewardClamp.min, tuning.rewardClamp.max) / 10f) * 10;
            var gig = new Gig
            {
                id = nextId++,
                type = t,
                target = target,
                issuer = PickIssuer(target),
                reward = reward,
                offerExpiresTick = Find.TickManager.TicksGame + Mathf.RoundToInt(tuning.offerExpiryDays * GenDate.TicksPerDay),
            };
            gigs.Add(gig);
            Messages.Message("RPC_Gig_NewOffer".Translate(gig.TypeLabel, target.Name, reward), MessageTypeDefOf.NeutralEvent, false);
            BarkUtility.SayColonist(BarkKeys.GigOffered);
            Diag.Event("gigs", $"offer #{gig.id}: {t} vs {target.Name} for {gig.IssuerLabel}, {reward} silver");
            return gig;
        }

        public static bool HasContact()
        {
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (var b in map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.CommsConsole))
                {
                    var power = b.TryGetComp<CompPowerTrader>();
                    if (power == null || power.PowerOn) return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- player actions

        [SyncMethod]
        public static void AcceptGig(int id)
        {
            var inst = Instance;
            var gig = inst?.gigs.FirstOrDefault(g => g.id == id);
            if (gig == null || gig.state != GigState.Offered) return;
            if (!inst.TryMakeSite(gig))
            {
                Messages.Message("RPC_Gig_NoSite".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            gig.state = GigState.Active;
            gig.deadlineTick = Find.TickManager.TicksGame + Mathf.RoundToInt(Tuning.deadlineDays * GenDate.TicksPerDay);
            Find.LetterStack.ReceiveLetter("RPC_Gig_AcceptedLetter".Translate(gig.TypeLabel),
                "RPC_Gig_AcceptedText".Translate(gig.TypeLabel, gig.target.Name, gig.reward, Tuning.deadlineDays.ToString("0"), ("RPC_Gig_Goal_" + gig.type).Translate()),
                LetterDefOf.NeutralEvent, gig.site);
            BarkUtility.SayColonist(BarkKeys.GigAccepted);
            Diag.Event("gigs", $"accepted #{gig.id} {gig.type} vs {gig.target.Name}, site at tile {gig.site.Tile}");
        }

        [SyncMethod]
        public static void DeclineOrAbandonGig(int id)
        {
            var inst = Instance;
            var gig = inst?.gigs.FirstOrDefault(g => g.id == id);
            if (gig == null) return;
            if (gig.state == GigState.Offered) inst.gigs.Remove(gig);
            else if (gig.state == GigState.Active) inst.Finish(gig, false);
        }

        private bool TryMakeSite(Gig gig)
        {
            var tuning = Tuning;
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, tuning.siteDistance.min, tuning.siteDistance.max)) return false;
            float points = Mathf.Max(tuning.minSiteThreat, StorytellerUtility.DefaultThreatPointsNow(Find.AnyPlayerHomeMap) * tuning.siteThreatFactor);
            var site = SiteMaker.MakeSite(new[] { tuning.basePart, tuning.objectivePart }, tile, gig.target, threatPoints: points);
            if (site == null) return false;
            site.customLabel = "RPC_Gig_SiteLabel".Translate(gig.TypeLabel, gig.target.Name);
            Find.WorldObjects.Add(site);
            gig.site = site;
            return true;
        }

        // ---------------------------------------------------------------- objective

        /// <summary>From the site part's map generation.</summary>
        public void Notify_SiteMapGenerated(Map map)
        {
            var gig = gigs.FirstOrDefault(g => g.state == GigState.Active && g.site != null && g.site == map.Parent);
            if (gig != null && !gig.objectiveSpawned) SpawnObjective(gig, map);
        }

        public Thing ObjectiveOnMap(Map map) => gigs.FirstOrDefault(g => g.state == GigState.Active && g.objective != null && g.objective.MapHeld == map)?.objective;

        public Gig GigForHostage(Pawn pawn) => gigs.FirstOrDefault(g => g.state == GigState.Active && g.type == GigType.Rescue && g.objective == pawn && !g.hostageFreed);

        public static bool TryFindSpot(Map map, IntVec2 size, out IntVec3 cell)
        {
            bool Fits(IntVec3 c)
            {
                foreach (var o in GenAdj.OccupiedRect(c, Rot4.North, size))
                    if (!o.InBounds(map) || !o.Standable(map) || o.GetEdifice(map) != null || o.GetFirstPawn(map) != null) return false;
                return true;
            }
            // base first: the middle of the map is where the outpost is
            return CellFinder.TryFindRandomCellNear(map.Center, map, 25, Fits, out cell)
                   || CellFinder.TryFindRandomCellNear(map.Center, map, 60, Fits, out cell);
        }

        private void SpawnObjective(Gig gig, Map map)
        {
            var tuning = Tuning;
            gig.objectiveSpawned = true;
            var size = gig.type == GigType.Sabotage ? tuning.serverRack.size : IntVec2.One;
            if (!TryFindSpot(map, size, out var cell))
            {
                Diag.Check("gigs", $"gig #{gig.id}: no place for the {gig.type} objective on {map}");
                Finish(gig, false);
                return;
            }
            switch (gig.type)
            {
                case GigType.Assassinate:
                {
                    var mark = PawnGenerator.GeneratePawn(new PawnGenerationRequest(BestKind(gig.target), gig.target, mustBeCapableOfViolence: true));
                    GenSpawn.Spawn(mark, cell, map);
                    var lord = map.lordManager.lords.FirstOrDefault(l => l.faction == gig.target);
                    if (lord != null) lord.AddPawn(mark);
                    else LordMaker.MakeNewLord(gig.target, new LordJob_DefendPoint(cell), map, new[] { mark });
                    gig.objective = mark;
                    break;
                }
                case GigType.Sabotage:
                {
                    var rack = ThingMaker.MakeThing(tuning.serverRack);
                    rack.SetFaction(gig.target);
                    gig.objective = GenSpawn.Spawn(rack, cell, map);
                    break;
                }
                case GigType.Steal:
                    gig.objective = GenSpawn.Spawn(ThingMaker.MakeThing(tuning.dataShard), cell, map);
                    break;
                case GigType.Rescue:
                {
                    var kind = PawnKindDefOf.Villager;
                    var hostage = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, gig.issuer, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true, mustBeCapableOfViolence: false));
                    GenSpawn.Spawn(hostage, cell, map);
                    hostage.health.AddHediff(tuning.restrained); // restrained: can't move, so can't wander into the crossfire
                    gig.objective = hostage;
                    break;
                }
            }
            Diag.Event("gigs", $"gig #{gig.id}: {gig.type} objective {gig.objective} at {cell} on {map}");
        }

        /// <summary>Local test: run the objective on the current map with a small guard, no travel needed (debug action).</summary>
        public Gig Debug_RunHere(GigType type, Map map)
        {
            var candidates = TargetCandidates();
            var target = candidates.Count > 0 ? candidates.RandomElement()
                : Find.FactionManager.AllFactionsListForReading.Where(f => IsCorpOrGang(f) && BestKind(f) != null).RandomElementWithFallback();
            if (target == null) return null;
            var gig = TryAddOffer(type, target);
            if (gig == null) return null;
            gig.state = GigState.Active;
            gig.localMap = map;
            gig.deadlineTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 3;
            SpawnObjective(gig, map);
            return gig;
        }

        public void Debug_Complete(Gig gig) => Finish(gig, true);

        /// <summary>From the free-hostage job.</summary>
        public void Notify_HostageFreed(Pawn hostage, Pawn by)
        {
            var gig = GigForHostage(hostage);
            if (gig == null) return;
            var restraint = hostage.health.hediffSet.GetFirstHediffOfDef(Tuning.restrained);
            if (restraint != null) hostage.health.RemoveHediff(restraint);
            hostage.guest?.SetGuestStatus(null);
            hostage.SetFaction(Faction.OfPlayer, by);
            gig.hostageFreed = true;
            Diag.Event("gigs", $"gig #{gig.id}: {Diag.Describe(hostage)} freed by {Diag.Describe(by)}");
            Finish(gig, true);
        }

        /// <summary>Home, or in a caravan. A gig run on the home map by the debug action needs it picked up first.</summary>
        private static bool SafelyHeld(Gig gig, Thing t)
        {
            if (t == null || t.Destroyed) return false;
            if (gig.localMap != null && t.Spawned) return false;
            if (t.MapHeld?.IsPlayerHome == true) return true;
            return ThingOwnerUtility.AnyParentIs<Caravan>(t);
        }

        /// <summary>Null = still going.</summary>
        private static bool? Outcome(Gig gig)
        {
            var o = gig.objective;
            if (gig.objectiveSpawned)
            {
                switch (gig.type)
                {
                    case GigType.Assassinate:
                        if (o is Pawn mark && mark.Dead) return true;
                        break;
                    case GigType.Sabotage:
                        if (o == null || o.Destroyed) return true;
                        break;
                    case GigType.Steal:
                        if (SafelyHeld(gig, o)) return true;
                        if (o == null || o.Destroyed) return false;
                        break;
                    case GigType.Rescue:
                        if (o is Pawn hostage && hostage.Dead) return false;
                        break;
                }
            }
            // left the site without finishing: the map and everything on it is gone
            if (gig.site != null && gig.site.Destroyed) return false;
            if (Find.TickManager.TicksGame > gig.deadlineTick && (gig.site == null ? gig.localMap == null : !gig.site.HasMap)) return false;
            return null;
        }

        private void Finish(Gig gig, bool success)
        {
            var tuning = Tuning;
            gig.state = success ? GigState.Completed : GigState.Failed;
            if (gig.site != null && !gig.site.Destroyed && !gig.site.HasMap) gig.site.Destroy();
            if (success)
            {
                var home = Find.AnyPlayerHomeMap;
                if (home != null && gig.reward > 0)
                {
                    var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silver.stackCount = gig.reward;
                    DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(home), home, new[] { silver }, canRoofPunch: false);
                }
                else Diag.Check("gigs", $"gig #{gig.id}: completed with no home map, {gig.reward} silver not delivered");
                gig.issuer?.TryAffectGoodwillWith(Faction.OfPlayer, tuning.goodwillGain, canSendMessage: true, canSendHostilityLetter: true);
                gig.target?.TryAffectGoodwillWith(Faction.OfPlayer, tuning.goodwillLossTarget, canSendMessage: false, canSendHostilityLetter: false);
                Find.LetterStack.ReceiveLetter("RPC_Gig_CompletedLetter".Translate(gig.TypeLabel),
                    "RPC_Gig_CompletedText".Translate(gig.TypeLabel, gig.target?.Name ?? "?", gig.reward, gig.IssuerLabel), LetterDefOf.PositiveEvent);
                BarkUtility.SayColonist(BarkKeys.GigCompleted);
            }
            else
            {
                gig.issuer?.TryAffectGoodwillWith(Faction.OfPlayer, tuning.goodwillLossFail, canSendMessage: true, canSendHostilityLetter: true);
                Find.LetterStack.ReceiveLetter("RPC_Gig_FailedLetter".Translate(gig.TypeLabel),
                    "RPC_Gig_FailedText".Translate(gig.TypeLabel, gig.target?.Name ?? "?", gig.IssuerLabel), LetterDefOf.NegativeEvent);
                BarkUtility.SayColonist(BarkKeys.GigFailed);
            }
            gig.objective = null; // don't hold a reference to whatever's left on a map that may go away
            Diag.Event("gigs", $"gig #{gig.id} {gig.type} {(success ? "COMPLETED" : "FAILED")}");
        }

        // ---------------------------------------------------------------- tick

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 0) return;
            var tuning = Tuning;
            if (tuning == null) return;

            for (int i = gigs.Count - 1; i >= 0; i--)
            {
                var g = gigs[i];
                if (g.state == GigState.Offered && now > g.offerExpiresTick) gigs.RemoveAt(i);
                else if (g.state == GigState.Active)
                {
                    bool? outcome = Outcome(g);
                    if (outcome.HasValue) Finish(g, outcome.Value);
                }
            }
            // finished gigs stay on the board for a while as a record, then go
            int keep = GenDate.TicksPerDay * 5;
            gigs.RemoveAll(g => g.state >= GigState.Completed && now > g.deadlineTick + keep);

            if (nextOfferTick < 0) nextOfferTick = now + Mathf.RoundToInt(tuning.daysBetweenOffers.RandomInRange * GenDate.TicksPerDay);
            if (now >= nextOfferTick)
            {
                nextOfferTick = now + Mathf.RoundToInt(tuning.daysBetweenOffers.RandomInRange * GenDate.TicksPerDay);
                if (gigs.Count(g => g.state == GigState.Offered) < tuning.maxOffers) TryAddOffer();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref gigs, "gigs", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "nextId", 1);
            Scribe_Values.Look(ref nextOfferTick, "nextOfferTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) gigs ??= new List<Gig>();
        }
    }

    /// <summary>Added next to the outpost part: spawns the gig's objective once the outpost is on the map.</summary>
    public class SitePartWorker_GigObjective : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            GameComponent_Gigs.Instance?.Notify_SiteMapGenerated(map);
        }

        public override string GetArrivedLetterPart(Map map, out LetterDef preferredLetterDef, out LookTargets lookTargets)
        {
            string text = base.GetArrivedLetterPart(map, out preferredLetterDef, out lookTargets);
            var objective = GameComponent_Gigs.Instance?.ObjectiveOnMap(map);
            if (objective != null)
            {
                lookTargets = new LookTargets(objective);
                text = text.NullOrEmpty() ? "RPC_Gig_ObjectiveHere".Translate(objective.LabelShort).Resolve() : text + "\n\n" + "RPC_Gig_ObjectiveHere".Translate(objective.LabelShort);
            }
            return text;
        }
    }

    /// <summary>Walk to the hostage, cut them loose.</summary>
    public class JobDriver_FreeHostage : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.WaitWith(TargetIndex.A, 180, useProgressBar: true);
            yield return new Toil
            {
                initAction = () => GameComponent_Gigs.Instance?.Notify_HostageFreed((Pawn)job.targetA.Thing, pawn),
                defaultCompleteMode = ToilCompleteMode.Instant,
            };
        }
    }

    /// <summary>Right-click the hostage to free them.</summary>
    public class FloatMenuOptionProvider_FreeHostage : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            if (GameComponent_Gigs.Instance?.GigForHostage(clickedPawn) == null) return null;
            var pawn = context.FirstSelectedPawn;
            string label = "RPC_Gig_FreeHostage".Translate(clickedPawn.LabelShort);
            if (!pawn.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly)) return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
            return new FloatMenuOption(label, () => pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(GameComponent_Gigs.Tuning.freeHostageJob, clickedPawn), JobTag.Misc));
        }
    }

    /// <summary>The contact: open offers and running contracts.</summary>
    public class MainTabWindow_Gigs : MainTabWindow
    {
        private const float RowHeight = 62f;
        private Vector2 scroll;

        public override Vector2 RequestedTabSize => new Vector2(760f, 480f);

        public override void DoWindowContents(Rect rect)
        {
            var inst = GameComponent_Gigs.Instance;
            if (inst == null) return;
            bool contact = GameComponent_Gigs.HasContact();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), "RPC_Gig_Title".Translate());
            Text.Font = GameFont.Small;
            GUI.color = contact ? Color.white : new Color(1f, 0.6f, 0.4f);
            Widgets.Label(new Rect(rect.x, rect.y + 34f, rect.width, 24f), contact ? "RPC_Gig_ContactOnline".Translate() : "RPC_Gig_NeedsComms".Translate());
            GUI.color = Color.white;

            var list = inst.Gigs.OrderBy(g => g.state == GigState.Active ? 0 : g.state == GigState.Offered ? 1 : 2).ThenByDescending(g => g.id).ToList();
            var outer = new Rect(rect.x, rect.y + 64f, rect.width, rect.height - 64f);
            if (list.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(outer, "RPC_Gig_None".Translate());
                GUI.color = Color.white;
                return;
            }
            var view = new Rect(0f, 0f, outer.width - 16f, list.Count * RowHeight);
            Widgets.BeginScrollView(outer, ref scroll, view);
            float y = 0f;
            foreach (var g in list)
            {
                DrawRow(new Rect(0f, y, view.width, RowHeight - 4f), g, contact);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private static void DrawRow(Rect r, Gig g, bool contact)
        {
            Widgets.DrawOptionBackground(r, false);
            var text = r.ContractedBy(6f);
            text.width -= 210f;
            string status = ("RPC_Gig_State_" + g.state).Translate();
            Widgets.Label(new Rect(text.x, text.y, text.width, 24f), $"<b>{g.TypeLabel}</b>: {g.target?.Name ?? "?"}  ({status})");
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            int now = Find.TickManager.TicksGame;
            string time = g.state == GigState.Offered ? "RPC_Gig_OfferEnds".Translate((g.offerExpiresTick - now).ToStringTicksToPeriod()).Resolve()
                : g.state == GigState.Active ? "RPC_Gig_DueIn".Translate(Mathf.Max(0, g.deadlineTick - now).ToStringTicksToPeriod()).Resolve() : "";
            Widgets.Label(new Rect(text.x, text.y + 24f, text.width, 24f), "RPC_Gig_Row".Translate(g.IssuerLabel, g.reward) + (time.NullOrEmpty() ? "" : "  " + time));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(r, ("RPC_Gig_Goal_" + g.type).Translate());

            var btn = new Rect(r.xMax - 206f, r.y + 14f, 98f, 30f);
            var btn2 = new Rect(r.xMax - 102f, r.y + 14f, 98f, 30f);
            if (g.state == GigState.Offered)
            {
                if (contact && Widgets.ButtonText(btn, "RPC_Gig_Accept".Translate())) GameComponent_Gigs.AcceptGig(g.id);
                if (Widgets.ButtonText(btn2, "RPC_Gig_Decline".Translate())) GameComponent_Gigs.DeclineOrAbandonGig(g.id);
            }
            else if (g.state == GigState.Active)
            {
                if (g.site != null && Widgets.ButtonText(btn, "RPC_Gig_Show".Translate()))
                {
                    CameraJumper.TryJumpAndSelect(g.site);
                    Find.MainTabsRoot.EscapeCurrentTab(false);
                }
                if (Widgets.ButtonText(btn2, "RPC_Gig_Abandon".Translate())) GameComponent_Gigs.DeclineOrAbandonGig(g.id);
            }
        }
    }
}
