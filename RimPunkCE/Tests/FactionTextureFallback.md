# Vanilla faction texture fallback

Updated 7 November 2026.

## Result

Arasaka's 180 live apparel PNGs were moved intact from `Common/Textures/Things/RimPunk/Apparel/Arasaka*` to `Source/Art/RetiredArasaka/2026-11-07/Things/RimPunk/Apparel/`. Their SHA-256 hashes and original locations are recorded in the adjacent `manifest.json`. Existing artwork already in `Source/Art/Things/RimPunk/Apparel/` was left in place, so distinct source and live versions are both retained.

All 21 factions other than MaxTac and Trauma Team now use vanilla apparel textures with one primary tint per faction. The game applies that tint directly, preserving vanilla outlines, shading and transparency. There are no generated replacement clothing PNGs. Both channels of masked clothing receive the same tint.

Arasaka's previous apparel definition names, outfit requirements, stats, prices and coverage remain unchanged. This preserves item/loadout references in saves. Arasaka no longer switches back to custom clothing when an art directory happens to exist. Vanilla pants return to vanilla behaviour: an inventory texture and leg coverage, with no separate worn texture.

MaxTac and Trauma Team apparel definitions and all 149 PNGs containing their names, including source art and vehicle graphics, were verified unchanged against a snapshot taken before this task. Their hand-drawn art remains active. Faction insignia and weapon artwork were outside this apparel change.

The old `gen_apparel_textures.py` now writes offline drafts only under `Source/Art/Drafts`, including its manifest and extra vehicle/weapon drafts. It cannot overwrite the live hand-drawn clothing or shuttles when run normally.

## Palette

The editable source is `Source/Tools/data/faction_colours.json`. These RGB values are a readable single-colour interpretation of Cyberpunk 2077 branding/gear, not an official published RGB specification or a claim that every NPC wears a monochrome uniform. Scavengers and Raffen Shiv use practical neutral/rust choices; the mod's Militech Receivables faction follows Militech olive.

| Faction | Primary tint | RGB |
|---|---|---|
| Arasaka | Red | 200, 24, 30 |
| Militech | Olive | 96, 112, 62 |
| Kang Tao | Orange | 230, 126, 35 |
| Biotechnica | Green | 60, 160, 80 |
| Petrochem | Blue | 50, 110, 190 |
| Zetatech | Orange | 225, 145, 35 |
| Night Corp | Navy | 48, 64, 110 |
| NetWatch | Blue | 60, 145, 210 |
| Militech Receivables | Olive | 96, 112, 62 |
| Maelstrom | Red | 170, 28, 38 |
| Tyger Claws | Red | 220, 48, 44 |
| Valentinos | Gold | 212, 168, 52 |
| 6th Street | Blue | 55, 90, 170 |
| Animals | Yellow | 220, 176, 45 |
| Voodoo Boys | Purple | 110, 60, 160 |
| Mox | Pink | 230, 70, 165 |
| Scavengers | Grey | 115, 125, 130 |
| Wraiths | Cyan-blue | 45, 155, 200 |
| BARGHEST | Yellow-green | 155, 190, 48 |
| Aldecaldos | Red | 185, 58, 45 |
| Raffen Shiv | Rust | 155, 65, 42 |

Visual references consulted: [Cyberpunk 2077 logo/resource reference collection](https://www.valencygraphics.com/cyberpunk-2077), [CDPR's official cosplay reference collection](https://www.cyberpunk.net/en/cosplay-guides), [Tyger Claws' licensed black/red merchandise](https://www.galaxus.at/en/s8/product/difuzed-cyberpunk-2077-tiger-claws-mens-short-sleeved-t-shirt-m-m-t-shirts-61143386). The palette remains an implementation choice where the game uses multiple colours.

## Verification

- Asset/definition audit: **277 apparel definitions**, including **268 vanilla-tinted definitions**, passed. All 21 palettes are applied. Item IDs, gameplay fields, clothing layers/coverage/tags and protected definitions match the previous XML.
- Preservation audit: **149 protected PNGs unchanged**, **180 archived Arasaka PNGs hash-identical**, no retired Arasaka clothing PNGs remain in the live texture folders.
- Existing offline suite: **11 tests passed**.
- Build: **zero warnings and errors**.
- In-game apparel load/wear test: **277/277 apparel definitions wore successfully with valid graphics; zero failures or warnings**. The isolated game exited successfully.

The audit and an inspected preview are saved under `Diagnostics/FactionTextures/`. The preview shows the tint operation on representative vanilla shirts, recon armour and helmets for every recoloured faction.

Reproduce from the repository root:

```powershell
python RimPunkCE/Source/Tools/gen_apparel.py
python RimPunkCE/Source/Tools/audit_faction_textures.py
python -m unittest discover -s RimPunkCE/Source/Tools/tests -v
python RimPunkCE/Source/Tools/run_live_tests.py --no-build --filter EveryApparelWears --timeout 240
```

The audit can use the local pre-change snapshot to repeat protected-definition/image checks. Its archive checks use the permanent manifest in `Source/Art`.
