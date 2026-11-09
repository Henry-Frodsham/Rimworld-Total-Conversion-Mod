"""Audit the vanilla faction recolours, archival integrity, and protected hand-drawn art; render a preview.

Run after gen_apparel.py. Reads the pre-change snapshot in Diagnostics/FactionTextures when present.
"""
from pathlib import Path
import hashlib
import json
import xml.etree.ElementTree as ET
from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
REPO = ROOT.parent
OUT = ROOT / 'Diagnostics/FactionTextures'
PALETTE = json.loads((ROOT / 'Source/Tools/data/faction_colours.json').read_text(encoding='utf-8'))
PROTECTED = ('RPC_Apparel_TraumaTeam_', 'RPC_Apparel_MaxTac_')


def read_defs(path):
    return {node.findtext('defName'): node for node in ET.parse(path).getroot() if node.findtext('defName')}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    current = read_defs(ROOT / 'Common/Defs/Apparel/Generated_Apparel.xml')
    before_path = OUT / 'apparel-before.xml'
    before = read_defs(before_path) if before_path.exists() else None
    failures = []
    checked = 0
    if before is not None:
        if current.keys() != before.keys(): failures.append('Apparel def identities changed')
    for name, node in current.items():
        fid = next((key for key in PALETTE if name.startswith(f'RPC_Apparel_{key}_')), None)
        if name.startswith(PROTECTED):
            if before is not None and ET.tostring(node) != ET.tostring(before[name]): failures.append(f'{name}: protected definition changed')
            continue
        if fid is None:
            failures.append(f'{name}: no faction palette')
            continue
        checked += 1
        graphic = node.find('graphicData')
        path = graphic.findtext('texPath')
        if not path.startswith('Things/Pawn/Humanlike/Apparel/'): failures.append(f'{name}: non-vanilla item texture {path}')
        worn = node.findtext('apparel/wornGraphicPath')
        if worn and not worn.startswith('Things/Pawn/Humanlike/Apparel/'): failures.append(f'{name}: non-vanilla worn texture {worn}')
        expected = '(' + ','.join(map(str, PALETTE[fid]['rgb'])) + ')'
        if graphic.findtext('color') != expected: failures.append(f'{name}: wrong tint')
        second = graphic.findtext('colorTwo')
        if second and second != expected: failures.append(f'{name}: wrong secondary tint')
        if before is not None:
            # Artwork-only change: stats, layers, coverage, recipes, tags and tech level remain identical.
            for tag in ('statBases', 'equippedStatOffsets', 'costList', 'techLevel', 'thingCategories'):
                old, new = before[name].find(tag), node.find(tag)
                if (ET.tostring(old) if old is not None else None) != (ET.tostring(new) if new is not None else None):
                    failures.append(f'{name}: gameplay field {tag} changed')
            for tag in ('bodyPartGroups', 'layers', 'tags'):
                if ET.tostring(before[name].find('apparel/' + tag)) != ET.tostring(node.find('apparel/' + tag)):
                    failures.append(f'{name}: apparel {tag} changed')

    protected_path = OUT / 'protected-before.json'
    protected_count = 0
    if protected_path.exists():
        for entry in json.loads(protected_path.read_text(encoding='utf-8-sig')):
            path = Path(entry['Path'])
            if not path.exists() or digest(path) != entry['Hash']: failures.append(f'Protected hand-drawn art changed: {path}')
            protected_count += 1

    manifest_path = ROOT / 'Source/Art/RetiredArasaka/2026-11-07/manifest.json'
    archived = 0
    if manifest_path.exists():
        for entry in json.loads(manifest_path.read_text(encoding='utf-8-sig')):
            path = ROOT / entry['Archived']
            if not path.exists() or digest(path) != entry['SHA256']: failures.append(f'Archive mismatch: {path}')
            if (ROOT / 'Common/Textures' / entry['Original']).exists(): failures.append(f'Retired art still live: {entry["Original"]}')
            archived += 1

    # Preview the actual operation used by RimWorld: multiply vanilla RGB by the faction tint, retaining alpha/detail.
    examples = ['ShirtButton/ShirtButton_Male_south', 'ReconArmor/ReconArmor_Male_south', 'ReconArmorHelmet/ReconHelmet_south']
    size = (210, 200)
    preview = Image.new('RGB', (size[0] * 7, size[1] * 3), (30, 33, 40))
    draw = ImageDraw.Draw(preview)
    missing = []
    for index, (fid, palette) in enumerate(PALETTE.items()):
        x, y = (index % 7) * size[0], (index // 7) * size[1]
        draw.text((x + 10, y + 8), fid, fill=(235, 238, 245))
        for j, example in enumerate(examples):
            source = REPO / 'vanilla_textures/Things/Pawn/Humanlike/Apparel' / (example + '.png')
            if not source.exists():
                missing.append(str(source))
                continue
            img = Image.open(source).convert('RGBA')
            tinted = ImageChops.multiply(img.convert('RGB'), Image.new('RGB', img.size, tuple(palette['rgb'])))
            tinted.putalpha(img.getchannel('A'))
            tinted.thumbnail((92, 104), Image.Resampling.LANCZOS)
            px, py = (x + 8, y + 36) if j == 0 else (x + 104, y + 36) if j == 1 else (x + 60, y + 100)
            preview.paste(tinted, (px, py), tinted)
        draw.rectangle((x + 12, y + 171, x + 29, y + 188), fill=tuple(palette['rgb']))
        draw.text((x + 36, y + 174), palette['name'], fill=(220, 225, 232))
    preview.save(OUT / 'faction-vanilla-preview.png')
    failures.extend('Missing preview source: ' + path for path in sorted(set(missing)))
    lines = [f'Apparel definitions checked: {len(current)}', f'Primary-colour vanilla apparel checked: {checked}',
        f'Faction palettes: {len(PALETTE)}', f'Protected MaxTac / Trauma Team PNGs unchanged: {protected_count}',
        f'Arasaka apparel PNGs archived intact: {archived}', f'Failures: {len(failures)}'] + failures
    (OUT / 'audit.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print('\n'.join(lines))
    if failures: raise SystemExit(1)


if __name__ == '__main__':
    main()
