"""Ability, gizmo and faction icons from game-icons.net (CC BY 3.0), rendered in one RimPunk style.
    pip install pillow
    python Source/Tools/gen_icons.py              downloads the icon pack once (~30 MB, to %TEMP%), renders everything
    python Source/Tools/gen_icons.py --list hour  search glyph names (for changing the tables below)

Output goes where TextureOverrides.cs looks (Common/Textures/RimPunk/...). Only files this script made are overwritten
(tracked in data/icons_generated.txt): drop your own art at the same path and it is left alone.
To change an icon: edit its glyph name or colour below and re-run. Credits for the glyphs used: data/icon_credits.txt.
"""
import argparse
import os
import sys
import tempfile
import urllib.request
import zipfile

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
TEX = os.path.join(ROOT, 'Common', 'Textures', 'RimPunk')
DATA = os.path.join(os.path.dirname(__file__), 'data')
TRACK = os.path.join(DATA, 'icons_generated.txt')
PACK_URL = 'https://game-icons.net/archives/png/zip/ffffff/transparent/game-icons.net.png.zip'
CACHE = os.path.join(tempfile.gettempdir(), 'rimpunk-game-icons')
AUTHOR_PREF = ['lorc', 'delapouite']

# colours by role
SANDY, BERSERK, MOVE, OPTICS, HACK, LAUNCH, HEAL, CAMO, WIRE = (
    '#3de8ff', '#ff3b4e', '#ffd23a', '#c86bff', '#4fa8ff', '#ff8a2a', '#5dff8f', '#cfe8ff', '#e8e8e8')
SHOCK, THERMAL, TOXIC = '#4fa8ff', '#ff7a2a', '#8dff3a'

ABILITIES = {  # defName: (glyph, colour)
    'RPC_Ability_DynalarSandevistan': ('hourglass', SANDY),
    'RPC_Ability_ZetatechSandevistan': ('stopwatch', SANDY),
    'RPC_Ability_WarpDancerSandevistan': ('sands-of-time', SANDY),
    'RPC_Ability_FalconSandevistan': ('falcon-moon', SANDY),
    'RPC_Ability_ApogeeSandevistan': ('time-synchronization', SANDY),
    'RPC_Ability_MooreTechBerserk': ('enrage', BERSERK),
    'RPC_Ability_BioDyneBerserk': ('muscle-up', BERSERK),
    'RPC_Ability_ZetatechBerserk': ('screaming', BERSERK),
    'RPC_Ability_MilitechBerserk': ('brute', BERSERK),
    'RPC_Ability_BloodPump': ('bleeding-heart', HEAL),
    'RPC_Ability_CyberJump': ('jump-across', MOVE),
    'RPC_Ability_CyberJumpPaired': ('jump-across', MOVE),
    'RPC_Ability_CyberJumpCharges': ('high-kick', MOVE),
    'RPC_Ability_CyberJumpChargesPaired': ('high-kick', MOVE),
    'RPC_Ability_MantisDash': ('praying-mantis', MOVE),
    'RPC_Ability_ThrowWeapon': ('thrown-daggers', MOVE),
    'RPC_Ability_MonowireLash': ('whip', WIRE),
    'RPC_Ability_ElectrifyingMonowireLash': ('whip', SHOCK),
    'RPC_Ability_ThermalMonowireLash': ('whip', THERMAL),
    'RPC_Ability_ToxicMonowireLash': ('whip', TOXIC),
    'RPC_Ability_KiroshiClairvoyantOpticsScan': ('crystal-ball', OPTICS),
    'RPC_Ability_KiroshiDoomsayerOpticsScan': ('bleeding-eye', OPTICS),
    'RPC_Ability_KiroshiOracleOpticsScan': ('third-eye', OPTICS),
    'RPC_Ability_KiroshiSentryOpticsScan': ('radar-sweep', OPTICS),
    'RPC_Ability_LauncherAimed': ('missile-launcher', LAUNCH),
    'RPC_Ability_LauncherSnap': ('rocket', LAUNCH),
    'RPC_Ability_OpticalCamo': ('invisible', CAMO),
    'RPC_Quickhack_ShortCircuit': ('lightning-arc', HACK),
}
GIZMOS = {  # RimPunk/UI/<name>, looked up by TextureOverrides.Gizmo
    'ToggleBlades': ('praying-mantis', MOVE),
    'CycleShell': ('anticlockwise-rotation', LAUNCH),
}
FACTIONS = {  # white glyphs: the game tints them with the faction colour
    'RPC_Faction_Aldecaldos': 'caravan', 'RPC_Faction_Animals': 'paw', 'RPC_Faction_Arasaka': 'samurai-helmet',
    'RPC_Faction_Barghest': 'wolf-head', 'RPC_Faction_Biotechnica': 'dna2', 'RPC_Faction_KangTao': 'dragon-head',
    'RPC_Faction_Maelstrom': 'android-mask', 'RPC_Faction_MaxTac': 'police-badge', 'RPC_Faction_Militech': 'eagle-emblem',
    'RPC_Faction_Mox': 'cat', 'RPC_Faction_NetWatch': 'eye-shield', 'RPC_Faction_NightCorp': 'moon',
    'RPC_Faction_Petrochem': 'oil-pump', 'RPC_Faction_RaffenShiv': 'bowie-knife', 'RPC_Faction_Scavengers': 'scalpel',
    'RPC_Faction_SixthStreet': 'usa-flag', 'RPC_Faction_TraumaTeam': 'medical-pack', 'RPC_Faction_TygerClaws': 'tiger-head',
    'RPC_Faction_Valentinos': 'rose', 'RPC_Faction_VoodooBoys': 'voodoo-doll', 'RPC_Faction_Wraiths': 'spectre',
    'RPC_Faction_Zetatech': 'processor',
}


