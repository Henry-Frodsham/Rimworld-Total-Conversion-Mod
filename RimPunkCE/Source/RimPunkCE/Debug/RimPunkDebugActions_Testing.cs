using System.Collections.Generic;
using System.Linq;
using System.Text;
using CombatExtended;
using LudeonTK;
using RimWorld;
using RimPunkCE.Patches;
using Verse;
using Verse.AI.Group;

namespace RimPunkCE
{
    /// <summary>
    /// Live-test tools. Debug actions menu (dev mode) → categories "RimPunk CE: ...".
    /// Map tools act where you click; pawn tools act on the pawn you click.
    /// </summary>
    public static class RimPunkDebugActions_Testing
    {
        private const string Weapons = "RimPunk CE: weapons";
        private const string Cyber = "RimPunk CE: cyberware";
        private const string Factions = "RimPunk CE: factions";
        private const string Trauma = "RimPunk CE: trauma team";
        private const string Events = "RimPunk CE: events";
        private const string MaxTac = "RimPunk CE: maxtac";

        private static IEnumerable<ThingDef> RpcWeapons => DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.IsWeapon && d.defName.StartsWith("RPC_")).OrderBy(d => d.defName);

        private static IEnumerable<Faction> RpcFactions => Find.FactionManager.AllFactionsListForReading
            .Where(f => f.Ext() != null && !f.defeated).OrderBy(f => f.Name);

        // ================================================================ weapons

        private static void SpawnWeaponWithAmmo(ThingDef def, IntVec3 cell, Map map, int ammoStacks = 2)
        {
            var weapon = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            GenPlace.TryPlaceThing(weapon, cell, map, ThingPlaceMode.Near);
            foreach (var ammo in AmmoFor(def).Take(3))
            {
                for (int i = 0; i < ammoStacks; i++)
                {
                    var stack = ThingMaker.MakeThing(ammo);
                    stack.stackCount = System.Math.Min(ammo.stackLimit, 200);
                    GenPlace.TryPlaceThing(stack, cell, map, ThingPlaceMode.Near);
                }
            }
        }

        private static IEnumerable<ThingDef> AmmoFor(ThingDef weapon) =>
            weapon.GetCompProperties<CompProperties_AmmoUser>()?.ammoSet?.ammoTypes?.Select(l => (ThingDef)l.ammo).Where(a => a != null)
            ?? Enumerable.Empty<ThingDef>();

        [DebugAction(Weapons, "Spawn weapon + ammo...", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SpawnWeapon()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var def in RpcWeapons)
            {
                var d = def;
                nodes.Add(new DebugActionNode(d.defName, DebugActionType.ToolMap, () => SpawnWeaponWithAmmo(d, UI.MouseCell(), Find.CurrentMap)));
            }
            return nodes;
        }

