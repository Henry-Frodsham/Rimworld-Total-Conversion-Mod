using System.Collections.Generic;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimPunkCE
{
    /// <summary>
    /// One-click test setups for the systems tuned in live tests (Debug actions menu, "RimPunk CE: test scenarios").
    /// Results go to diag.log and the message feed, so the log monitor picks them up.
    /// </summary>
    public static class RimPunkDebugActions_Scenarios
    {
        private const string Cat = "RimPunk CE: test scenarios";

        private static void Report(string category, string text)
        {
            Diag.Event(category, text);
            Log.Message("[RimPunk CE] " + text);
        }

        // ---------------------------------------------------------------- smart weapons

        [DebugAction(Cat, "Smart weapon range (link + G-58 + target 12 cells)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SmartRange(Pawn p)
        {
            var link = DefDatabase<HediffDef>.GetNamedSilentFail("RPC_SmartLink");
            if (link != null && !SmartLinkUtility.HasSmartLink(p)) RimPunkDebugActions.DebugInstall(p, link, CyberTier.Iconic);
            var gun = DefDatabase<ThingDef>.GetNamedSilentFail("RPC_Gun_KangTaoG58Dian");
            if (gun != null) RimPunkDebugActions_Testing.Equip(p, gun);

            // an unarmed hostile 12 cells away, in line of sight if possible
            var map = p.Map;
            IntVec3 cell = IntVec3.Invalid;
            foreach (var dir in GenRadial.RadialPattern.Take(400))
            {
                var c = p.Position + dir;
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) continue;
                float d = c.DistanceTo(p.Position);
                if (d < 11.5f || d > 13f || !GenSight.LineOfSight(p.Position, c, map)) continue;
                cell = c;
                break;
            }
            if (!cell.IsValid)
            {
                Messages.Message("No open cell 12 tiles away in sight", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var hostile = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false) ?? Find.FactionManager.OfAncientsHostile;
            var target = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, hostile, mustBeCapableOfViolence: false));
            target.equipment?.DestroyAllEquipment();
            GenSpawn.Spawn(target, cell, map);

            if (p.drafter != null && p.IsColonistPlayerControlled) p.drafter.Drafted = true;
            var job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            job.playerForced = true;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
            Report("weapons", $"smart range: {Diag.Describe(p)} linked={SmartLinkUtility.HasSmartLink(p)} firing {gun?.defName} at {Diag.Describe(target)} {p.Position.DistanceTo(cell):0.#} cells away");
        }

        // ---------------------------------------------------------------- cycle 1

        [DebugAction(Cat, "Log part plating (armor / health per part)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogPlating(Pawn p)
        {
            var sb = new StringBuilder();
            sb.Append($"{Diag.Describe(p)} role {ContextualCombat.RoleOf(p)}, pawn-wide armor {p.GetStatValue(StatDefOf.ArmorRating_Sharp):0.#} mm / {p.GetStatValue(StatDefOf.ArmorRating_Blunt):0.#} MPa:");
            foreach (var part in p.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside))
            {
                ChromePlatingUtility.Totals(p, part, out float sharp, out float blunt, out float health);
                sb.Append($"\n  {part.Label}: +{sharp:0.#} mm, +{blunt:0.#} MPa, health x{health:0.##}");
            }
            Report("plating", sb.ToString());
        }

        [DebugAction(Cat, "Netrunner duel (two T5 decks, hostile, 12 cells)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void NetrunnerDuel(Pawn p)
        {
            LearnAllAt(p, CyberTier.T5);
            var hostile = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false) ?? Find.FactionManager.OfAncientsHostile;
            var rival = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, hostile, mustBeCapableOfViolence: true));
            if (!CellFinder.TryFindRandomCellNear(p.Position, p.Map, 14, c => c.Standable(p.Map) && c.DistanceTo(p.Position) > 9f && GenSight.LineOfSight(p.Position, c, p.Map), out var cell))
            {
                Messages.Message("No open cell near the pawn", MessageTypeDefOf.RejectInput, false);
                return;
            }
            GenSpawn.Spawn(rival, cell, p.Map);
            LearnAllAt(rival, CyberTier.T5);
            rival.mindState.enemyTarget = p;
            LordMaker.MakeNewLord(hostile, new LordJob_AssaultColony(hostile, canKidnap: false, canTimeoutOrFlee: false), p.Map, new[] { rival });
            Report("quickhack", $"netrunner duel: {Diag.Describe(p)} vs {Diag.Describe(rival)} at {cell.DistanceTo(p.Position):0.#} cells");
        }

        [DebugAction(Cat, "Adrenaline: burst, then 40 damage dealt + taken", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AdrenalineTest(Pawn p)
        {
            var t = p.GetCyberTracker();
            if (t == null) return;
            t.SetAdrenaline(0f);
            t.Notify_CombatAction();
            float a0 = t.Adrenaline;
            var dinfo = new DamageInfo(DamageDefOf.Blunt, 40f);
            t.Notify_DamageForAdrenaline(dinfo, 40f, dealt: true);
            float a1 = t.Adrenaline;
            t.Notify_DamageForAdrenaline(dinfo, 40f, dealt: false);
            Report("adrenaline", $"{Diag.Describe(p)}: entered combat {a0:0.#}, after dealing 40 {a1:0.#}, after taking 40 {t.Adrenaline:0.#} / {t.AdrenalineMax:0}");
        }

        [DebugAction(Cat, "Gig: new offer of each type", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void GigOffers()
        {
            foreach (GigType t in System.Enum.GetValues(typeof(GigType))) GameComponent_Gigs.Instance?.TryAddOffer(t);
        }

        [DebugAction(Cat, "Gig: run objective on this map...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> GigRunHere()
        {
            var nodes = new List<DebugActionNode>();
            foreach (GigType t in System.Enum.GetValues(typeof(GigType)))
                nodes.Add(new DebugActionNode(t.ToString(), DebugActionType.Action, () =>
                {
                    var gig = GameComponent_Gigs.Instance?.Debug_RunHere(t, Find.CurrentMap);
                    Report("gigs", gig == null ? "couldn't start a gig (no corp or gang faction?)" : $"gig #{gig.id} {t} running here, objective {gig.objective}");
                }));
            return nodes;
        }

        [DebugAction(Cat, "Gig: complete all active gigs", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void GigComplete()
        {
            var inst = GameComponent_Gigs.Instance;
            if (inst == null) return;
            foreach (var g in inst.Gigs.Where(g => g.state == GigState.Active).ToList()) inst.Debug_Complete(g);
        }

        [DebugAction(Cat, "Gig: log the board", allowedGameStates = AllowedGameStates.Playing)]
        private static void GigLog()
        {
            var inst = GameComponent_Gigs.Instance;
            if (inst == null) return;
            Report("gigs", $"{inst.Gigs.Count} gigs, {GameComponent_Gigs.TargetCandidates().Count} targets, contact {GameComponent_Gigs.HasContact()}: " +
                           string.Join("; ", inst.Gigs.Select(g => $"#{g.id} {g.type} {g.state} vs {g.target?.Name} for {g.IssuerLabel}, {g.reward}")));
        }

        // ---------------------------------------------------------------- capacity

        [DebugAction(Cat, "Set capacity usage to...", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SetUsage()
        {
            var nodes = new List<DebugActionNode>();
            foreach (float pct in new[] { 0.4f, 0.55f, 0.79f, 0.85f, 1.5f })
                nodes.Add(new DebugActionNode(pct.ToStringPercent(), DebugActionType.ToolMapForPawns, pawnAction: p => SetUsageTo(p, pct)));
            return nodes;
        }

        /// <summary>Adjusts the rolled base capacity so used / max hits the target (max is linear in base unless Gonk).</summary>
        private static void SetUsageTo(Pawn p, float target)
        {
            var t = p.GetCyberTracker();
            if (t == null) return;
            if (t.CapacityUsed <= 0f)
            {
                Messages.Message("No cyberware installed: usage is 0% whatever the capacity", MessageTypeDefOf.RejectInput, false);
                return;
            }
            // max = (base + offsets) * multiplier: measure the slope, then solve for the base
            float wantMax = t.CapacityUsed / target;
            float b0 = t.BaseCapacity, m0 = t.CapacityMax;
            t.Debug_SetBaseCapacity(b0 + 10f);
            float slope = (t.CapacityMax - m0) / 10f;
            if (slope > 0.001f) t.Debug_SetBaseCapacity(b0 + (wantMax - m0) / slope);
            else t.Debug_SetBaseCapacity(b0); // Gonk: capacity is fixed, nothing to solve
            Report("capacity", $"{Diag.Describe(p)}: base {t.BaseCapacity:0}, used {t.CapacityUsed:0.#} / {t.CapacityMax:0.#} = {t.UsageRatio:P0} (wanted {target:P0}{(AptitudeUtility.IsGonk(p) ? ", Gonk: fixed capacity" : "")})");
        }

        // ---------------------------------------------------------------- downed / gear recovery

        [DebugAction(Cat, "Down pawn (keeps mental state)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DownPawn(Pawn p)
        {
            if (!p.Downed) HealthUtility.DamageUntilDowned(p, allowBleedingWounds: false);
        }

        [DebugAction(Cat, "Heal fully (get back up)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void HealFully(Pawn p)
        {
            foreach (var h in p.health.hediffSet.hediffs.Where(h => h is Hediff_Injury || h.def == HediffDefOf.BloodLoss).ToList())
                p.health.RemoveHediff(h);
        }

        // ---------------------------------------------------------------- loadouts

        [DebugAction(Cat, "Add max cyberware to pawn (every slot, top tier)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void MaxCyberware(Pawn p)
        {
            var added = new List<string>();
            foreach (var slot in DefDatabase<CyberSlotDef>.AllDefsListForReading)
            {
                // best first: highest top tier, then the most capacity (the strongest pieces)
                var options = CyberwareDefCache.AllCyberware.Where(d => d.Ext()?.slot == slot)
                    .OrderByDescending(d => d.Ext().maxTier).ThenByDescending(d => d.Ext().capacityCost).ThenBy(d => d.defName).ToList();
                for (int guard = 0; guard < 12; guard++)
                {
                    HediffDef pick = null;
                    foreach (var d in options)
                    {
                        bool fits = slot.WholeBody
                            ? CyberUtility.CanInstall(p, d, null, out _)
                            : CyberSurgeryUtility.CandidateParts(p, slot).Any(part => CyberUtility.CanInstall(p, d, part, out _));
                        if (fits) { pick = d; break; }
                    }
                    if (pick == null) break;
                    RimPunkDebugActions.DebugInstall(p, pick, pick.Ext().maxTier);
                    added.Add($"{pick.defName} {pick.Ext().maxTier}");
                }
            }
            var tracker = p.GetCyberTracker();
            tracker?.Notify_CyberwareChanged();
            Report("cyberware", $"debug: max cyberware on {Diag.Describe(p)}: {added.Count} pieces [{string.Join(", ", added)}], usage {tracker?.UsageRatio:P0}");
        }

        // ---------------------------------------------------------------- animations

        [DebugAction(Cat, "Play every cyberware animation (towards 4 cells east)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void PlayAnimations(Pawn p)
        {
            var target = new LocalTargetInfo((p.Position + new IntVec3(4, 0, 0)).ClampInsideMap(p.Map));
            var near = new LocalTargetInfo((p.Position + new IntVec3(1, 0, 0)).ClampInsideMap(p.Map));
            MapComponent_CyberAnimations.Play(p, CyberAnimKind.BladeSlash, near, MapComponent_CyberAnimations.Steel, 1f);
            MapComponent_CyberAnimations.Play(p, CyberAnimKind.BladeSlash, near, MapComponent_CyberAnimations.Thermal, -1f, duration: 30);
            MapComponent_CyberAnimations.Play(p, CyberAnimKind.Punch, near, MapComponent_CyberAnimations.Electric, duration: 40);
            MapComponent_CyberAnimations.Play(p, CyberAnimKind.WhipLash, target, MapComponent_CyberAnimations.Toxic, duration: 50);
            MapComponent_CyberAnimations.Play(p, CyberAnimKind.LauncherFire, target, UnityEngine.Color.white, duration: 60);
            MapComponent_CyberAnimations.Play(p, CyberAnimKind.DashStreak, target, MapComponent_CyberAnimations.Steel, duration: 70);
            Report("visuals", $"debug: playing every cyberware animation on {Diag.Describe(p)} (needs the visual effects setting on)");
        }

        // ---------------------------------------------------------------- arm launcher

        [DebugAction(Cat, "Arm launcher: install T3 + 20 of each shell", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LauncherKit(Pawn p)
        {
            var pls = DefDatabase<HediffDef>.GetNamedSilentFail("RPC_ProjectileLaunchSystem");
            if (pls != null && ArmLauncherUtility.Magazine(p) == null) RimPunkDebugActions.DebugInstall(p, pls, CyberTier.T3);
            var mag = ArmLauncherUtility.Magazine(p);
            if (mag == null)
            {
                // say why (usually both arm slots already taken, e.g. mantis blades + gorilla arms)
                string why = "no arm";
                if (pls != null)
                    foreach (var part in CyberSurgeryUtility.CandidateParts(p, pls.Ext()?.slot))
                        if (!CyberUtility.CanInstall(p, pls, part, out why)) break;
                Report("cyberware", $"debug: couldn't install the launcher on {Diag.Describe(p)}: {why}");
                return;
            }
            foreach (var a in mag.Props.ammo) ArmLauncherUtility.GiveToInventory(p, a, 20);
            mag.TryStartReload(queue: false);
            Report("cyberware", $"debug: {Diag.Describe(p)} has the launcher ({mag.Capacity} shell magazine) and 20 of each shell, reloading");
        }

        // ---------------------------------------------------------------- trauma team

        [DebugAction(Cat, "Cyberpsycho -> Trauma extraction now (tests chrome strip)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PsychoExtraction(Pawn p)
        {
            if (!CyberpsychosisUtility.InCyberpsychosis(p)) CyberpsychosisUtility.TryStart(p, "debug");
            RimPunkDebugActions_Testing.GiveCoverage(p);
            if (!p.Downed) HealthUtility.DamageUntilDowned(p, allowBleedingWounds: false);
            Messages.Message("Trauma Team dispatched. Use 'Return extracted patients now' after the shuttle leaves.", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "Trauma Team: end hostility now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TraumaForgive()
        {
            if (!GameComponent_RimPunk.TraumaHostileToPlayer) { Report("trauma", "debug: Trauma Team isn't hostile"); return; }
            GameComponent_RimPunk.Instance.EndTraumaHostility();
            Report("trauma", "debug: Trauma Team hostility ended");
        }

        [DebugAction(Cat, "Trauma debt: add 1000", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebtAdd()
        {
            var inst = GameComponent_RimPunk.Instance;
            inst?.AddDebt(1000f);
            Report("trauma", $"debug: debt now {inst?.TraumaDebt:0}, collectors in {GameComponent_RimPunk.DaysUntilCollection:0.0} days");
        }

        [DebugAction(Cat, "Trauma debt: deadline in 1 hour", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebtDeadlineSoon()
        {
            var inst = GameComponent_RimPunk.Instance;
            if (inst == null || !GameComponent_RimPunk.PlayerInDebt) { Report("trauma", "debug: no debt (add some first)"); return; }
            inst.Debug_SetNextCollection(Find.TickManager.TicksGame + 2500);
            Report("trauma", "debug: collectors due in 2500 ticks (warning letters fire on the way)");
        }

        [DebugAction(Cat, "Trauma debt: send collectors now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebtCollectNow()
        {
            var inst = GameComponent_RimPunk.Instance;
            if (inst == null) return;
            if (!GameComponent_RimPunk.PlayerInDebt) inst.AddDebt(1000f);
            bool ok = inst.SendCollectors(GameComponent_RimPunk.Tuning);
            Report("trauma", $"debug: collectors {(ok ? "sent" : "FAILED (see diag.log)")}");
        }

        [DebugAction(Cat, "Trauma debt: run a day of interest", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebtInterest() => GameComponent_RimPunk.Instance?.Debug_DailyTick();

        [DebugAction(Cat, "Trauma debt: clear", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebtClear()
        {
            GameComponent_RimPunk.Instance?.ClearDebt();
            Report("trauma", "debug: debt cleared, collectors stood down");
        }

        [DebugAction(Cat, "Trauma bill: preview for pawn", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BillPreview(Pawn p)
        {
            var t = GameComponent_RimPunk.Tuning;
            if (t == null) return;
            int treated = GameComponent_RimPunk.CountConditions(p);
            var bill = TraumaBill.For(t, treated, 0, CyberpsychosisUtility.InCyberpsychosis(p));
            Report("trauma", $"bill preview for {Diag.Describe(p)} (no combat): {bill.Lines().Replace("\n", " / ")}");
        }

        // ---------------------------------------------------------------- inspection

        [DebugAction(Cat, "Dump RimPunk state", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Dump(Pawn p)
        {
            var sb = new StringBuilder();
            var t = p.GetCyberTracker();
            sb.Append($"{Diag.Describe(p)}");
            if (t != null)
            {
                sb.Append($" | capacity base {t.BaseCapacity:0}, used {t.CapacityUsed:0.#} / {t.CapacityMax:0.#} = {t.UsageRatio:P0}");
                sb.Append($" | gonk={AptitudeUtility.IsGonk(p)} burnout={CyberpsychosisUtility.HasBurnout(p)}");
                sb.Append(" | cyberware [" + string.Join(", ", t.Cyberware.Select(c => $"{c.def.defName} {c.tier} ({c.CapacityCost:0.#}) on {c.Part?.def.defName ?? "body"}")) + "]");
                sb.Append(" | foreign [" + string.Join(", ", t.ForeignImplants.Select(f => $"{f.def.defName} ({ForeignCyberwareUtility.CostOf(f):0.#}) on {f.Part?.def.defName ?? "body"}")) + "]");
            }
            sb.Append($" | mental {p.MentalStateDef?.defName ?? "none"}");
            if (p.MentalState is MentalState_Cyberpsychosis ms)
                sb.Append($" kills {ms.kills} maxTacCalled {ms.maxTacCalled} droppedWeapon {ms.droppedWeapon?.def.defName ?? "none"} droppedInv [{string.Join(", ", ms.droppedInventory.Select(d => d.thingDef.defName + " x" + d.count))}]");
            sb.Append($" | weapon {p.equipment?.Primary?.def.defName ?? "none"}");
            var melee = p.meleeVerbs?.GetUpdatedAvailableVerbsList(false);
            if (melee != null)
                sb.Append(" | melee [" + string.Join(", ", melee.Select(v => $"{v.verb.tool?.label ?? v.verb.verbProps.label}:{(v.verb.tool is CombatExtended.ToolCE ? "CE" : "NOT-CE")}{(v.verb.HediffSource != null ? "@" + v.verb.HediffSource.def.defName : "")}")) + "]");
            Report("debug", sb.ToString());
        }

        [DebugAction(Cat, "Log all foreign implant costs", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogForeignCosts()
        {
            var rows = DefDatabase<HediffDef>.AllDefsListForReading
                .Where(ForeignCyberwareUtility.IsForeignImplantDef)
                .Select(d => (def: d, cost: ForeignCyberwareUtility.CostOfDef(d, out string why), why))
                .OrderByDescending(r => r.cost).ToList();
            foreach (var r in rows)
                Diag.Event("capacity", $"foreign cost {r.cost,5:0.#}  {r.def.defName} ({r.def.modContentPack?.Name ?? "?"}): {r.why}");
            Report("capacity", $"logged {rows.Count} foreign implant costs to diag.log (highest first)");
        }

        [DebugAction(Cat, "Log implant melee tools not CE-ready", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogNonCeTools()
        {
            int n = 0;
            foreach (var hd in DefDatabase<HediffDef>.AllDefsListForReading)
                foreach (var vg in hd.comps?.OfType<HediffCompProperties_VerbGiver>() ?? Enumerable.Empty<HediffCompProperties_VerbGiver>())
                    foreach (var tool in vg.tools ?? new List<Tool>())
                        if (!(tool is CombatExtended.ToolCE))
                        {
                            n++;
                            Diag.Event("melee", $"not CE-ready: {hd.defName} tool {tool.label} ({hd.modContentPack?.Name ?? "?"})");
                        }
            Report("melee", n == 0 ? "every implant melee tool is CE-ready" : $"{n} implant melee tool(s) not CE-ready (see diag.log)");
        }

        // ---------------------------------------------------------------- quickhacks

        [DebugAction(Cat, "Learn all quickhacks at tier...", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> LearnAll()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var t in new[] { CyberTier.T1, CyberTier.T2, CyberTier.T3, CyberTier.T4, CyberTier.T5 })
                nodes.Add(new DebugActionNode(t.ToString(), DebugActionType.ToolMapForPawns, pawnAction: p => LearnAllAt(p, t)));
            return nodes;
        }

        internal static void LearnAllAt(Pawn p, CyberTier tier)
        {
            var tracker = p.GetCyberTracker();
            if (tracker == null) return;
            if (!tracker.HasCyberdeck)
            {
                var deck = DefDatabase<HediffDef>.GetNamedSilentFail("RPC_ArasakaCyberdeck");
                if (deck != null) RimPunkDebugActions.DebugInstall(p, deck, CyberTier.T5);
            }
            int n = 0;
            foreach (var d in QuickhackLoadoutUtility.AllQuickhacks)
                if (tracker.LearnQuickhack(d, tier)) n++;
            tracker.GainRam(9999f);
            Report("quickhack", $"{Diag.Describe(p)} learned {n} quickhacks at {tier} (deck: {tracker.HasCyberdeck}, RAM {tracker.Ram:0}/{tracker.RamMax:0})");
        }

        [DebugAction(Cat, "Quickhack test range (deck + all T5 + 3 targets)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void QuickhackRange(Pawn p)
        {
            LearnAllAt(p, CyberTier.T5);
            var map = p.Map;
            var hostile = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false) ?? Find.FactionManager.OfAncientsHostile;
            var kinds = new List<(PawnKindDef kind, Faction faction)>
            {
                (PawnKindDefOf.Villager, hostile),
                (DefDatabase<PawnKindDef>.GetNamedSilentFail("RPC_Maelstrom_Grunt") ?? PawnKindDefOf.Villager, hostile),
                (DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_Scyther"), Faction.OfMechanoids),
            };
            int i = 0;
            foreach (var (kind, faction) in kinds)
            {
                if (kind == null || faction == null) continue;
                var cell = CellFinder.StandableCellNear(p.Position + new IntVec3(10, 0, -3 + 3 * i++), map, 3f);
                if (!cell.IsValid) continue;
                var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, mustBeCapableOfViolence: false));
                pawn.equipment?.DestroyAllEquipment();
                GenSpawn.Spawn(pawn, cell, map);
                // stand there: no lord, no weapon
                pawn.jobs?.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 6000), JobCondition.InterruptForced);
            }
            Report("quickhack", $"quickhack range ready for {Diag.Describe(p)}: draft and use the quickhack row on the targets 10 tiles east");
        }

        [DebugAction(Cat, "Log quickhack table", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogQuickhacks()
        {
            foreach (var d in DefDatabase<AbilityDef>.AllDefsListForReading.Where(x => x.HasModExtension<QuickhackExtension>()))
            {
                var e = d.GetModExtension<QuickhackExtension>();
                var cells = new List<string>();
                for (var t = e.minTier; t <= e.maxTier; t++)
                    cells.Add($"{t}: ram {e.Eval(e.ram, t):0.#} up {e.Eval(e.upload, t):0.#}s dur {e.Eval(e.duration, t):0.#}s mag {e.Eval(e.magnitude, t):0.#}{(e.spread != null ? $" spread {e.Eval(e.spread, t):0}" : "")}");
                Diag.Event("quickhack", $"{d.defName} ({e.category}, cd {d.cooldownTicksRange.Average / 60f:0.#}s{(e.untraceable ? ", untraceable" : "")}): {string.Join(" | ", cells)}");
            }
            Report("quickhack", "logged the quickhack table to diag.log");
        }
    }
}