def ensure_pack(path):
    if path:
        return path
    root = os.path.join(CACHE, 'icons', 'ffffff', 'transparent', '1x1')
    if not os.path.isdir(root):
        os.makedirs(CACHE, exist_ok=True)
        zpath = os.path.join(CACHE, 'pack.zip')
        print(f'downloading game-icons.net pack to {CACHE} ...')
        urllib.request.urlretrieve(PACK_URL, zpath)
        with zipfile.ZipFile(zpath) as z:
            for m in z.namelist():  # extract only plain png paths inside the cache
                if m.endswith('.png') and '..' not in m and not os.path.isabs(m):
                    z.extract(m, CACHE)
    return root


def index(root):
    found = {}
    for author in sorted(os.listdir(root), key=lambda a: (a not in AUTHOR_PREF, AUTHOR_PREF.index(a) if a in AUTHOR_PREF else 0, a)):
        folder = os.path.join(root, author)
        for fn in os.listdir(folder):
            if fn.endswith('.png'):
                found.setdefault(fn[:-4], (os.path.join(folder, fn), author))
    return found


def rgb(hexcol):
    return tuple(int(hexcol[i:i + 2], 16) for i in (1, 3, 5))


def mask(glyph_path, size):
    a = Image.open(glyph_path).convert('RGBA').getchannel('A')
    return a.resize((size, size), Image.LANCZOS)


def solid(size, colour, alpha):
    img = Image.new('RGBA', (size, size), colour + (0,))
    img.putalpha(alpha)
    return img


