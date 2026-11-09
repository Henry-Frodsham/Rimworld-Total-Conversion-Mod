using System;
using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimPunkCE
{
    /// <summary>Observation only, enabled by the isolated live-test flag. Uses the actual game's sound dispatch.</summary>
    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot))]
    public static class WeaponAudioObservation
    {
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        public static int Count(SoundDef sound) => sound != null && counts.TryGetValue(sound.defName, out int n) ? n : 0;
        public static void Prefix(SoundDef soundDef, SoundInfo info)
        {
            // CE dispatches with a cell+map TargetInfo, so Maker.Thing is normally null.
            if (!GameComponent_RimPunkLiveTests.Enabled || soundDef?.defName?.StartsWith("RPC_Shot_") != true) return;
            counts[soundDef.defName] = Count(soundDef) + 1;
        }
    }

    public partial class GameComponent_RimPunkLiveTests
    {
        [LiveScenario("weaponaudio")]
        private IEnumerable<int> Scenario_WeaponAudio_DecodesAndPlaysEveryVariant()
        {
            var p = Colonist(Center);
            var banks = DefDatabase<SoundDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_Shot_")).ToList();
            Check(banks.Count == 14, $"all 14 firing sound banks loaded ({banks.Count})");
            int variants = 0;
            foreach (var bank in banks)
            {
                Check(!bank.sustain && bank.HasSubSoundsInWorld && bank.context == SoundContext.MapOnly, bank.defName + ": positional one-shot, no loop");
                foreach (var sub in bank.subSounds)
                {
                    Check(!sub.tempoAffectedByGameSpeed && sub.startDelayRange.max == 0f, bank.defName + ": no firing delay or speed-dependent pitch");
                    var clips = sub.grains.SelectMany(g => g.GetResolvedGrains()).OfType<ResolvedGrain_Clip>().ToList();
                    Check(clips.Count >= 2, bank.defName + ": multiple decoded variants");
                    foreach (var resolved in clips)
                    {
                        var clip = resolved.clip;
                        Check(clip != null && clip.channels == 1 && clip.frequency == 44100 && clip.length >= 0.055f && clip.length <= 0.56f,
                            bank.defName + "/" + clip?.name + ": short mono 44.1 kHz clip");
                        if (clip == null) continue;
                        var data = new float[clip.samples * clip.channels];
                        bool read = clip.GetData(data, 0);
                        Check(read && data.Any(v => Mathf.Abs(v) > 0.01f) && data.All(v => !float.IsNaN(v) && Mathf.Abs(v) < 0.74f),
                            bank.defName + "/" + clip.name + ": decoded PCM is non-silent and below clipping");
                        var playing = SampleOneShot.TryMakeAndPlay(sub, clip, SoundInfo.InMap(p));
                        Check(playing?.source != null && playing.source.clip == clip && playing.source.isPlaying,
                            bank.defName + "/" + clip.name + ": engine AudioSource starts playback");
                        playing?.source?.Stop();
                        variants++;
                        yield return 1;
                    }
                }
            }
            Check(variants == 41, $"all 41 imported variants decoded and exercised ({variants})");
        }

        [LiveScenario("weaponaudio")]
        private IEnumerable<int> Scenario_WeaponAudio_EveryGunDispatchesOneSoundPerRound()
        {
            var guns = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("RPC_Gun_")).OrderBy(d => d.defName).ToList();
            Check(guns.Count == 52, "52 guns covered");
            foreach (var gun in guns)
            {
                yield return 30;
                var p = Colonist(Center, gun);
                var target = Dummy(Center + new IntVec3(8, 0, 0));
                if (WeaponClassUtility.IsSmart(p.equipment.Primary)) RimPunkDebugActions.DebugInstall(p, Hediff("RPC_SmartLink"), CyberTier.T3);
                p.drafter.Drafted = true;
                var verb = p.equipment.Primary.TryGetComp<CompEquippable>().PrimaryVerb;
                var sound = verb.verbProps.soundCast;
                Check(sound != null && sound.defName.StartsWith("RPC_Shot_"), gun.label + ": custom firing sound survives CE patch");
                Check(verb.verbProps.soundCastTail == null, gun.label + ": no second vanilla firing tail layered on top");
                string type = gun.weaponTags.FirstOrDefault(t => t.StartsWith("RPC_Type_"));
                Check(sound != null && sound.defName.Contains(type == "RPC_Type_tech" ? "_Tech_" : type == "RPC_Type_smart" ? "_Smart_" : "_Power_"),
                    gun.label + ": sound matches power/tech/smart mechanism");
                var ammo = p.equipment.Primary.TryGetComp<CompAmmoUser>();
                if (ammo.CurMagCount == 0) ammo.ResetAmmoCount();
                int rounds = ammo.CurMagCount, soundCount = WeaponAudioObservation.Count(sound);
                var job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                job.playerForced = true;
                p.jobs.StartJob(job, JobCondition.InterruptForced);
                for (int i = 0; i < 150 && ammo.CurMagCount == rounds; i++) yield return 6;
                int fired = rounds - ammo.CurMagCount, emitted = WeaponAudioObservation.Count(sound) - soundCount;
                Check(fired > 0 && emitted == fired, gun.label + $": {fired} rounds fired, {emitted} firing events (job {p.CurJobDef?.defName}, stance {p.stances.curStance?.GetType().Name}, canHit {verb.CanHitTarget(target)})");
                Cleanup(p, target);
            }
        }

        [LiveScenario("weaponaudio")]
        private IEnumerable<int> Scenario_WeaponAudio_ArmLauncherOnlyOnSuccessfulFire()
        {
            var p = Colonist(Center);
            RimPunkDebugActions.DebugInstall(p, Hediff("RPC_ProjectileLaunchSystem"), CyberTier.T3);
            ArmLauncherUtility.StockGenerated(p);
            var mag = ArmLauncherUtility.Magazine(p);
            var ability = p.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("RPC_Ability_LauncherSnap"));
            var comp = ability?.CompOfType<CompAbilityEffect_Launcher>();
            Check(mag != null && comp != null, "launcher and firing comp available");
            if (mag == null || comp == null) yield break;
            ArmLauncherUtility.GiveToInventory(p, mag.Props.ammo[0], 10);
            mag.DoReload();
            var sound = DefDatabase<SoundDef>.GetNamed("RPC_Shot_ArmLauncher");
            int before = WeaponAudioObservation.Count(sound);
            int loaded = mag.Loaded;
            comp.Apply(new LocalTargetInfo(Center + new IntVec3(15, 0, 0)), LocalTargetInfo.Invalid);
            Check(loaded > 0 && mag.Loaded == loaded-1 && WeaponAudioObservation.Count(sound) == before+1, "loaded launcher emits one sound with one shell");
            while (mag.Loaded > 0) mag.Fire(false);
            before = WeaponAudioObservation.Count(sound);
            comp.Apply(new LocalTargetInfo(Center + new IntVec3(15, 0, 0)), LocalTargetInfo.Invalid);
            Check(WeaponAudioObservation.Count(sound) == before, "empty launcher emits no firing sound");
            yield return 2;
        }
    }
}
