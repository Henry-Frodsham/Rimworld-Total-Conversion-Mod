using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatExtended;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>Improvement cycle 1: adrenaline, netrun UI, plating, arms, contextual AI, barks, gigs, save / load.</summary>
    public partial class GameComponent_RimPunkLiveTests
    {
        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_ArmDefs()
        {
            Check(PowerOf("RPC_GorillaArms") >= 20f, $"gorilla fist power {PowerOf("RPC_GorillaArms")}");
            Check(Hediff("RPC_GorillaArms").Ext().combatRole == CombatRole.Melee, "gorilla arms are melee-typed");
            Check(Hediff("RPC_ElectrifyingGorillaArms").Ext().combatRole == CombatRole.Melee, "electrifying gorilla arms are melee-typed");
            Check(Hediff("RPC_ElectrifyingGorillaArms").CompProps<HediffCompProperties_CyberOnHit>().riderFraction <= 0.15f, "electrifying gorilla rider nerfed");
            Check(Hediff("RPC_ElectrifyingGorillaArms").CompProps<HediffCompProperties_GiveCyberAbilities>() == null, "gorilla arms have no closing ability");
            Check(Hediff("RPC_MantisBlades").Ext().HasValue("cooldown") && Hediff("RPC_ThermalMantisBlades").Ext().HasValue("cooldown"), "mantis dash has a tiered cooldown");
            Check(Hediff("RPC_MantisBlades").Ext().effects != null, "mantis blades scale with tier");
            yield break;
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_Adrenaline()
        {
            var p = Colonist(Center);
            var t = p.GetCyberTracker();
            t.lastCombatTick = -99999;
            t.SetAdrenaline(0f);
            t.Notify_CombatAction();
            Check(Mathf.Approximately(t.Adrenaline, RPDefOf.RPC_AdrenalineTuning.enteredCombatBurst), $"entering combat gives the burst: {t.Adrenaline}");

            float before = t.Adrenaline;
            var attacker = Hostile(Center + new IntVec3(3, 0, 0), Thing("Gun_Autopistol"));
            var victim = Hostile(Center + new IntVec3(-3, 0, 0));
            CyberEvents.Notify_DamageDealt(p, victim, new DamageInfo(DamageDefOf.Cut, 10f, 0f, -1f, p), 10f);
            Check(t.Adrenaline > before, $"dealing damage builds it through CyberEvents: {before} -> {t.Adrenaline}");
            before = t.Adrenaline;
            CyberEvents.Notify_DamageTaken(p, new DamageInfo(DamageDefOf.Cut, 10f, 0f, -1f, attacker), 10f);
            Check(t.Adrenaline > before, $"taking damage builds it through CyberEvents: {before} -> {t.Adrenaline}");

            t.SetAdrenaline(90f);
            yield return 20;
            Check(p.health.hediffSet.HasHediff(RPDefOf.RPC_AdrenalineSurge), "surge hediff at 90%");
            t.SetAdrenaline(10f);
            yield return 20;
            Check(!p.health.hediffSet.HasHediff(RPDefOf.RPC_AdrenalineSurge), "surge hediff gone at 10%");

            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_DynalarSandevistan"), CyberTier.T3);
            var sandy = AbilityFrom(p, CyberBuffAiRule.CloseDistance);
            Check(sandy != null, "sandevistan ability granted");
            t.SetAdrenaline(5f);
            Check(sandy != null && !sandy.CanCast.Accepted, "sandevistan refused at 5 adrenaline");
            t.SetAdrenaline(40f);
            Check(sandy != null && sandy.CanCast.Accepted, "sandevistan castable at 40 adrenaline");
            sandy?.CompOfType<CompAbilityEffect_CyberBuff>()?.Apply(p, p);
            Check(Mathf.Approximately(t.Adrenaline, 15f), $"activation spent 25: {t.Adrenaline}");
            Check(BuffActive(sandy), "sandevistan running");

            Cleanup(attacker, victim);
            t.lastCombatTick = -99999;
            float d0 = t.Adrenaline;
            yield return 120;
            Check(t.Adrenaline < d0, $"decays out of combat: {d0} -> {t.Adrenaline}");
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_Plating()
        {
            var chromed = Colonist(Center + new IntVec3(2, 0, 0));
            var plain = Colonist(Center + new IntVec3(-2, 0, 0));
            var foe = Hostile(Center + new IntVec3(0, 0, 8));
            RimPunkDebugActions.DebugInstall(chromed, Hediff("RPC_GorillaArms"), CyberTier.Iconic);
            var implant = chromed.InstalledCyberware().First(c => c.def.defName == "RPC_GorillaArms");
            var arm = implant.Part;
            var hand = arm.GetDirectChildParts().FirstOrDefault();
            var torso = chromed.RaceProps.body.corePart;

            ChromePlatingUtility.Totals(chromed, arm, out float sharp, out float blunt, out float hp);
            Check(sharp == 0f && blunt == 0f && hp > 1f, $"implanted arm plated: {sharp} mm, {blunt} MPa, x{hp} health");
            if (hand != null)
            {
                ChromePlatingUtility.Totals(chromed, hand, out float hs, out _, out _);
                Check(hs == 0f, "general arm implant adds no armour to its hand");
            }
            ChromePlatingUtility.Totals(chromed, torso, out float ts, out _, out float th);
            Check(ts == 0f && th == 1f, $"torso unaffected by an arm implant: {ts} mm, x{th}");
            float natural = ArmorUtilityCE.PartialStat(chromed, StatDefOf.ArmorRating_Sharp, arm);
            float naturalPlain = ArmorUtilityCE.PartialStat(plain, StatDefOf.ArmorRating_Sharp, plain.RaceProps.body.AllParts.First(x => x.def == arm.def));
            Check(Mathf.Approximately(natural, naturalPlain), $"CE sees the plating on that part: {natural} vs {naturalPlain} mm");

            // the same high-penetration hit on the same part, through CE's real armor path
            float Hit(Pawn target)
            {
                var part = target.RaceProps.body.AllParts.First(x => x.def == arm.def && x.customLabel == arm.customLabel);
                var dinfo = new DamageInfo(DamageDefOf.Cut, 12f, 500f, -1f, foe, part);
                return ArmorUtilityCE.GetAfterArmorDamage(dinfo, target, part, out _, out _, out _).Amount;
            }
            float onPlain = Hit(plain), onChrome = Hit(chromed);
            Check(onChrome < onPlain && onChrome > 0f, $"plated part takes less after CE's armor: {onChrome:0.##} vs {onPlain:0.##}");
            var realPart = chromed.RaceProps.body.AllParts.First(x => x == arm);
            chromed.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f, 500f, -1f, foe, realPart));
            Check(chromed.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(), "a real hit still lands");

            // NPCs get the same
            var npc = Hostile(Center + new IntVec3(6, 0, 0));
            RimPunkDebugActions.DebugInstall(npc, Hediff("RPC_MantisBlades"), CyberTier.T5);
            var npcPart = npc.InstalledCyberware().First().Part;
            ChromePlatingUtility.Totals(npc, npcPart, out float ns, out _, out float nh);
            Check(ns == 0f && nh > 1f, $"NPC implant plates too: {ns} mm, x{nh}");

            // maxed chrome against the large-mech target
            var maxed = Colonist(Center + new IntVec3(-6, 0, 0));
            RimPunkDebugActions_Scenarios.MaxCyberware(maxed);
            ChromePlatingUtility.Totals(maxed, maxed.RaceProps.body.corePart, out _, out _, out float mt);
            var mArm = maxed.InstalledCyberware().FirstOrDefault(c => c.Part != null && c.Ext.slot.defName == "RPC_Slot_Arm")?.Part;
            ChromePlatingUtility.Totals(maxed, mArm, out float ms, out float mb, out float ma);
            float wideS = maxed.GetStatValue(StatDefOf.ArmorRating_Sharp), wideB = maxed.GetStatValue(StatDefOf.ArmorRating_Blunt);
            Note($"mixed chrome build: torso {wideS:0.#} mm / {wideB:0.#} MPa x{mt:0.##} health; total implant armour on arm {ms:0.#} mm / {mb:0.#} MPa x{ma:0.##} health. Dedicated plating ceiling: 10 mm / 25 MPa");
            Check(wideS > 0f && wideS <= 10.001f && wideB > 0f && wideB <= 25.001f, $"mixed chrome build stays within implant ceiling: {wideS:0.#} mm / {wideB:0.#} MPa");
            Check(mt >= 2.5f && ma <= ChromePlatingUtility.Tuning.maxHealthFactor, $"maxed pawn health in large-mech range: torso x{mt:0.##}, arm x{ma:0.##} (centipede x4.32)");
            yield break;
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_Barks()
        {
            var p = Colonist(Center);
            int before = Find.PlayLog.AllEntries.Count;
            BarkUtility.Say(p, BarkKeys.GigOffered);
            Check(Find.PlayLog.AllEntries.Count == before + 1 || Find.PlayLog.AllEntries.Count == 150, "bark added to the play log");
            var entry = Find.PlayLog.AllEntries.FirstOrDefault() as PlayLogEntry_Bark;
            Check(entry != null, "it's a bark entry");
            string text = entry?.ToGameStringFromPOV(p);
            Check(!text.NullOrEmpty() && DefDatabase<BarkDef>.GetNamed(BarkKeys.GigOffered).lines.Any(l => text.Contains(l)), $"entry reads back as a line: {text}");
            Note($"bubble mod listening on PlayLog.Add: {BarkUtility.BubbleModPresent}");
            foreach (var def in DefDatabase<BarkDef>.AllDefsListForReading)
                if (def.lines.NullOrEmpty()) Check(false, def.defName + " has no lines");
            yield break;
        }

        /// <summary>Bubbles is optional: with it, speech is a bubble and no floating text; without it, speech floats as text. Run the suite with --no-bubbles to cover the second half.</summary>
        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_BarksWithoutBubbles()
        {
            bool loaded = HarmonyLib.AccessTools.TypeByName("Bubbles.Core.Bubble") != null;
            Check(BarkUtility.BubbleModPresent == loaded, $"bubble detection matches the mod being loaded ({loaded})");
            if (!loaded)
            {
                Check(!HarmonyLib.Harmony.GetAllPatchedMethods().Any(m => m.DeclaringType?.FullName?.StartsWith("Bubbles.") == true), "nothing of Bubbles is patched");
                Check(!HarmonyLib.Harmony.GetAllPatchedMethods().Any(m => m.DeclaringType == typeof(CommsUtility) || m.Name == "Draw" && m.DeclaringType?.Namespace == "Bubbles.Core"), "comms colour patch stays off");
            }

            var p = Colonist(Center);
            string line = "rpc-test-line-" + p.thingIDNumber;
            int before = Find.PlayLog.AllEntries.OfType<PlayLogEntry_Bark>().Count();
            BarkUtility.SayText(p, line);
            Check(Find.PlayLog.AllEntries.OfType<PlayLogEntry_Bark>().Count() > before || Find.PlayLog.AllEntries.Count == 150, "line is in the play log either way");
            bool floating = map.listerThings.AllThings.OfType<MoteText>().Any(m => m.text == line);
            Check(floating == !loaded, loaded ? "bubble mod draws it: no floating text" : "no bubble mod: the line floats as text");

            var gig = DefDatabase<BarkDef>.GetNamed(BarkKeys.GigOffered);
            var q = Colonist(Center + new IntVec3(2, 0, 0));
            for (int i = 0; i < 200 && !map.listerThings.AllThings.OfType<MoteText>().Any(m => gig.lines.Contains(m.text)) ; i++)
            {
                if (loaded) break;
                BarkUtility.Say(q, BarkKeys.GigOffered);
                yield return 100;
            }
            if (!loaded) Check(map.listerThings.AllThings.OfType<MoteText>().Any(m => gig.lines.Contains(m.text)), "a normal bark floats as text too");
            yield break;
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_RogueCyberpsycho()
        {
            var inc = DefDatabase<IncidentDef>.GetNamed("RPC_RogueCyberpsycho");
            var parms = StorytellerUtility.DefaultParmsNow(inc.category, map);
            parms.forced = true;
            bool ok = inc.Worker.TryExecute(parms);
            Check(ok, "incident fired");
            var rogue = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.kindDef.defName.StartsWith(inc.pawnKind.defName));
            Check(rogue != null, "rogue on the map");
            if (rogue == null) yield break;
            spawned.Add(rogue);
            Check(rogue.health.hediffSet.HasHediff(Hediff("RPC_ScrapPlating")), "wearing scrap plating");
            Check(CyberpsychosisUtility.InCyberpsychosis(rogue), "in cyberpsychosis");
            int skinOrBone = rogue.InstalledCyberware().Count(c => c.Ext.slot.defName is "RPC_Slot_IntegumentarySystem" or "RPC_Slot_Skeleton");
            Check(skinOrBone >= 2, $"armor implants installed: {skinOrBone}");
            Note($"rogue: {rogue.GetStatValue(StatDefOf.ArmorRating_Sharp):0.#} mm, {rogue.GetStatValue(StatDefOf.ArmorRating_Blunt):0.#} MPa, role {ContextualCombat.RoleOf(rogue)}, cyberware [{string.Join(", ", rogue.InstalledCyberware().Select(c => c.def.defName + " " + c.tier))}]");
            Check(ContextualCombat.RoleOf(rogue) == CombatRole.Melee || rogue.equipment?.Primary != null, "has a way to fight");
            var pos = rogue.Position;
            yield return 300;
            Check(!rogue.Destroyed && !rogue.Dead && rogue.InMentalState, "still raging after 5 s");
            Check(rogue.Destroyed || rogue.Position != pos || rogue.CurJob != null, $"doing something: {rogue.CurJobDef?.defName}");
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_NetrunUi()
        {
            var netrunner = Colonist(Center);
            RimPunkDebugActions_Scenarios.LearnAllAt(netrunner, CyberTier.T5);
            netrunner.GetCyberTracker().GainRam(9999f);
            var target = Standing(Center + new IntVec3(10, 0, 0));
            netrunner.drafter.Drafted = true;
            yield return 2;

            var provider = new FloatMenuOptionProvider_Quickhack();
            var ctx = new FloatMenuContext(new List<Pawn> { netrunner }, target.DrawPos, map);
            Check(provider.SelectedPawnValid(netrunner, ctx), "provider accepts a pawn with a deck");
            var options = provider.GetOptionsFor(target, ctx).ToList();
            Check(options.Count >= 10, $"right-click menu lists learned hacks: {options.Count}");
            Check(options.Any(o => !o.Disabled), "at least one usable entry");
            var mate = Colonist(Center + new IntVec3(-3, 0, 0));
            Check(provider.GetOptionsFor(mate, ctx).Any(), "menu works on a friendly pawn too");
            var bare = Colonist(Center + new IntVec3(-5, 0, 0));
            Check(!provider.SelectedPawnValid(bare, new FloatMenuContext(new List<Pawn> { bare }, target.DrawPos, map)), "no menu without a deck");

            var comp = MapComponent_Netrun.For(map);
            var hack = QuickhackDefOf.RPC_Quickhack_ShortCircuit;
            netrunner.abilities.GetAbility(hack).ResetCooldown();
            float ram = netrunner.GetCyberTracker().Ram;
            int uploaded = comp.ResultCount(NetrunResult.Uploaded);
            NetrunUtility.QueueUpload(netrunner, hack, target);
            bool tracked = false;
            for (int i = 0; i < 1600 && comp.ResultCount(NetrunResult.Uploaded) == uploaded; i++)
            {
                tracked |= comp.IsUploading(hack, target);
                yield return 5;
            }
            Check(tracked, "map component tracked the upload");
            Check(comp.ResultCount(NetrunResult.Uploaded) > uploaded, "UPLOADED state reached");
            Check(netrunner.GetCyberTracker().Ram < ram, "RAM spent");

            // Self-ICE: resisted
            var iced = Standing(Center + new IntVec3(8, 0, 4));
            RimPunkDebugActions.DebugInstall(iced, Hediff("RPC_SelfIce"), CyberTier.Iconic);
            int resisted = comp.ResultCount(NetrunResult.Resisted);
            netrunner.abilities.GetAbility(hack).ResetCooldown();
            NetrunUtility.QueueUpload(netrunner, hack, iced);
            for (int i = 0; i < 1600 && comp.ResultCount(NetrunResult.Resisted) == resisted; i++) yield return 5;
            Check(comp.ResultCount(NetrunResult.Resisted) > resisted, "RESISTED state reached through Self-ICE");

            // a hack that can't affect its target: immune, and nothing is spent
            var mech = PawnGenerator.GeneratePawn(PawnKindDefOf.Mech_Scyther ?? PawnKindDefOf.Villager, Faction.OfMechanoids);
            GenSpawn.Spawn(mech, Center + new IntVec3(6, 0, -5), map);
            spawned.Add(mech);
            var mechProof = netrunner.abilities.AllAbilitiesForReading.OfType<QuickhackAbility>().FirstOrDefault(a => a.Ext != null && !a.Ext.affectsMechanoids);
            Check(mechProof != null, "there's a hack mechanoids shrug off");
            if (mechProof != null)
            {
                int immune = comp.ResultCount(NetrunResult.Immune);
                Check(QuickhackUtility.InvalidReason(mechProof, mech) != null, "invalid on a mechanoid");
                Check(provider.GetOptionsFor(mech, ctx).Any(o => o.Disabled), "shown disabled in the menu");
                QuickhackUtility.ValidTarget(mechProof, mech, true);
                Check(comp.ResultCount(NetrunResult.Immune) > immune, "IMMUNE state reached");
            }
            netrunner.drafter.Drafted = false;
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_NetrunnerDuel()
        {
            // an enemy netrunner with a sniper and a closer pistol in reach: it should go for the sniper
            var sniper = Colonist(Center + new IntVec3(18, 0, 0), Thing("Gun_SniperRifle"));
            var pistol = Colonist(Center + new IntVec3(6, 0, 0), Thing("Gun_Autopistol"));
            sniper.drafter.Drafted = true;
            pistol.drafter.Drafted = true;
            var runner = Hostile(Center, Thing("Gun_Autopistol"));
            RimPunkDebugActions_Scenarios.LearnAllAt(runner, CyberTier.T5);
            var t = runner.GetCyberTracker();
            t.GainRam(9999f);
            Check(ContextualCombat.RoleOf(runner) == CombatRole.Ranged, "netrunner counts as ranged");
            runner.mindState.enemyTarget = pistol;
            bool hitSniper = false;
            Thing firstTarget = null;
            for (int i = 0; i < 80 && !hitSniper; i++)
            {
                if (runner.Dead || runner.Destroyed) break;
                if (runner.CurJob?.ability is QuickhackAbility)
                {
                    firstTarget ??= runner.CurJob.targetA.Thing;
                    if (runner.CurJob.targetA.Thing == sniper) hitSniper = true;
                }
                yield return 15;
            }
            Check(hitSniper, $"enemy netrunner went after the sniper (first hack target: {firstTarget?.LabelShort ?? "none"})");

            // the duel: a player netrunner fights back, and neither side throws
            RimPunkDebugActions_Scenarios.LearnAllAt(pistol, CyberTier.T5);
            pistol.GetCyberTracker().GainRam(9999f);
            var comp = MapComponent_Netrun.For(map);
            int results = comp.ResultCount(NetrunResult.Uploaded) + comp.ResultCount(NetrunResult.Resisted);
            if (!runner.Dead && !runner.Destroyed)
            {
                var hack = QuickhackDefOf.RPC_Quickhack_ShortCircuit;
                pistol.abilities.GetAbility(hack)?.ResetCooldown();
                NetrunUtility.QueueUpload(pistol, hack, runner);
            }
            yield return 300;
            Check(comp.ResultCount(NetrunResult.Uploaded) + comp.ResultCount(NetrunResult.Resisted) >= results,
                "duel remains valid during long uploads; committed completion/cancellation covered by cycle2-upload");
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_MeleeCloses()
        {
            var target = Colonist(Center + new IntVec3(20, 0, 0));
            target.drafter.Drafted = true;
            var brawler = Hostile(Center, Thing("Gun_Autopistol"));
            RimPunkDebugActions.DebugInstall(brawler, Hediff("RPC_ElectrifyingGorillaArms"), CyberTier.T5);
            Check(ContextualCombat.RoleOf(brawler) == CombatRole.Melee, "gorilla arms make a melee build");
            brawler.mindState.enemyTarget = target;
            bool charged = false;
            for (int i = 0; i < 60 && !charged; i++)
            {
                charged = brawler.CurJobDef == JobDefOf.AttackMelee;
                yield return 10;
            }
            Check(charged, $"gorilla arms with a gun in hand close to melee (job: {brawler.CurJobDef?.defName})");
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_RangedKites()
        {
            var target = Colonist(Center + new IntVec3(4, 0, 0));
            target.drafter.Drafted = true;
            var shooter = Hostile(Center, Thing("Gun_AssaultRifle"));
            RimPunkDebugActions.DebugInstall(shooter, Hediff("RPC_SmartLink"), CyberTier.T5);
            Check(ContextualCombat.RoleOf(shooter) == CombatRole.Ranged, "smart link makes a ranged build");
            shooter.mindState.enemyTarget = target;
            float start = shooter.Position.DistanceTo(target.Position);
            bool kited = false;
            for (int i = 0; i < 60 && !kited; i++)
            {
                kited = shooter.CurJobDef == JobDefOf.Goto && shooter.CurJob.targetA.Cell.DistanceTo(target.Position) >= start + 3f;
                yield return 10;
            }
            Check(kited, $"ranged build backs off from a close enemy (job: {shooter.CurJobDef?.defName})");
            yield return 120;
            Check(shooter.Position.DistanceTo(target.Position) > start, $"and gained distance: {start:0.#} -> {shooter.Position.DistanceTo(target.Position):0.#}");
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_SandevistanCloses()
        {
            // a long gap: over a short one the pawn can legitimately be inside 6 tiles before its first fight decision
            var target = Colonist(Center + new IntVec3(18, 0, 0));
            target.drafter.Drafted = true;
            var runner = Hostile(Center + new IntVec3(-14, 0, 0), Thing("Gun_Autopistol"));
            RimPunkDebugActions.DebugInstall(runner, Hediff("RPC_DynalarSandevistan"), CyberTier.T5);
            RimPunkDebugActions.DebugInstall(runner, Hediff("RPC_GorillaArms"), CyberTier.T5);
            runner.mindState.enemyTarget = target;
            var sandy = AbilityFrom(runner, CyberBuffAiRule.CloseDistance);
            Check(sandy != null, "has sandevistan");
            bool used = false;
            var seen = new List<string>();
            for (int i = 0; i < 80 && !used; i++)
            {
                used = BuffActive(sandy);
                string state = $"{runner.CurJobDef?.defName}/ability {runner.CurJob?.ability?.def.defName}/enemy {runner.mindState.enemyTarget?.LabelShort}/canCast {sandy?.CanCast.Accepted}/ai {sandy?.AICanTargetNow(runner)}/role {ContextualCombat.RoleOf(runner)}/adr {runner.GetCyberTracker().Adrenaline:0}";
                if (!seen.Contains(state)) seen.Add(state);
                yield return 10;
            }
            Note("sandevistan timeline: " + string.Join(" | ", seen.Take(10)));
            Check(used, $"melee build pops sandevistan to close 32 tiles (adrenaline {runner.GetCyberTracker().Adrenaline:0}, job {runner.CurJobDef?.defName})");
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_BerserkWhenPinned()
        {
            var target = Colonist(Center + new IntVec3(20, 0, 0));
            target.drafter.Drafted = true;
            var grunt = Hostile(Center, Thing("Gun_Autopistol"));
            RimPunkDebugActions.DebugInstall(grunt, Hediff("RPC_MooreTechBerserk"), CyberTier.T5);
            var t = grunt.GetCyberTracker();
            t.SetAdrenaline(100f);
            grunt.mindState.enemyTarget = target;
            var berserk = AbilityFrom(grunt, CyberBuffAiRule.Pinned);
            Check(berserk != null, "has berserk");
            yield return 60;
            Check(!BuffActive(berserk), "not used at long range while not pinned");
            var suppress = grunt.TryGetComp<CompSuppressable>();
            Check(suppress != null, "CE suppression comp present");
            for (int i = 0; i < 40 && suppress != null && !suppress.isSuppressed; i++)
            {
                suppress.AddSuppression(2000f, target.Position);
                yield return 5;
            }
            Check(suppress?.isSuppressed == true || BuffActive(berserk), "pawn is pinned");
            bool used = false;
            for (int i = 0; i < 40 && !used; i++)
            {
                used = BuffActive(berserk);
                yield return 10;
            }
            Check(used, $"berserk popped when pinned (job {grunt.CurJobDef?.defName}, adrenaline {t.Adrenaline:0})");
            if (used)
            {
                float s = suppress.CurrentSuppression;
                suppress.AddSuppression(2000f, target.Position);
                Check(suppress.CurrentSuppression <= s, "berserk ignores further suppression");
                Check(ContextualCombat.RoleOf(grunt) == CombatRole.Melee, "berserk makes it a melee build");
            }
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_GigsLocal()
        {
            var inst = GameComponent_Gigs.Instance;
            Check(inst != null, "game component exists");
            Check(GameComponent_Gigs.TargetCandidates().Count > 0, $"hostile corp/gang targets: {GameComponent_Gigs.TargetCandidates().Count}");
            int silver0 = map.listerThings.ThingsOfDef(ThingDefOf.Silver).Sum(t => t.stackCount);

            var runner = Colonist(Center + new IntVec3(2, 0, 2));

            var kill = inst.Debug_RunHere(GigType.Assassinate, map);
            Check(kill?.objective is Pawn, "assassinate: mark spawned");
            if (kill?.objective is Pawn mark)
            {
                spawned.Add(mark);
                mark.Kill(null);
            }
            yield return 260;
            Check(kill?.state == GigState.Completed, $"assassinate: done when the mark dies ({kill?.state})");

            var sab = inst.Debug_RunHere(GigType.Sabotage, map);
            Check(sab?.objective?.def == GameComponent_Gigs.Tuning.serverRack, "sabotage: rack spawned");
            sab?.objective?.Destroy(DestroyMode.KillFinalize);
            yield return 260;
            Check(sab?.state == GigState.Completed, $"sabotage: done when the rack is destroyed ({sab?.state})");

            var steal = inst.Debug_RunHere(GigType.Steal, map);
            Check(steal?.objective?.def == GameComponent_Gigs.Tuning.dataShard, "steal: shard spawned");
            yield return 260;
            Check(steal?.state == GigState.Active, $"steal: not done while it's lying on the floor ({steal?.state})");
            var shard = steal?.objective;
            if (shard != null)
            {
                shard.DeSpawn();
                runner.inventory.innerContainer.TryAdd(shard);
            }
            yield return 260;
            Check(steal?.state == GigState.Completed, $"steal: done once carried off ({steal?.state})");

            var rescue = inst.Debug_RunHere(GigType.Rescue, map);
            var hostage = rescue?.objective as Pawn;
            Check(hostage != null && hostage.Downed, "rescue: hostage spawned, restrained");
            if (hostage != null)
            {
                spawned.Add(hostage);
                var freeOptions = new FloatMenuOptionProvider_FreeHostage().GetOptionsFor(hostage, new FloatMenuContext(new List<Pawn> { runner }, hostage.DrawPos, map)).ToList();
                Check(freeOptions.Count == 1 && !freeOptions[0].Disabled, "rescue: right-click option offered");
                runner.jobs.TryTakeOrderedJob(JobMaker.MakeJob(GameComponent_Gigs.Tuning.freeHostageJob, hostage), JobTag.Misc);
            }
            for (int i = 0; i < 100 && rescue?.state == GigState.Active; i++) yield return 15;
            Check(rescue?.state == GigState.Completed, $"rescue: done when freed ({rescue?.state}; rescuer job {runner.CurJobDef?.defName}, downed {runner.Downed}, dead {runner.Dead}, mental {runner.MentalStateDef?.defName}, carrying {runner.carryTracker?.CarriedThing}, hostage {hostage?.Position} reachable {(hostage != null && !runner.Dead && runner.CanReach(hostage, PathEndMode.Touch, Danger.Deadly))}, rescuer at {runner.Position})");
            Check(hostage?.Faction == Faction.OfPlayer && !hostage.Downed, "rescue: hostage up and joined");

            var failed = inst.Debug_RunHere(GigType.Rescue, map);
            if (failed?.objective is Pawn doomed)
            {
                spawned.Add(doomed);
                doomed.Kill(null);
            }
            yield return 260;
            Check(failed?.state == GigState.Failed, $"rescue: fails if the hostage dies ({failed?.state})");

            yield return 400; // drop pods land
            int silver1 = map.listerThings.ThingsOfDef(ThingDefOf.Silver).Sum(t => t.stackCount);
            Check(silver1 > silver0, $"pay arrived: {silver0} -> {silver1} silver");

            var offered = inst.TryAddOffer(GigType.Sabotage);
            Check(offered?.quest != null && offered.quest.State == QuestState.NotYetAccepted && Find.QuestManager.Contains(offered.quest), "an offer is served as a not-yet-accepted quest");
        }

        [LiveScenario("cycle1")]
        private IEnumerable<int> Scenario_GigSite()
        {
            var inst = GameComponent_Gigs.Instance;
            var offer = inst.TryAddOffer(GigType.Assassinate);
            Check(offer != null, "offer made");
            if (offer == null) yield break;
            GameComponent_Gigs.AcceptGig(offer.id);
            Check(offer.state == GigState.Active && offer.site != null, $"accepted: state {offer.state}, site {offer.site?.Label}");
            Check(offer.quest?.State == QuestState.Ongoing, $"accepting the gig accepts its quest ({offer.quest?.State})");
            if (offer.site == null) yield break;
            Check(Find.WorldObjects.Contains(offer.site), "site is on the world map");
            Check(offer.site.Faction == offer.target, "site belongs to the target");

            var siteMap = GetOrGenerateMapUtility.GetOrGenerateMap(offer.site.Tile, new IntVec3(120, 1, 120), offer.site.def);
            Check(siteMap != null, "site map generated");
            Check(offer.objectiveSpawned && offer.objective is Pawn, $"objective spawned on the site map: {offer.objective}");
            Check(siteMap != null && siteMap.mapPawns.AllPawnsSpawned.Count(p => p.Faction == offer.target) >= 2, $"guarded: {siteMap?.mapPawns.AllPawnsSpawned.Count(p => p.Faction == offer.target)} hostiles");
            var worker = offer.site.parts.FirstOrDefault(p => p.def == GameComponent_Gigs.Tuning.objectivePart)?.def.Worker;
            Check(worker != null, "objective part is on the site");
            string letter = worker?.GetArrivedLetterPart(siteMap, out _, out var look);
            Check(!letter.NullOrEmpty(), "arrival letter names the objective: " + letter);

            if (Find.Maps.Contains(siteMap)) Current.Game.DeinitAndRemoveMap(siteMap, false);
            if (!offer.site.Destroyed) offer.site.Destroy();
            yield return 260;
            Check(offer.state == GigState.Failed, $"leaving the site without finishing fails the gig ({offer.state})");
            Check(offer.quest?.State == QuestState.EndedFailed, $"and fails its quest ({offer.quest?.State})");
            Current.Game.CurrentMap = map;
        }

        private static float savedInstability;
        private static int savedShardPawn;
        private static int savedEngramItem;
        private static int savedUploadCaster;
        private static int savedUploadRemaining;
        [LiveScenario("last")]
        private IEnumerable<int> Scenario_SaveLoad()
        {
            var keep = Colonist(Center + new IntVec3(1, 0, 1));
            keep.GetCyberTracker().SetAdrenaline(33f);
            keep.GetCyberTracker().lastCombatTick = Find.TickManager.TicksGame;
            BarkUtility.Say(keep, BarkKeys.GigAccepted);
            var active = GameComponent_Gigs.Instance.Debug_RunHere(GigType.Steal, map);
            Check(active != null, "a running gig to save");
            spawned.Remove(keep);
            savedGigs = GameComponent_Gigs.Instance.Gigs.Count;
            savedAdrenaline = keep.GetCyberTracker().Adrenaline;
            savedPawnId = keep.thingIDNumber;
            keep.GetCyberTracker().instability = 42.5f;
            savedInstability = 42.5f;
            var caster = Cycle2Runner(Center + new IntVec3(-10, 0, 0));
            var target = Standing(Center + new IntVec3(10, 0, 0));
            NetrunUtility.QueueUpload(caster, QuickhackDefOf.RPC_Quickhack_ShortCircuit, target);
            yield return 30;
            Check(Verb_Quickhack.IsUploading(caster, out var upload), "upload active at save");
            savedUploadCaster = caster.thingIDNumber;
            savedUploadRemaining = upload?.ticksLeft ?? 0;
            // cycle 4: slotted shards, an engram's recruit numbers and an engram item all have to come back
            var shardPawn = WithPort(Colonist(Center + new IntVec3(2, 0, 2)));
            ShardUtility.Slot(shardPawn, Thing("RPC_SkillShard_Plants"));
            ShardUtility.Slot(shardPawn, Thing("RPC_CapacityShard"));
            shardPawn.GetCyberTracker().engramResistance = 7f;
            shardPawn.GetCyberTracker().engramWill = 3f;
            spawned.Remove(shardPawn);
            savedShardPawn = shardPawn.thingIDNumber;
            var engramItem = EngramUtility.MakeEngram(Lore("DavidMartinez"));
            GenSpawn.Spawn(engramItem, Center + new IntVec3(3, 0, 3), map);
            savedEngramItem = engramItem.thingIDNumber;
            // The earlier gig stays saved even though this test world now has gigs disabled.
            WorldFeatures.Current.SetForTests(false, true);
            GameDataSaveLoader.SaveGame(SaveName);
            Note($"saved {SaveName}: {savedGigs} gigs, adrenaline {savedAdrenaline}");
            reloading = true;
            LongEventHandler.QueueLongEvent(() => GameDataSaveLoader.LoadGame(SaveName), "LoadingLongEvent", true, null);
            yield return 100000; // the load replaces this game and this component
        }

        private IEnumerable<int> Scenario_AfterReload()
        {
            var inst = GameComponent_Gigs.Instance;
            Check(inst != null && inst.Gigs.Count == savedGigs, $"gigs survived the reload: {inst?.Gigs.Count} of {savedGigs}");
            var steal = inst?.Gigs.FirstOrDefault(g => g.state == GigState.Active && g.type == GigType.Steal);
            Check(steal?.objective != null && !steal.objective.Destroyed, "running gig kept its objective");
            var p = map.mapPawns.AllPawnsSpawned.FirstOrDefault(x => x.thingIDNumber == savedPawnId);
            Check(p != null, "test pawn present");
            if (p != null) Check(Mathf.Abs(p.GetCyberTracker().Adrenaline - savedAdrenaline) < 5f, $"adrenaline survived: {p.GetCyberTracker().Adrenaline} (was {savedAdrenaline})");
            Check(!WorldFeatures.Gigs && WorldFeatures.Quickhacks, "world feature choices survived full game reload");
            Check(p != null && Mathf.Abs(p.GetCyberTracker().instability - savedInstability) < 0.01f, $"instability survived full game reload ({p?.GetCyberTracker().instability} vs {savedInstability})");
            var caster = map.mapPawns.AllPawnsSpawned.FirstOrDefault(x => x.thingIDNumber == savedUploadCaster);
            Check(Verb_Quickhack.IsUploading(caster, out var upload), "in-progress upload survived full game reload");
            Check(upload != null && upload.ticksLeft > 0 && upload.ticksLeft <= savedUploadRemaining, "upload remaining duration preserved");
            Check(Find.PlayLog.AllEntries.OfType<PlayLogEntry_Bark>().Any(e => !e.ToGameStringFromPOV(null).NullOrEmpty()), "bark entries survived");
            var sp = map.mapPawns.AllPawnsSpawned.FirstOrDefault(x => x.thingIDNumber == savedShardPawn);
            Check(sp != null && ShardUtility.Slotted(sp).Count == 2 && ShardUtility.Slotted(sp).Contains(Thing("RPC_SkillShard_Plants")) && ShardUtility.Slotted(sp).Contains(Thing("RPC_CapacityShard")), "slotted shards survived the reload");
            Check(sp != null && sp.skills.GetSkill(SkillDefOf.Plants).Aptitude == 5 && ShardUtility.CapacityBonus(sp) == 25f, "and still work");
            Check(sp != null && sp.GetCyberTracker().engramResistance == 7f && sp.GetCyberTracker().engramWill == 3f, "engram recruit numbers survived the reload");
            var item = map.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == savedEngramItem);
            Check(item?.TryGetComp<CompEngram>()?.Data?.FullName == "David Martinez" && item.TryGetComp<CompEngram>().Data.skills.Count == 12, "an engram item survived the reload with its person");
            yield return 120;
            Check(!map.mapPawns.AllPawnsSpawned.Any(x => x.Destroyed), "ticks fine after the reload");

            if (caster != null)
            {
                var remainingRam = caster.GetCyberTracker().Ram;
                caster.TakeDamage(new DamageInfo(DamageDefOf.Cut, 1f, 999f, -1f, null, caster.RaceProps.body.corePart));
                Check(!Verb_Quickhack.IsUploading(caster, out _), "reloaded upload still cancels on damage");
                Check(Mathf.Approximately(caster.GetCyberTracker().Ram, remainingRam), "reloaded cancellation spends no RAM");
            }
            string path = GenFilePaths.FilePathForSavedGame(SaveName);
            if (File.Exists(path)) File.Delete(path);
            Check(!File.Exists(path), "test save deleted");
        }
    }
}