def ability_icon(glyph_path, colour, S=128):
    c = rgb(colour)
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    # backdrop: rounded dark plate, vertical gradient, faint scanlines
    plate = Image.new('L', (S, S), 0)
    ImageDraw.Draw(plate).rounded_rectangle((1, 1, S - 2, S - 2), radius=14, fill=255)
    grad = Image.new('RGBA', (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        base = tuple(int(a + (b - a) * t) for a, b in zip((24, 30, 40), (8, 10, 14)))
        tint = tuple(int(v * 0.88 + cc * 0.12 * (1 - t)) for v, cc in zip(base, c))
        gd.line((0, y, S, y), fill=tint + (255,) if y % 3 else tuple(max(0, v - 6) for v in tint) + (255,))
    grad.putalpha(plate)
    img.alpha_composite(grad)
    # glyph with glow
    g = int(S * 0.70)
    m = Image.new('L', (S, S), 0)
    m.paste(mask(glyph_path, g), ((S - g) // 2, (S - g) // 2))
    m = ImageChops.multiply(m, plate)
    glow = m.filter(ImageFilter.GaussianBlur(S / 18)).point(lambda v: min(255, int(v * 1.6)))
    img.alpha_composite(solid(S, c, glow))
    bright = tuple(int(v + (255 - v) * 0.3) for v in c)
    img.alpha_composite(solid(S, bright, m))
    # neon border
    border = Image.new('L', (S, S), 0)
    ImageDraw.Draw(border).rounded_rectangle((1, 1, S - 2, S - 2), radius=14, outline=150, width=2)
    img.alpha_composite(solid(S, c, border))
    return img


def white_icon(glyph_path, S, fill=0.86):
    g = int(S * fill)
    m = Image.new('L', (S, S), 0)
    m.paste(mask(glyph_path, g), ((S - g) // 2, (S - g) // 2))
    return solid(S, (255, 255, 255), m)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--icons', help='folder of game-icons.net 1x1 PNGs (author sub-folders); default: download')
    ap.add_argument('--list', metavar='TEXT', help='print glyph names containing TEXT and exit')
    args = ap.parse_args()
    glyphs = index(ensure_pack(args.icons))
    if args.list:
        print(' '.join(sorted(n for n in glyphs if args.list in n)))
        return

    tracked = set(open(TRACK, encoding='utf-8').read().split()) if os.path.exists(TRACK) else set()
    jobs = []
    for name, (glyph, colour) in ABILITIES.items():
        jobs.append((f'Abilities/{name}.png', glyph, lambda p, c=colour: ability_icon(p, c)))
    for name, (glyph, colour) in GIZMOS.items():
        jobs.append((f'UI/{name}.png', glyph, lambda p, c=colour: ability_icon(p, c)))
    for name, glyph in FACTIONS.items():
        jobs.append((f'Factions/{name}.png', glyph, lambda p: white_icon(p, 128)))
        jobs.append((f'Factions/{name}_Settlement.png', glyph, lambda p: white_icon(p, 64, 0.9)))

    missing = sorted({g for _, g, _ in jobs if g not in glyphs})
    if missing:
        sys.exit(f'unknown glyph names: {missing} (use --list to search)')

    made, kept, used = [], [], {}
    for rel, glyph, render in jobs:
        out = os.path.join(TEX, *rel.split('/'))
        if os.path.exists(out) and rel not in tracked:
            kept.append(rel)
            continue
        os.makedirs(os.path.dirname(out), exist_ok=True)
        render(glyphs[glyph][0]).save(out, optimize=True)
        made.append(rel)
        used.setdefault(glyphs[glyph][1], set()).add(glyph)

    os.makedirs(DATA, exist_ok=True)
    open(TRACK, 'w', encoding='utf-8').write('\n'.join(sorted(set(made) | (tracked - set(kept)))) + '\n')
    with open(os.path.join(DATA, 'icon_credits.txt'), 'w', encoding='utf-8') as fh:
        fh.write('Icons from game-icons.net, CC BY 3.0 (https://creativecommons.org/licenses/by/3.0/), recoloured.\n')
        for author in sorted(used):
            fh.write(f'  {author}: {", ".join(sorted(used[author]))}\n')
    print(f'{len(made)} icons rendered, {len(kept)} kept (your own art); credits -> Source/Tools/data/icon_credits.txt')


if __name__ == '__main__':
    main()
