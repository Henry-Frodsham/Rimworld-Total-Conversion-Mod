using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>
    /// Content sweeps: every item of each kind goes through the real game once, so a broken def, comp or texture fails
    /// here without needing its own scenario. A failed item is named, and the sweep carries on with the next.
    /// </summary>
    public partial class GameComponent_RimPunkLiveTests
    {
        private void Sweep<T>(string what, IReadOnlyCollection<T> all, List<string> bad)
        {
            Check(bad.Count == 0, $"{all.Count - bad.Count}/{all.Count} {what}" + (bad.Count > 0 ? ". Broken: " + string.Join("; ", bad.Take(12)) : ""));
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryCyberwareEveryTier()
        {
            var bad = new List<string>();
            var all = CyberwareDefCache.AllCyberware.ToList();
            var p = Colonist(Center);
            int n = 0;
            foreach (var def in all)
            {
                var ext = def.Ext();
                try
                {
                    if (ext.item == null) bad.Add(def.defName + ": no item");
                    else if (ext.item.GetCompProperties<CompProperties_CyberwareTier>() == null) bad.Add(def.defName + ": item has no tier comp");
                    for (var tier = ext.minTier; tier <= ext.maxTier; tier++)
                    {
                        RimPunkDebugActions.DebugInstall(p, def, tier);
                        var c = p.InstalledCyberware().FirstOrDefault(x => x.def == def);
                        if (c == null)
                        {
                            bad.Add($"{def.defName} {tier}: didn't install");
                            break;
                        }
                        if (c.tier != tier) bad.Add($"{def.defName} {tier}: installed as {c.tier}");
                        if (c.CurStage == null) bad.Add($"{def.defName} {tier}: no stage");
                        _ = c.LabelInBrackets;
                        _ = c.TipStringExtra;
                        _ = c.CapacityCost;
                        var give = c.TryGetComp<HediffComp_GiveCyberAbilities>();
                        if (give != null && give.Props.abilities.Any(a => p.abilities.GetAbility(a) == null)) bad.Add($"{def.defName} {tier}: ability not granted");
                        p.health.RemoveHediff(c);
                        if (give != null && give.Props.abilities.Any(a => p.abilities.GetAbility(a) != null)) bad.Add($"{def.defName} {tier}: ability left behind");
                    }
                }
                catch (System.Exception e)
                {
                    bad.Add($"{def.defName}: threw {e.GetType().Name}: {e.Message}");
                }
                if (++n % 10 == 0) yield return 1; // let hediff comps tick with whatever is installed
            }
            Sweep("cyberware install, show and remove at every tier", all, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryCyberwareTicksInCombat()
        {
            // installed and ticking under fire: triggers, conditionals and on-hit comps run for real
            var all = CyberwareDefCache.AllCyberware.ToList();
            var bad = new List<string>();
            foreach (var batch in all.Select((d, i) => (d, i)).GroupBy(x => x.i / 20))
            {
                var p = Colonist(Center);
                var foe = Dummy(Center + new IntVec3(1, 0, 0));
                foreach (var (d, _) in batch)
                {
                    try
                    {
                        var ext = d.Ext();
                        bool fits = ext.slot.WholeBody ? CyberUtility.CanInstall(p, d, null, out _)
                            : CyberSurgeryUtility.CandidateParts(p, ext.slot).Any(x => CyberUtility.CanInstall(p, d, x, out _));
                        if (fits) RimPunkDebugActions.DebugInstall(p, d, ext.maxTier);
                    }
                    catch (System.Exception e) { bad.Add($"{d.defName}: {e.Message}"); }
                }
                yield return 35;
                p.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 0f, -1f, foe));
                foe.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 0f, -1f, p));
                yield return 35;
                foe.Kill(new DamageInfo(DamageDefOf.Cut, 500f, 0f, -1f, p));
                yield return 35;
                if (p.Dead) bad.Add("batch killed its own pawn: " + string.Join(",", batch.Select(x => x.d.defName)));
                else p.Destroy();
                if (!foe.Destroyed) foe.Destroy();
            }
            Sweep("cyberware survive combat events while installed", all, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryCyberwareAbilityCasts()
        {
            var bad = new List<string>();
            var pairs = new List<(HediffDef source, AbilityDef ability)>();
            foreach (var d in CyberwareDefCache.AllCyberware)
            {
                var give = d.CompProps<HediffCompProperties_GiveCyberAbilities>();
                if (give != null) foreach (var a in give.abilities) pairs.Add((d, a));
            }
            foreach (var (source, abilityDef) in pairs)
            {
                var p = Colonist(Center);
                p.drafter.Drafted = true;
                var foe = Dummy(Center + new IntVec3(5, 0, 0));
                RimPunkDebugActions.DebugInstall(p, source, source.Ext().maxTier);
                var t = p.GetCyberTracker();
                t.SetAdrenaline(9999f);
                t.GainRam(9999f);
                ArmLauncherUtility.StockGenerated(p);
                var a = p.abilities.GetAbility(abilityDef);
                if (a == null)
                {
                    bad.Add(abilityDef.defName + ": not granted");
                    Cleanup(p, foe);
                    continue;
                }
                if (!a.CanCast.Accepted)
                {
                    bad.Add($"{abilityDef.defName}: can't cast: {a.CanCast.Reason}");
                    Cleanup(p, foe);
                    continue;
                }
                var tp = abilityDef.verbProperties.targetParams;
                LocalTargetInfo target;
                if (!abilityDef.verbProperties.targetable || (tp.canTargetSelf && !tp.canTargetPawns)) target = p;
                else if (tp.canTargetPawns && abilityDef.hostile) target = foe;
                else if (tp.canTargetLocations) target = Center + new IntVec3(-4, 0, 0);
                else if (tp.canTargetPawns) target = foe;
                else target = p;
                string threw = null;
                try { p.jobs.StartJob(a.GetJob(target, target), JobCondition.InterruptForced); }
                catch (System.Exception e) { threw = e.Message; }
                int last = a.lastCastTick;
                bool used = false;
                for (int i = 0; i < 40 && !used && threw == null; i++)
                {
                    yield return 10;
                    if (p.Destroyed || p.Dead) break;
                    used = a.lastCastTick != last || a.CooldownTicksRemaining > 0 || BuffActive(a as CyberwareAbility);
                }
                if (threw != null) bad.Add($"{abilityDef.defName}: threw {threw}");
                else if (!used) bad.Add($"{abilityDef.defName}: never fired (job {p.CurJobDef?.defName})");
                // never destroy a pawn mid-flight: the flyer still holds them
                for (int i = 0; i < 60 && p.ParentHolder is PawnFlyer; i++) yield return 5;
                yield return 5;
                Cleanup(p, foe);
            }
            Sweep("cyberware abilities fire", pairs, bad);
        }

        private void Cleanup(params Pawn[] pawns)
        {
            foreach (var p in pawns) Remove(p);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryQuickhackLands()
        {
            var bad = new List<string>();
            var all = QuickhackLoadoutUtility.AllQuickhacks.ToList();
            Check(all.Count >= 17, $"{all.Count} quickhacks defined");
            var comp = MapComponent_Netrun.For(map);
            foreach (var hack in all)
            {
                var ext = hack.GetModExtension<QuickhackExtension>();
                foreach (var tier in new[] { ext.minTier, ext.maxTier }.Distinct())
                {
                    var caster = Colonist(Center);
                    var victim = Standing(Center + new IntVec3(8, 0, 0));
                    RimPunkDebugActions_Scenarios.LearnAllAt(caster, tier);
                    var t = caster.GetCyberTracker();
                    t.LearnQuickhack(hack, tier);
                    t.GainRam(9999f);
                    t.SyncQuickhacks();
                    var a = caster.abilities.GetAbility(hack) as QuickhackAbility;
                    if (a == null)
                    {
                        // Blackwall Gateway only comes with an Iconic Canto
                        if (hack != QuickhackDefOf.RPC_Quickhack_BlackwallGateway) bad.Add($"{hack.defName} {tier}: not granted");
                        Cleanup(caster, victim);
                        continue;
                    }
                    int before = comp.ResultCount(NetrunResult.Uploaded);
                    bool threw = false;
                    try { a.Activate(new LocalTargetInfo(victim), new LocalTargetInfo(victim)); }
                    catch (System.Exception e)
                    {
                        threw = true;
                        bad.Add($"{hack.defName} {tier}: threw {e.GetType().Name}: {e.Message}");
                    }
                    if (!threw && comp.ResultCount(NetrunResult.Uploaded) <= before) bad.Add($"{hack.defName} {tier}: no result");
                    yield return 15; // DoTs and debuffs tick
                    Cleanup(caster, victim);
                }
            }
            Sweep("quickhacks land at their lowest and highest tier", all, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryGunFires()
        {
            var guns = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_Gun_")).OrderBy(d => d.defName).ToList();
            var bad = new List<string>();
            foreach (var gun in guns)
            {
                yield return 30; // rounds from the last gun are still in the air: they'd hit this one's target
                var p = Colonist(Center, gun);
                var foe = Dummy(Center + new IntVec3(8, 0, 0));
                if (WeaponClassUtility.IsSmart(p.equipment.Primary)) RimPunkDebugActions.DebugInstall(p, Hediff("RPC_SmartLink"), CyberTier.T3);
                p.drafter.Drafted = true;
                var ammo = p.equipment.Primary?.TryGetComp<CompAmmoUser>();
                if (ammo == null)
                {
                    bad.Add(gun.defName + ": no CE ammo");
                    Cleanup(p, foe);
                    continue;
                }
                if (ammo.CurMagCount == 0) ammo.ResetAmmoCount();
                int mag = ammo.CurMagCount;
                var job = JobMaker.MakeJob(JobDefOf.AttackStatic, foe);
                job.playerForced = true;
                p.jobs.StartJob(job, JobCondition.InterruptForced);
                bool fired = false;
                for (int i = 0; i < 40 && !fired; i++)
                {
                    yield return 8;
                    fired = ammo.CurMagCount < mag;
                }
                if (!fired)
                {
                    var v = p.equipment.Primary?.TryGetComp<CompEquippable>()?.PrimaryVerb;
                    bad.Add($"{gun.defName}: didn't fire (job {p.CurJobDef?.defName}, stance {p.stances.curStance?.GetType().Name}, verb {v?.GetType().Name} available {v?.Available()}, canHit {v?.CanHitTarget(foe)}, mag {ammo.CurMagCount}/{mag}, inventory mass {MassUtility.InventoryMass(p):0.#} of {MassUtility.Capacity(p):0.#}, foe dead {foe.Dead}, CE bulk {p.GetStatValue(StatDefOf.CarryingCapacity):0.#})");
                }
                Cleanup(p, foe);
            }
            Sweep("guns fire their ammo", guns, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryAmmoTypeFlies()
        {
            // every projectile the generator made gets launched once, from the gun that uses it
            var guns = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_Gun_")).ToList();
            var sets = guns.Select(g => g.GetCompProperties<CompProperties_AmmoUser>()?.ammoSet).Where(s => s != null).Distinct().ToList();
            var bad = new List<string>();
            int total = 0;
            foreach (var set in sets)
            {
                if (set.ammoTypes.NullOrEmpty()) bad.Add(set.defName + ": empty ammo set");
                else foreach (var link in set.ammoTypes)
                {
                    total++;
                    if (link.projectile?.projectile == null) bad.Add($"{set.defName}/{link.ammo?.defName}: no projectile");
                    else if (link.projectile.projectile.GetDamageAmount((Thing)null) <= 0 && link.projectile.projectile.explosionRadius <= 0f) bad.Add($"{link.projectile.defName}: no damage");
                    else if (link.ammo?.BaseMarketValue <= 0f) bad.Add($"{link.ammo?.defName}: no value");
                }
            }
            Check(bad.Count == 0, $"{total} ammo types in {sets.Count} sets" + (bad.Count > 0 ? ". Broken: " + string.Join("; ", bad.Take(12)) : ""));
            yield break;
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryMeleeWeaponHits()
        {
            var melee = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_Melee_")).OrderBy(d => d.defName).ToList();
            var bad = new List<string>();
            foreach (var def in melee)
            {
                var p = Colonist(Center, def);
                var foe = Dummy(Center + new IntVec3(1, 0, 0));
                p.drafter.Drafted = true;
                if (p.equipment.Primary?.def != def)
                {
                    bad.Add(def.defName + ": not equipped");
                    Cleanup(p, foe);
                    continue;
                }
                float hp = foe.health.summaryHealth.SummaryHealthPercent;
                var job = JobMaker.MakeJob(JobDefOf.AttackMelee, foe);
                job.playerForced = true;
                job.killIncappedTarget = true;
                p.jobs.StartJob(job, JobCondition.InterruptForced);
                bool hit = false;
                for (int i = 0; i < 50 && !hit; i++)
                {
                    yield return 10;
                    hit = foe.Destroyed || foe.Dead || foe.health.summaryHealth.SummaryHealthPercent < hp;
                }
                if (!hit) bad.Add($"{def.defName}: no damage (job {p.CurJobDef?.defName})");

                var thrown = p.equipment?.Primary?.TryGetComp<CompEquippableAbility>()?.AbilityForReading;
                if (thrown != null && !p.Dead)
                {
                    var foe2 = Dummy(Center + new IntVec3(5, 0, 0));
                    p.jobs.StartJob(thrown.GetJob(foe2, foe2), JobCondition.InterruptForced);
                    bool went = false;
                    for (int i = 0; i < 30 && !went; i++)
                    {
                        yield return 10;
                        went = ThrownWeaponUtility.IsThrown(p);
                    }
                    if (!went) bad.Add($"{def.defName}: throw never happened (job {p.CurJobDef?.defName})");
                    Cleanup(foe2);
                }
                Cleanup(p, foe);
            }
            Sweep("melee weapons hit", melee, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryPawnKindGenerates()
        {
            var kinds = DefDatabase<PawnKindDef>.AllDefsListForReading.Where(k => k.defName.StartsWith("RPC_")).OrderBy(k => k.defName).ToList();
            var bad = new List<string>();
            int n = 0, chromed = 0;
            foreach (var k in kinds)
            {
                Faction f = k.defaultFactionDef == null ? null : Find.FactionManager.FirstFactionOfDef(k.defaultFactionDef);
                try
                {
                    var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(k, f, forceGenerateNewPawn: true));
                    if (p.InstalledCyberware().Any()) chromed++;
                    var loadout = k.GetModExtension<CyberwareLoadoutExtension>();
                    if (loadout != null && loadout.options.Any(o => o.chance >= 1f) && !p.InstalledCyberware().Any()) bad.Add(k.defName + ": guaranteed cyberware missing");
                    bool armed = k.weaponMoney.max > 0f && !k.weaponTags.NullOrEmpty();
                    if (armed && p.equipment?.Primary == null)
                    {
                        // a budget roll can come up short once in a while; never getting a weapon is the bug
                        int without = 1, tries = 8;
                        for (int i = 1; i < tries; i++)
                        {
                            var again = PawnGenerator.GeneratePawn(new PawnGenerationRequest(k, f, forceGenerateNewPawn: true));
                            if (again.equipment?.Primary == null) without++;
                            again.Destroy();
                        }
                        Note($"{k.defName}: unarmed {without} of {tries} (money {k.weaponMoney}, tags {string.Join(",", k.weaponTags)})");
                        if (without == tries) bad.Add(k.defName + ": never gets a weapon");
                    }
                    p.Destroy();
                }
                catch (System.Exception e)
                {
                    bad.Add($"{k.defName}: threw {e.GetType().Name}: {e.Message}");
                }
                if (++n % 10 == 0) yield return 1;
            }
            Note($"{chromed}/{kinds.Count} generated with chrome");
            Sweep("pawn kinds generate", kinds, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryFactionFieldsARaidGroup()
        {
            var factions = Find.FactionManager.AllFactionsListForReading.Where(f => f.Ext() != null && !f.defeated).ToList();
            Check(factions.Count > 0, $"{factions.Count} RimPunk factions in this world");
            var defs = DefDatabase<FactionDef>.AllDefsListForReading.Where(d => d.HasModExtension<CyberFactionExtension>()).ToList();
            var bad = new List<string>();
            foreach (var def in defs)
            {
                var combat = def.pawnGroupMakers?.Where(m => m.kindDef == PawnGroupKindDefOf.Combat).ToList();
                if (combat.NullOrEmpty()) continue;
                var f = factions.FirstOrDefault(x => x.def == def);
                if (f == null) continue;
                try
                {
                    var pawns = PawnGroupMakerUtility.GeneratePawns(new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Combat, faction = f, points = 800f, tile = map.Tile }).ToList();
                    if (pawns.Count == 0) bad.Add(def.defName + ": empty raid group at 800 points");
                    foreach (var p in pawns) p.Destroy();
                }
                catch (System.Exception e)
                {
                    bad.Add($"{def.defName}: threw {e.Message}");
                }
                yield return 1;
            }
            Sweep("faction defs make raid groups", defs, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryApparelWears()
        {
            var apparel = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsApparel && d.defName.StartsWith("RPC_")).OrderBy(d => d.defName).ToList();
            var bad = new List<string>();
            var p = Colonist(Center);
            int n = 0;
            foreach (var def in apparel)
            {
                try
                {
                    p.apparel.DestroyAll();
                    var a = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                    p.apparel.Wear(a, dropReplacedApparel: false);
                    if (!p.apparel.WornApparel.Contains(a)) bad.Add(def.defName + ": not worn");
                    else if (!def.apparel.wornGraphicPath.NullOrEmpty() && !ApparelGraphicRecordGetter.TryGetGraphicApparel(a, p.story.bodyType, false, out _)) bad.Add(def.defName + ": no worn graphic");
                }
                catch (System.Exception e)
                {
                    bad.Add($"{def.defName}: threw {e.Message}");
                }
                if (++n % 20 == 0) yield return 1;
            }
            p.apparel.DestroyAll();
            Sweep("apparel wears, with graphics", apparel, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_EveryDamageTypeApplies()
        {
            var defs = DefDatabase<DamageDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_")).ToList();
            var bad = new List<string>();
            foreach (var d in defs)
            {
                var human = Dummy(Center + new IntVec3(3, 0, 0));
                var mech = PawnGenerator.GeneratePawn(PawnKindDefOf.Mech_Scyther, Faction.OfMechanoids);
                GenSpawn.Spawn(mech, Center + new IntVec3(-3, 0, 0), map);
                spawned.Add(mech);
                var from = Dummy(Center + new IntVec3(0, 0, 3));
                foreach (var target in new Pawn[] { human, mech })
                {
                    try { target.TakeDamage(new DamageInfo(d, 8f, 2f, -1f, from)); }
                    catch (System.Exception e) { bad.Add($"{d.defName} on {target.def.defName}: threw {e.Message}"); }
                }
                yield return 30; // DoT hediffs tick
                Cleanup(human, mech, from);
            }
            Sweep("damage types hit a human and a mech", defs, bad);
        }

        [LiveScenario("sweep")]
        private IEnumerable<int> Scenario_ContentIntegrity()
        {
            var bad = new List<string>();
            foreach (var r in DefDatabase<RecipeDef>.AllDefsListForReading.Where(r => r.defName.StartsWith("RPC_")))
            {
                if (r.Worker == null) bad.Add(r.defName + ": no worker");
                if (r.researchPrerequisite != null && r.researchPrerequisite.techLevel == TechLevel.Undefined) bad.Add(r.defName + ": bad research");
            }
            int install = DefDatabase<RecipeDef>.AllDefsListForReading.Count(r => r.Worker is Recipe_InstallCyberware);
            int all = CyberwareDefCache.AllCyberware.Count;
            Check(install == all, $"an install surgery per cyberware: {install} of {all}");
            int upgrade = DefDatabase<RecipeDef>.AllDefsListForReading.Count(r => r.Worker is Recipe_UpgradeCyberware);
            int steps = CyberwareDefCache.AllCyberware.Sum(d => (int)d.Ext().maxTier - (int)d.Ext().minTier);
            Check(upgrade >= steps, $"an upgrade surgery per tier step: {upgrade} for {steps} steps");
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_Gun_") || d.defName.StartsWith("RPC_Melee_")))
            {
                if (d.recipeMaker == null && d.costList.NullOrEmpty()) bad.Add(d.defName + ": can't be made");
                if (d.graphicData != null && d.graphic == BaseContent.BadGraphic) bad.Add(d.defName + ": missing texture");
            }
            foreach (var d in DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_")))
                if (d.baseCost <= 0f) bad.Add(d.defName + ": no cost");
            foreach (var d in CyberwareDefCache.AllCyberware)
                if (d.Ext().item?.graphic == BaseContent.BadGraphic) bad.Add(d.defName + ": item has no texture");
            Check(bad.Count == 0, "recipes, research and textures resolve" + (bad.Count > 0 ? ". Broken: " + string.Join("; ", bad.Take(15)) : ""));
            yield break;
        }
    }
}