        [DebugAction(Weapons, "Spawn ALL weapons + ammo (test armory)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnAllWeapons()
        {
            foreach (var def in RpcWeapons) SpawnWeaponWithAmmo(def, UI.MouseCell(), Find.CurrentMap, 1);
        }

        [DebugAction(Weapons, "Equip weapon (with ammo)...", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> EquipWeapon()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var def in RpcWeapons)
            {
                var d = def;
                nodes.Add(new DebugActionNode(d.defName, DebugActionType.ToolMapForPawns, pawnAction: p => Equip(p, d)));
            }
            return nodes;
        }

        private static void Equip(Pawn p, ThingDef def)
        {
            if (p.equipment == null) return;
            if (p.equipment.Primary != null) p.equipment.DestroyEquipment(p.equipment.Primary);
            var w = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            p.equipment.AddEquipment(w);
            foreach (var ammo in AmmoFor(def).Take(1))
            {
                var stack = ThingMaker.MakeThing(ammo);
                stack.stackCount = System.Math.Min(ammo.stackLimit, 150);
                p.inventory?.innerContainer.TryAdd(stack);
            }
        }

        [DebugAction(Weapons, "Log gun balance table", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogGuns()
        {
            var sb = new StringBuilder("[RimPunk CE] guns: name | verb | range | burst | mag | reload | first ammo dmg / penS / penB\n");
            foreach (var def in RpcWeapons.Where(d => d.IsRangedWeapon && d.defName.StartsWith("RPC_Gun_")))
            {
                var v = def.Verbs.FirstOrDefault();
                var au = def.GetCompProperties<CompProperties_AmmoUser>();
                var proj = au?.ammoSet?.ammoTypes?.FirstOrDefault()?.projectile?.projectile as ProjectilePropertiesCE;
                sb.AppendLine($"{def.label} | {v?.verbClass?.Name} | {v?.range} | {v?.burstShotCount} | {au?.magazineSize} | {au?.reloadTime} | " +
                              $"{proj?.GetDamageAmount((Thing)null)} / {proj?.armorPenetrationSharp} / {proj?.armorPenetrationBlunt}");
            }
            Log.Message(sb.ToString());
        }

        // ================================================================ cyberware

        [DebugAction(Cyber, "Spawn cyberware item...", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SpawnCyberwareItem()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var hd in CyberwareDefCache.AllCyberware)
            {
                var ext = hd.Ext();
                for (var t = ext.minTier; t <= ext.maxTier; t++)
                {
                    var tier = t;
                    nodes.Add(new DebugActionNode($"{ext.item.defName} {tier}", DebugActionType.ToolMap, () =>
                    {
                        var item = ThingMaker.MakeThing(ext.item);
                        item.TryGetComp<CompCyberwareTier>()?.SetTier(tier);
                        GenPlace.TryPlaceThing(item, UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
                    }));
                }
            }
            return nodes;
        }

        [DebugAction(Cyber, "Install full chrome (iconic)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FullChrome(Pawn p)
        {
            foreach (var name in new[] { "RPC_DynalarSandevistan", "RPC_MantisBlades", "RPC_GorillaArms", "RPC_NewtonModule", "RPC_RamUpgrade", "RPC_RamUpgrade", "RPC_SmartLink" })
            {
                var def = DefDatabase<HediffDef>.GetNamedSilentFail(name);
                if (def != null) PawnLoadoutUtility.TryInstall(p, def, CyberTier.Iconic, CyberTier.Iconic);
            }
        }

        [DebugAction(Cyber, "Set capacity aptitude...", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SetAptitude()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var data in RPDefOf.RPC_CyberAptitude.degreeDatas)
            {
                int degree = data.degree;
                nodes.Add(new DebugActionNode(data.label, DebugActionType.ToolMapForPawns, pawnAction: p =>
                {
                    var traits = p.story?.traits;
                    if (traits == null) return;
                    var old = traits.GetTrait(RPDefOf.RPC_CyberAptitude);
                    if (old != null) traits.RemoveTrait(old);
                    traits.GainTrait(new Trait(RPDefOf.RPC_CyberAptitude, degree));
                    p.GetCyberTracker()?.Notify_CyberwareChanged();
                }));
            }
            return nodes;
        }

        [DebugAction(Cyber, "Toggle Netrunner trait", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ToggleNetrunner(Pawn p)
        {
            var traits = p.story?.traits;
            if (traits == null) return;
            var t = traits.GetTrait(RPDefOf.RPC_Netrunner);
            if (t != null) traits.RemoveTrait(t);
            else traits.GainTrait(new Trait(RPDefOf.RPC_Netrunner));
        }

        [DebugAction(Cyber, "Add cyberpsychosis burnout", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AddBurnout(Pawn p)
        {
            if (!CyberpsychosisUtility.HasBurnout(p)) p.health.AddHediff(RPDefOf.RPC_CyberpsychosisBurnout);
            p.GetCyberTracker()?.Notify_CyberwareChanged();
        }

        [DebugAction(Cyber, "End cyberpsychosis (remove chrome until safe)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StripUntilSafe(Pawn p)
        {
            var tracker = p.GetCyberTracker();
            if (tracker == null) return;
            foreach (var c in tracker.Cyberware.OrderByDescending(c => c.CapacityCost).ToList())
            {
                if (tracker.UsageRatio < RPDefOf.RPC_CyberpsychosisTuning.safeUsage) break;
                p.health.RemoveHediff(c);
            }
        }

        // ================================================================ factions

        [DebugAction(Factions, "Spawn faction squad (hostile assault)...", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SpawnSquad()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var faction in RpcFactions)
            {
                var f = faction;
                nodes.Add(new DebugActionNode(f.Name, DebugActionType.ToolMap, () => SpawnGroup(f, UI.MouseCell(), Find.CurrentMap, assault: true)));
            }
            return nodes;
        }

        [DebugAction(Factions, "Spawn faction squad (friendly, no relation change)...", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SpawnFriendlySquad()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var faction in RpcFactions)
            {
                var f = faction;
                nodes.Add(new DebugActionNode(f.Name, DebugActionType.ToolMap, () => SpawnGroup(f, UI.MouseCell(), Find.CurrentMap, assault: false)));
            }
            return nodes;
        }

        private static void SpawnGroup(Faction f, IntVec3 cell, Map map, bool assault)
        {
            if (assault && !f.HostileTo(Faction.OfPlayer)) CyberFactionUtility.ForceRelation(f, Faction.OfPlayer, FactionRelationKind.Hostile);
            var kinds = DefDatabase<PawnKindDef>.AllDefsListForReading.Where(k => k.defaultFactionDef == f.def && k.isFighter).ToList();
            if (kinds.Count == 0) return;
            var pawns = new List<Pawn>();
            for (int i = 0; i < 4; i++)
            {
                var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kinds[i % kinds.Count], f, mustBeCapableOfViolence: true));
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(cell, map, 4), map);
                pawns.Add(pawn);
            }
            LordJob job = assault ? new LordJob_AssaultColony(f, canKidnap: false, canTimeoutOrFlee: false) : new LordJob_DefendPoint(cell);
            LordMaker.MakeNewLord(f, job, map, pawns);
        }

        [DebugAction(Factions, "Spawn faction pawn kind...", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SpawnKind()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var kind in DefDatabase<PawnKindDef>.AllDefsListForReading.Where(k => k.defName.StartsWith("RPC_")).OrderBy(k => k.defName))
            {
                var k = kind;
                nodes.Add(new DebugActionNode(k.defName, DebugActionType.ToolMap, () =>
                {
                    var f = k.defaultFactionDef != null ? Find.FactionManager.FirstFactionOfDef(k.defaultFactionDef) : null;
                    var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(k, f));
                    GenSpawn.Spawn(pawn, UI.MouseCell(), Find.CurrentMap);
                }));
            }
            return nodes;
        }

        [DebugAction(Factions, "Raid now from faction...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> RaidNow()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var faction in RpcFactions)
            {
                var f = faction;
                nodes.Add(new DebugActionNode(f.Name, DebugActionType.Action, () =>
                {
                    if (!f.HostileTo(Faction.OfPlayer)) CyberFactionUtility.ForceRelation(f, Faction.OfPlayer, FactionRelationKind.Hostile);
                    var map = Find.CurrentMap;
                    IncidentDefOf.RaidEnemy.Worker.TryExecute(new IncidentParms
                    {
                        target = map, faction = f, forced = true, points = StorytellerUtility.DefaultThreatPointsNow(map),
                    });
                }));
            }
            return nodes;
        }

        [DebugAction(Factions, "Set relation with player...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> SetRelation()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var faction in RpcFactions)
            {
                var f = faction;
                var node = new DebugActionNode(f.Name);
                foreach (var kind in new[] { FactionRelationKind.Hostile, FactionRelationKind.Neutral, FactionRelationKind.Ally })
                {
                    var k = kind;
                    node.AddChild(new DebugActionNode(k.ToString(), DebugActionType.Action, () => CyberFactionUtility.ForceRelation(f, Faction.OfPlayer, k)));
                }
                nodes.Add(node);
            }
            return nodes;
        }

        [DebugAction(Factions, "Trigger corp retaliation strike team...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> Retaliation()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var faction in RpcFactions.Where(f => f.IsCorporation()))
            {
                var f = faction;
                nodes.Add(new DebugActionNode(f.Name, DebugActionType.Action, () =>
                {
                    if (!f.HostileTo(Faction.OfPlayer)) CyberFactionUtility.ForceRelation(f, Faction.OfPlayer, FactionRelationKind.Hostile);
                    Patch_Faction_TryAffectGoodwillWith.QueueRetaliation(f, f.Ext());
                }));
            }
            return nodes;
        }

        [DebugAction(Factions, "Gift goodwill to corp (tests rival anger)...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> GiftCorp()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var faction in RpcFactions.Where(f => f.IsCorporation()))
            {
                var f = faction;
                nodes.Add(new DebugActionNode(f.Name, DebugActionType.Action, () => f.TryAffectGoodwillWith(Faction.OfPlayer, 20, true, true, HistoryEventDefOf.GaveGift)));
            }
            return nodes;
        }

        [DebugAction(Factions, "Corp orbital trader arrives...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> CorpTrader()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var tk in DefDatabase<TraderKindDef>.AllDefsListForReading.Where(t => t.orbital && t.defName.StartsWith("RPC_")))
            {
                var k = tk;
                nodes.Add(new DebugActionNode(k.label, DebugActionType.Action, () =>
                    IncidentDefOf.OrbitalTraderArrival.Worker.TryExecute(new IncidentParms { target = Find.CurrentMap, traderKind = k, forced = true })));
            }
            return nodes;
        }

        [DebugAction(Factions, "Log RimPunk factions + relations", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogFactions()
        {
            var sb = new StringBuilder("[RimPunk CE] factions:\n");
            foreach (var f in RpcFactions)
            {
                var ext = f.Ext();
                sb.Append($"{f.Name} ({ext.kind}, {f.def.techLevel}): player {f.PlayerGoodwill} {f.PlayerRelationKind}");
                foreach (var r in ext.rivals)
                {
                    var rf = Find.FactionManager.FirstFactionOfDef(r);
                    if (rf != null) sb.Append($"; vs {rf.Name}: {f.RelationKindWith(rf)}");
                }
                sb.AppendLine();
            }
            Log.Message(sb.ToString());
        }

        // ================================================================ trauma team

        [DebugAction(Trauma, "Give coverage (free)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void GiveCoverage(Pawn p)
        {
            if (!GameComponent_RimPunk.IsCovered(p)) p.health.AddHediff(RPDefOf.RPC_TraumaTeamCoverage);
        }

        [DebugAction(Trauma, "Cover + down pawn (dispatch now)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CoverAndDown(Pawn p)
        {
            GiveCoverage(p);
            if (!p.Downed) HealthUtility.DamageUntilDowned(p, allowBleedingWounds: false);
        }

        // ================================================================ maxtac

        [DebugAction(MaxTac, "Cyberpsycho + call MaxTac now", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CallMaxTacNow(Pawn p)
        {
            if (!CyberpsychosisUtility.InCyberpsychosis(p)) CyberpsychosisUtility.TryStart(p, "debug");
            if (p.MentalState is MentalState_Cyberpsychosis ms) ms.maxTacCalled = true;
            GameComponent_RimPunk.Instance?.CallMaxTac(p, "debug");
        }

        [DebugAction(MaxTac, "Cyberpsycho: one kill short of MaxTac", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void OneKillShort(Pawn p)
        {
            if (!CyberpsychosisUtility.InCyberpsychosis(p)) CyberpsychosisUtility.TryStart(p, "debug");
            if (p.MentalState is MentalState_Cyberpsychosis ms) ms.kills = (RPDefOf.RPC_CyberpsychosisTuning?.maxTacKills ?? 5) - 1;
        }

        [DebugAction(Trauma, "Return extracted patients now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReturnNow()
        {
            var gc = GameComponent_RimPunk.Instance;
            if (gc == null) return;
            Messages.Message($"Returning {gc.PendingReturns} patient(s) on the next check.", MessageTypeDefOf.NeutralEvent, false);
            gc.ForceReturnsNow();
        }

        // ================================================================ diagnostics

        [DebugAction(Events, "Diagnostics: run state checks now", allowedGameStates = AllowedGameStates.Playing)]
        private static void RunChecks()
        {
            DiagChecks.RunAll();
            Messages.Message("RimPunk diagnostics written to " + Diag.FilePath, MessageTypeDefOf.NeutralEvent, false);
        }

        // ================================================================ events

        [DebugAction(Events, "NetWatch hunt now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void NetWatchNow()
        {
            var map = Find.CurrentMap;
            var colonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            var tracker = colonist?.GetCyberTracker();
            if (tracker != null) tracker.lastHighTierQuickhackTick = Find.TickManager.TicksGame;
            var def = DefDatabase<IncidentDef>.GetNamed("RPC_NetWatchHunt");
            var parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.forced = true;
            if (!def.Worker.TryExecute(parms)) Messages.Message("NetWatch hunt failed (is NetWatch in this world?)", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(Events, "Mark pawn as high-tier netrunner", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MarkNetrunner(Pawn p)
        {
            var t = p.GetCyberTracker();
            if (t != null) t.lastHighTierQuickhackTick = Find.TickManager.TicksGame;
        }

        [DebugAction(Events, "Rogue cyberpsycho now (map edge)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RogueNow()
        {
            var def = DefDatabase<IncidentDef>.GetNamed("RPC_RogueCyberpsycho");
            var parms = StorytellerUtility.DefaultParmsNow(def.category, Find.CurrentMap);
            parms.forced = true;
            def.Worker.TryExecute(parms);
        }

        [DebugAction(Events, "Rogue cyberpsycho at cursor", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RogueHere()
        {
            var kind = DefDatabase<PawnKindDef>.GetNamed("RPC_RogueCyberpsycho");
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, mustBeCapableOfViolence: true));
            GenSpawn.Spawn(pawn, UI.MouseCell(), Find.CurrentMap);
            pawn.mindState.mentalStateHandler.TryStartMentalState(RPDefOf.RPC_Cyberpsychosis, "debug", forced: true, forceWake: true);
        }

        [DebugAction(Events, "Start cyberpsychosis (ignore immunity)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForcePsychosis(Pawn p) =>
            p.mindState?.mentalStateHandler?.TryStartMentalState(RPDefOf.RPC_Cyberpsychosis, "debug", forced: true, forceWake: true);
    }
}
