using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimPunkCE
{
    /// <summary>
    /// Projectile Launch System, CE ammo: the arm holds a small magazine (size by tier) loaded with one shell type.
    /// Shells are CE AmmoDefs carried in the inventory (Ammo_RPC_LauncherShell_*); a reload job loads the selected type,
    /// returning any shells of another type to the inventory. Each shot is a real CE projectile (ballistic arc,
    /// CE explosion and fragments), launched through CE_Utility.LaunchProjectileCE.
    /// Snap shots are instant but scatter; aimed shots have a warmup and fire the aimed shell defs (+30% damage, +25% radius).
    /// </summary>
    public class CompProperties_AbilityLauncher : CompProperties_AbilityEffect
    {
        public bool aimed;
        public float scatter = 2.5f;

        public CompProperties_AbilityLauncher() => compClass = typeof(CompAbilityEffect_Launcher);
    }

    public class CompAbilityEffect_Launcher : CompAbilityEffect
    {
        public new CompProperties_AbilityLauncher Props => (CompProperties_AbilityLauncher)props;

        private HediffComp_LauncherAmmo Magazine => ArmLauncherUtility.Magazine(parent.pawn);

        public override bool CanCast => Magazine?.Loaded > 0;

        public override bool GizmoDisabled(out string reason)
        {
            var mag = Magazine;
            if (mag == null || mag.Loaded <= 0)
            {
                reason = "RPC_LauncherEmpty".Translate();
                return true;
            }
            return base.GizmoDisabled(out reason);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            var caster = parent.pawn;
            var map = caster.Map;
            var mag = Magazine;
            if (map == null || mag == null) return;
            var shell = mag.Fire(Props.aimed);
            if (shell == null) return;
            var cell = target.Cell;
            if (Props.scatter > 0f)
            {
                var near = GenRadial.RadialCellsAround(cell, Props.scatter, true).Where(c => c.InBounds(map)).ToList();
                if (near.Count > 0) cell = near.RandomElement();
            }
            MapComponent_CyberAnimations.Play(caster, CyberAnimKind.LauncherFire, new LocalTargetInfo(cell), Color.white);
            ArmLauncherUtility.Launch(caster, shell, target.HasThing ? target : new LocalTargetInfo(cell), cell);
            DefDatabase<SoundDef>.GetNamed("RPC_Shot_ArmLauncher").PlayOneShot(new TargetInfo(caster.Position, map));
            if (mag.Loaded <= 0) mag.TryStartReload(queue: true);
        }

        public override bool AICanTargetNow(LocalTargetInfo target) =>
            Magazine?.Loaded > 0 && target.Thing is Pawn p && !p.Downed && p.HostileTo(parent.pawn) && !p.Position.InHorDistOf(parent.pawn.Position, 5f);
    }

    public class HediffCompProperties_LauncherAmmo : HediffCompProperties
    {
        /// <summary>Projectiles per shell type (snap, aimed), parallel to <see cref="ammo"/>.</summary>
        public List<ThingDef> shells = new List<ThingDef>();
        public List<ThingDef> aimedShells = new List<ThingDef>();
        /// <summary>The shell items (CE AmmoDefs) carried in the inventory.</summary>
        public List<ThingDef> ammo = new List<ThingDef>();
        public List<string> shellLabels = new List<string>();
        /// <summary>Magazine size by tier (T1..Iconic).</summary>
        public List<int> magazineByTier = new List<int>();
        public int reloadTicksPerShell = 50;
        /// <summary>Generated NPCs carry this many magazines of spares.</summary>
        public float npcSpareMagazines = 2f;

        public HediffCompProperties_LauncherAmmo() => compClass = typeof(HediffComp_LauncherAmmo);
    }

    public class HediffComp_LauncherAmmo : HediffComp
    {
        /// <summary>The shell type to load next (cycle gizmo).</summary>
        public int selected;
        /// <summary>The shell type in the magazine (-1: never loaded).</summary>
        public int loadedIndex = -1;
        private int loaded;

        public HediffCompProperties_LauncherAmmo Props => (HediffCompProperties_LauncherAmmo)props;

        public int Loaded => loaded;

        public int Capacity
        {
            get
            {
                int t = parent is Hediff_Cyberware cw ? (int)cw.tier : 2;
                if (Props.magazineByTier.NullOrEmpty()) return 3;
                return Mathf.Max(1, Props.magazineByTier[Mathf.Clamp(t, 0, Props.magazineByTier.Count - 1)]);
            }
        }

        private ThingDef AmmoAt(int i) => Props.ammo.NullOrEmpty() ? null : Props.ammo[Mathf.Clamp(i, 0, Props.ammo.Count - 1)];
        private string LabelAt(int i) => i >= 0 && i < Props.shellLabels.Count ? Props.shellLabels[i] : "?";
        public ThingDef SelectedAmmo => AmmoAt(selected);

        public int InInventory(ThingDef ammo) => ammo == null || Pawn.inventory == null ? 0 : Pawn.inventory.innerContainer.TotalStackCountOfDef(ammo);

        /// <summary>A reload would change something: room in the magazine, or another type selected.</summary>
        public bool NeedsReload => loaded < Capacity || (loaded > 0 && loadedIndex != selected);

        public string CantReloadReason()
        {
            if (!NeedsReload) return "RPC_LauncherFull".Translate();
            if (InInventory(SelectedAmmo) <= 0) return "RPC_LauncherNoShells".Translate(LabelAt(selected));
            return null;
        }

        /// <summary>Takes one shell from the magazine; returns the projectile to fire (aimed or snap), or null if empty.</summary>
        public ThingDef Fire(bool aimed)
        {
            if (loaded <= 0 || loadedIndex < 0) return null;
            loaded--;
            var list = aimed && Props.aimedShells.Count == Props.shells.Count ? Props.aimedShells : Props.shells;
            return list.NullOrEmpty() ? null : list[Mathf.Clamp(loadedIndex, 0, list.Count - 1)];
        }

        /// <summary>End of the reload job: unloads another type back to the inventory, then fills from the inventory.</summary>
        public void DoReload()
        {
            if (loaded > 0 && loadedIndex != selected)
            {
                ArmLauncherUtility.GiveToInventory(Pawn, AmmoAt(loadedIndex), loaded);
                loaded = 0;
            }
            loadedIndex = selected;
            int take = Mathf.Min(Capacity - loaded, InInventory(SelectedAmmo));
            if (take <= 0) return;
            ArmLauncherUtility.TakeFromInventory(Pawn, SelectedAmmo, take);
            loaded += take;
        }

        /// <summary>Generated pawns: a full magazine and spares.</summary>
        public void StockForGeneratedPawn()
        {
            if (Props.ammo.NullOrEmpty()) return;
            // HE most of the time, any other type otherwise
            selected = loadedIndex = Rand.Chance(0.5f) ? 0 : Rand.RangeInclusive(0, Props.ammo.Count - 1);
            loaded = Capacity;
            ArmLauncherUtility.GiveToInventory(Pawn, SelectedAmmo, Mathf.RoundToInt(Capacity * Props.npcSpareMagazines));
        }

        /// <summary>Starts (or queues after the current job) a reload. False if there's nothing to load.</summary>
        public bool TryStartReload(bool queue)
        {
            var pawn = Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Dead || CantReloadReason() != null) return false;
            if (pawn.CurJobDef == RPDefOf.RPC_ReloadArmLauncher) return true;
            var job = JobMaker.MakeJob(RPDefOf.RPC_ReloadArmLauncher, pawn);
            if (queue && pawn.CurJob != null) pawn.jobs.jobQueue.EnqueueFirst(job);
            else pawn.jobs.StartJob(job, JobCondition.InterruptForced, resumeCurJobAfterwards: true);
            return true;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            var pawn = Pawn;
            if (loaded > 0 || !pawn.IsHashIntervalTick(300) || !pawn.Spawned || pawn.Downed) return;
            bool ai = !(pawn.Faction?.IsPlayer ?? false) || pawn.InMentalState;
            if (ai)
            {
                // AI and mental states: load whatever type they carry
                if (InInventory(SelectedAmmo) <= 0)
                    for (int i = 0; i < Props.ammo.Count; i++)
                        if (InInventory(Props.ammo[i]) > 0) { selected = i; break; }
                TryStartReload(queue: false);
            }
            else if (pawn.Drafted && pawn.CurJobDef == JobDefOf.Wait_Combat) TryStartReload(queue: false); // drafted and idle
        }

        public override string CompLabelInBracketsExtra =>
            loadedIndex < 0 ? $"0/{Capacity}" : $"{loaded}/{Capacity} {LabelAt(loadedIndex)}";

        public override string CompTipStringExtra =>
            "RPC_LauncherTip".Translate(loaded, Capacity, loadedIndex < 0 ? "-" : LabelAt(loadedIndex), LabelAt(selected),
                string.Join(", ", Props.ammo.Select((a, i) => $"{LabelAt(i)} {InInventory(a)}")));

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            var pawn = Pawn;
            if (!(pawn.Faction?.IsPlayer ?? false) || pawn.InMentalState) yield break;
            if (Props.shells.Count > 1)
                yield return new Command_Action
                {
                    defaultLabel = "RPC_CycleShell".Translate(LabelAt(selected)),
                    defaultDesc = "RPC_CycleShellDesc".Translate(),
                    icon = TextureOverrides.Gizmo("CycleShell"),
                    action = () => CycleShell(pawn, (selected + 1) % Props.shells.Count),
                };
            var reload = new Command_Action
            {
                defaultLabel = "RPC_LauncherReload".Translate(loaded, Capacity),
                defaultDesc = "RPC_LauncherReloadDesc".Translate(LabelAt(selected), InInventory(SelectedAmmo)),
                icon = TextureOverrides.Gizmo("CycleShell"),
                action = () => OrderReload(pawn),
            };
            string why = CantReloadReason();
            if (why != null) reload.Disable(why);
            yield return reload;
        }

        /// <summary>Synced in multiplayer.</summary>
        [Multiplayer.API.SyncMethod]
        public static void CycleShell(Pawn pawn, int index)
        {
            var c = ArmLauncherUtility.Magazine(pawn);
            if (c != null) c.selected = index;
        }

        /// <summary>Synced in multiplayer.</summary>
        [Multiplayer.API.SyncMethod]
        public static void OrderReload(Pawn pawn) => ArmLauncherUtility.Magazine(pawn)?.TryStartReload(queue: false);

        /// <summary>Implant removed from a living pawn: the loaded shells go back to the inventory.</summary>
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            if (loaded > 0 && Pawn != null && !Pawn.Dead) ArmLauncherUtility.GiveToInventory(Pawn, AmmoAt(loadedIndex), loaded);
            loaded = 0;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref selected, "rpcShell");
            Scribe_Values.Look(ref loadedIndex, "rpcShellLoadedType", -1);
            Scribe_Values.Look(ref loaded, "rpcShellLoaded");
        }
    }

    /// <summary>Reloads the arm launcher from the inventory: a short wait per shell, then <see cref="HediffComp_LauncherAmmo.DoReload"/>.</summary>
    public class JobDriver_ReloadArmLauncher : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var mag = ArmLauncherUtility.Magazine(pawn);
            this.FailOn(() => mag == null || mag.CantReloadReason() != null);
            int shells = mag == null ? 1 : Mathf.Max(1, Mathf.Min(mag.Capacity - (mag.loadedIndex == mag.selected ? mag.Loaded : 0), mag.InInventory(mag.SelectedAmmo)));
            var wait = Toils_General.Wait((mag?.Props.reloadTicksPerShell ?? 50) * shells);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;
            var load = ToilMaker.MakeToil("ReloadArmLauncher");
            load.initAction = () => mag?.DoReload();
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return load;
        }
    }

    public static class ArmLauncherUtility
    {
        public static HediffComp_LauncherAmmo Magazine(Pawn pawn)
        {
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return null;
            foreach (var h in hediffs)
                if (h is HediffWithComps hwc && hwc.TryGetComp<HediffComp_LauncherAmmo>() is HediffComp_LauncherAmmo c) return c;
            return null;
        }

        /// <summary>Generated pawn with a launcher: loaded, with spares in the inventory.</summary>
        public static void StockGenerated(Pawn pawn) => Magazine(pawn)?.StockForGeneratedPawn();

        public static void GiveToInventory(Pawn pawn, ThingDef def, int count)
        {
            if (def == null || count <= 0 || pawn?.inventory == null) return;
            while (count > 0)
            {
                var t = ThingMaker.MakeThing(def);
                t.stackCount = Mathf.Min(count, def.stackLimit);
                count -= t.stackCount;
                if (!pawn.inventory.innerContainer.TryAdd(t) && pawn.Spawned)
                    GenPlace.TryPlaceThing(t, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            }
        }

        public static void TakeFromInventory(Pawn pawn, ThingDef def, int count)
        {
            var inner = pawn.inventory.innerContainer;
            for (int i = inner.Count - 1; i >= 0 && count > 0; i--)
            {
                var t = inner[i];
                if (t.def != def) continue;
                int n = Mathf.Min(count, t.stackCount);
                t.SplitOff(n).Destroy();
                count -= n;
            }
        }

        // CE_Utility.LaunchProjectileCE(ThingDef projectileDef, ThingDef ammoDef, Def ammoSetDef, Vector2 origin,
        // LocalTargetInfo target, Pawn launcher, float shotAngle, float shotRotation, float shotHeight, float shotSpeed).
        // Called by reflection: CE is compiled against UnityEngine.CoreModule, this mod against UnityStubs, so the
        // Vector2 parameter is built from CE's own parameter type.
        private static readonly MethodInfo LaunchMethod = typeof(CombatExtended.CE_Utility)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "LaunchProjectileCE" && m.GetParameters().Length == 10);

        private static bool loggedFailure;

        /// <summary>Fires <paramref name="projectile"/> from the caster at <paramref name="cell"/> on a ballistic arc (CE's low-angle solution).</summary>
        public static void Launch(Pawn caster, ThingDef projectile, LocalTargetInfo target, IntVec3 cell)
        {
            var props = projectile.projectile as CombatExtended.ProjectilePropertiesCE;
            if (props == null || LaunchMethod == null)
            {
                if (!loggedFailure) Diag.Check("cyberware", $"arm launcher can't fire {projectile.defName}: CE props={(props != null)} launch method={(LaunchMethod != null)}", "launcherfail");
                loggedFailure = true;
                return;
            }
            var src = caster.TrueCenter();
            var dst = cell.ToVector3Shifted();
            float height = new CombatExtended.CollisionVertical(caster).shotHeight;
            float dx = dst.x - src.x, dz = dst.z - src.z;
            float range = Mathf.Sqrt(dx * dx + dz * dz);
            float speed = props.speed;
            float g = props.GravityPerWidth;
            // BaseTrajectoryWorker.ShotAngle: atan((v² - sqrt(v⁴ - g(g·d² + 2·h·v²))) / (g·d)), aiming at the ground (h = -shotHeight)
            float angle;
            if (range < 0.5f) angle = 0f;
            else
            {
                float v2 = speed * speed;
                float disc = v2 * v2 - g * (g * range * range + 2f * -height * v2);
                angle = disc < 0f ? Mathf.PI / 4f : Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * range));
            }
            float rotation = (-90f + 57.29578f * Mathf.Atan2(dz, dx)) % 360f;
            try
            {
                var originType = LaunchMethod.GetParameters()[3].ParameterType;
                object origin = Activator.CreateInstance(originType, src.x, src.z);
                LaunchMethod.Invoke(null, new object[] { projectile, null, null, origin, target, caster, angle, rotation, height, speed });
            }
            catch (Exception e)
            {
                if (!loggedFailure) Diag.Check("cyberware", $"arm launcher launch failed: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}", "launcherfail");
                loggedFailure = true;
            }
        }
    }
}
