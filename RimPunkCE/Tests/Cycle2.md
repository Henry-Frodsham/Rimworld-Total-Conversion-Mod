# Cycle 2 implementation and test record

Date: 7 November 2026. Changes are implemented locally; no commit has been created.

## Implemented behaviour

- **Armour:** only Carapace, Cellular Adapter, Chitin, Cogito Lattice, Nano-plating, RangeGuard, Subdermal Armor, Titanium Bones, and explicit scrap plating supply flat protection. Values are reduced to 40% of their previous amounts. Para Bellum, Rara Avis and Scar Coalescer multiply plating only. Implant protection is capped after multipliers at 10 mm sharp / 25 MPa blunt on every body part. Natural race armour and worn apparel remain separate. General implant effective health and NPC generation probabilities are unchanged.
- **Uploads:** distance is sampled when casting starts; duration is `clamp(lerp(60,120,distance/range)/uploadSpeed,60,120)` normal-speed seconds. Gun aiming-delay modifiers do not shorten it. Uploaders remain stationary. Damage, movement orders, lost targets and disabled features cancel uploads without a delayed effect. RAM is charged only on completion; interrupted casting retains the game's existing job-end cooldown behaviour. Active uploads retain their origin and remaining duration across saves.
- **Trauma Team:** a standing patient remains eligible for physical extraction. Downed pawns do not gain the adrenaline surge's pain suppression. Adrenaline numbers are unchanged.
- **Combat AI:** ranged builds hold usable firing positions, seek spacing/cover, and react to the netrunner currently uploading into them. Corps prefer 75% of weapon range and place more weight on cover; gangs prefer 50%; other styles use 65%, bounded to 7–28 tiles. Hack targeting includes chrome load alongside sniper/netrunner priority. Melee builds still close. Decisions use deterministic scans rather than new random sources.
- **Weapons:** the approved 18 gun and 8 melee multipliers are applied through the source data and generator. Gun damage uses each weapon's `RangedWeapon_DamageMultiplier`, preserving shared ammo and penetration. Melee primary tool powers change; secondary shock riders and prices do not. CE rounds projectile damage: Carnage's default 7-damage buckshot remains 7 after its 1.05 multiplier. The approved multiplier still applies to its other ammo.
- **World features:** independent gigs and quickhack/netrun choices are made through the world-creation screen and saved with the world. The mod settings page supplies defaults for future worlds and shows current locked choices. New defaults are off. Worlds saved before these flags existed retain both features. Disabled gameplay paths, cast validation, menu entries, shard use, gig offers, the gig tab and NetWatch initiation are gated.
- **Cyberpsychosis:** capacity thresholds no longer cause automatic breaks. Chrome load, event spikes and recovery actions drive a saved instability value. Temporary episodes can expire without reducing instability. Rare permanent rogue outcomes require heavy high-tier chrome. Existing permanent states are preserved. Existing immunity rules remain. Immunoblockers retain their capacity effects and provide normal drug recovery; Last Stand expiry adds instability instead of universally forcing permanent psychosis.

## Initial psychosis tuning

These are implementation defaults for playtesting, not values settled by the original Q&A.

| Parameter | Initial value |
|---|---|
| Chrome load | Each own implant: `1 + 0.2 × zero-based tier`; each foreign implant: 1 |
| Baseline increase | 0.002 instability per chrome-load unit per simulated second |
| Damage / kill / Sandy or Berserk activation | 2 / 6 / 8, multiplied by `1 + 0.04 × chrome load` |
| Breaking point | 100 instability |
| Permanent eligibility | Chrome load ≥12 and at least 3 implants at T4 or above |
| Permanent probability | 3% per eligible break, scaled by the existing psychosis setting |
| Temporary duration | 3,600 + 120 × chrome load ticks, capped at 7,200 |
| Treatment ends temporary episode early | Instability ≤20 |
| Grace after episode | 6,000 ticks |
| Sleep recovery | 0.08 per simulated second of sleep |
| Recreation recovery | 40 per full joy-level gain, based on actual gain |
| Drug recovery | 12 per ingestion |
| Medical recovery | Up to 15 per treatment batch, scaled by quality |

The existing psychosis multiplier scales accumulation/event spikes and permanent-outcome probability. Instability never decreases just because time passes, a temporary episode ends, or capacity changes. Permanent probability uses `Verse.Rand`; rendering does not generate simulation randomness.

## Suites and commands

From `D:\RimworldMods`:

```powershell
dotnet build RimPunkCE/Source/RimPunkCE --no-restore -nologo -v q
python -m unittest discover -s RimPunkCE/Source/Tools/tests -v
python RimPunkCE/Source/Tools/run_live_tests.py --no-build --filter cycle2 --timeout 1200
python RimPunkCE/Source/Tools/run_live_tests.py --no-build --timeout 1200
```

The live runner uses a separate quick-test world/config/save directory and temporary dependency junctions. Its game process exits after the suite. It does not edit the user's normal saves or active mod list.

The **11 offline tests** cover all XML parsing, definition uniqueness, plating classification, unchanged NPC generation parameters, the exact approved weapon manifest, CE patch multipliers, independently specified melee powers, ammo/projectile references, psychosis tuning, translation uniqueness, and reproducible generation in an isolated directory.

The **12 new live scenarios** cover tier/body-part armour matrices through CE, multiplier-only builds, apparel protection, distance timing, uninterrupted uploads, damage cancellation/no ghost completion, movement and target loss, every feature combination with actual Scribe serialization, legacy defaults, faction range styles, anti-netrun focus, chrome targeting, physical cover, event-driven instability, real recreation/drug/tending/sleep actions, temporary and permanent break branches, and standing-patient extraction/return. The existing full-game reload scenario additionally verifies world choices, instability, an in-progress upload, and damage cancellation after reload.

## Execution results

- Build: passed with zero warnings/errors.
- Offline suite: 11 passed.
- First dedicated cycle 2 run: 12 scenarios, 594 checks passed, zero failures/warnings.
- Final full regression: **59 scenarios, 854 checks passed, zero failures, zero warnings**. Game process exited successfully. This includes all 12 new cycle 2 scenarios and the full-game reload checks. Simulation test time: 65 seconds; total process time including startup/world generation: 106 seconds.
- Final diagnostics: no `ERROR` entries, matching the pre-change baseline's zero errors.

Earlier runs exposed and corrected an unfiltered-run null argument, incident episodes starting without instability, and tests checking carriage before extraction. Existing UI/sweep scenarios were updated to use uninterrupted targets and pay high RAM costs through Overclock. Zero-scenario runs now fail explicitly.

Archived run logs and the pre-change diagnostic baseline are under `Diagnostics/Cycle2`. The successful full run is archived as `full-final.log` and `full-final-diag.log`. Current full-run logs are `Diagnostics/livetest.log` and `Diagnostics/diag.log`. The approved numeric weapon table is copied to [Cycle2-weapons.md](Cycle2-weapons.md).

## Practical limits

These checks establish the tested behaviour, not final gameplay balance. Psychosis rates and faction tactics still need player feedback. Full multiplayer validation and a manual world-creation/settings UI walkthrough have not been performed. No save-breaking reset was introduced; migration of feature flags and new-state round trips are covered, but every possible old third-party save is not tested.
