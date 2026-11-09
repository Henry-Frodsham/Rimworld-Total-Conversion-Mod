# Weapon firing audio integration

Prepared 7 November 2026. Source recordings were read from `D:\RimworldSounds` (plural); the original directory named in chat was singular. No sound generation API was called and no source files were modified.

## Installed

- **41 mono PCM WAVs**, 16-bit / 44.1 kHz, under `Common/Sounds/RimPunk/Guns/` and `Common/Sounds/RimPunk/Arms/Launcher/Fire/`.
- **14 positional, non-looping SoundDefs** in `Common/Defs/Sounds/Generated_WeaponSounds.xml`.
- **52 guns** mapped to appropriate power, tech or smart shot banks in both their base definition and CE compatibility patch.
- The projectile-launch arm plays one launch sound when a shell is actually fired. Empty attempts are silent.

Reloads, impacts, charging, smart-lock acquisition, melee, explosions, transport and callouts retain their existing behaviour. Charge/lock sounds are deliberately not used as firing clips. The separate stock camera-tail sound was removed from the generated guns to avoid layering an unrelated second effect over the edited shot.

## Editing

The importer uses the original raw WAV where available, falling back to the generated OGG only if the raw file is absent. It filters sub-bass/DC and extreme high frequencies, locates a strong attack using a short-time energy envelope, retains one attack, and limits duration by weapon family. A second strong attack after a quiet valley shortens the cut further. Boundary fades prevent discontinuities; they do not add a long fade-in before the shot. Silent/unusable recordings fail the import.

Source lengths of roughly 1.48–4.96 seconds become **0.078–0.440 second** firing clips. Every clip peaks at approximately **−3 dBFS** and starts audibly within **1 ms**. Each has a 0.7 ms attack-boundary fade and up to 20 ms end fade. Final PCM boundaries are zero. The source and output SHA-256 hashes, exact cut locations, duration, peak and RMS are recorded in `Source/Tools/data/weapon_sound_import.json`.

The game chooses variants through its normal sound system. A slight 0.98–1.02 pitch variation prevents identical repeats. Playback tempo does not change with game speed. Clips trigger once per fired round; a shotgun's multiple pellets do not each trigger a sound, and an automatic burst is formed by consecutive shot events rather than a baked multi-shot recording.

## Routing

| Bank | Used by | Maximum clip length |
|---|---|---:|
| Power pistol | Power pistols | 0.24 s |
| Power revolver | Power revolvers | 0.34 s |
| Power SMG | Power SMGs | 0.16 s |
| Power shotgun | Power shotguns | 0.42 s |
| Power rifle | Power assault rifles | 0.20 s |
| Power precision | Power precision rifles | 0.40 s |
| Power sniper | Power sniper rifles | 0.55 s |
| Power MG | Power light/heavy machine guns | 0.22 s |
| Tech light | Tech pistols / SMGs | 0.22 s |
| Tech medium | Tech revolvers / precision rifles | 0.30 s |
| Tech heavy | Tech shotguns / sniper rifles | 0.44 s |
| Smart light | Smart pistols / SMGs | 0.20 s |
| Smart heavy | Smart rifles / shotguns / snipers | 0.30 s |
| Arm launcher | Successful projectile-launch-system discharge | 0.40 s |

Defaults live in `Source/Tools/data/weapon_sounds.json`. `gen_weapons.py` reads this configuration, so regenerating weapons preserves the assignments.

## Verification

- Build: passed, zero warnings/errors.
- Offline suite: **14 tests passed**, including every output's PCM format, duration, peak, onset, boundary samples, hash and bank/routing validation, plus the existing content/generator checks.
- In-engine decoding/playback: **all 41 variants loaded and started on an AudioSource**, with valid non-silent, unclipped PCM.
- Complete live firing checks: **3 scenarios, 379 checks passed, zero failures or warnings**. All 52 guns emitted exactly one imported firing event per fired round, with the correct mechanism bank and no extra vanilla tail. The arm launcher emitted one sound for a successful shot and none for an empty attempt. No diagnostic errors were logged. Successful logs are archived under `Diagnostics/WeaponAudio/live-final.log` and `diag-final.log`.
- Original recording preservation: **41/41 source SHA-256 hashes unchanged** after processing and verification.

The first live run caught a test-observer assumption: CE fires sound events with a cell/map target, so the observation must not require a pawn reference. This was corrected. The runner also now tolerates names that cannot be represented by the Windows console code page when printing failed assertions.

Playback checks validate actual decoding and playback initiation inside the game; they do not constitute a human listening review on speakers. The audition reel in `Diagnostics/WeaponAudio/weapon-audition.wav` contains one representative clip per bank, first alone and then as three consecutive shots. The adjacent text file gives timestamps. This makes the final tone and mix easy to review without replaying combat.

## Reproduce

From `D:\RimworldMods`:

```powershell
python RimPunkCE/Source/Tools/import_weapon_sounds.py --source D:/RimworldSounds
python RimPunkCE/Source/Tools/gen_weapons.py
dotnet build RimPunkCE/Source/RimPunkCE --no-restore -nologo -v q
python -m unittest discover -s RimPunkCE/Source/Tools/tests -v
python RimPunkCE/Source/Tools/run_live_tests.py --no-build --filter weaponaudio --timeout 360
```

The live test uses the existing isolated quick-test game/config/save workflow. Audio test observations are enabled only by that test flag and do not alter normal sound playback or gameplay state.
