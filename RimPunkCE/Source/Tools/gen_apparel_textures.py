"""Offline draft textures only: vanilla shapes recoloured (flat or shaded), plus simple emblems.
Writes Source/Art/Drafts, never the live textures. Trauma Team and MaxTac's live art is hand-drawn and protected.

    python -I Source/Tools/gen_apparel_textures.py --templates <folder>

<folder> holds vanilla textures as PNGs in their game paths (Things/Pawn/Humanlike/Apparel/ReconArmor/ReconArmor_Male_south.png, ...).
RimWorld 1.7 ships them packed in Assets.zip, so they have to be exported first.

Files you have edited are left alone: every file this script writes is recorded with its hash in data/apparel_textures_generated.txt,
and a file whose hash no longer matches (you changed it) is skipped. Delete a file to have it redrawn.
"""
import argparse
import hashlib
import os

import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from polish_art import polish

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
DRAFTS = os.path.join(ROOT, 'Source', 'Art', 'Drafts')
OUT = os.path.join(DRAFTS, 'Textures', 'Things', 'RimPunk', 'Apparel')
TRACK = os.path.join(DRAFTS, 'apparel_textures_generated.txt')
V = 'Things/Pawn/Humanlike/Apparel/'
BODIES = ['Male', 'Female', 'Thin', 'Hulk', 'Fat']
DIRS = ['south', 'north', 'east']

TT_WHITE, TT_GREY, TT_TEAL, TT_GLOW, OUTLINE = (238, 242, 243), (150, 158, 164), (0, 150, 155), (90, 235, 235), (18, 22, 26)
MT_BLACK, MT_DARK, MT_BLUE, MT_RED, MT_WHITE = (70, 73, 80), (38, 40, 46), (34, 64, 120), (235, 45, 40), (225, 228, 230)
GOLD, YELLOW = (215, 170, 50), (235, 200, 40)

TT_DARK, MT_NAVY = (100, 110, 118), (30, 52, 96)

TT_GREEN = (52, 130, 78)

AR_RED, AR_RED_MID, AR_RED_DARK = (200, 30, 36), (150, 22, 28), (92, 14, 18)      # Arasaka red (helmets, rigs)
AR_BLACK, AR_BLACK_MID, AR_BLACK_DARK, AR_WHITE = (66, 66, 72), (42, 42, 48), (26, 26, 30), (232, 232, 228)

# name: (vanilla template, has body types, palette, decal). palette is (light, mid, dark, visor glow or None) for a shaded
# recolour, or ('flat', colour) for one flat colour inside the vanilla outline. Template '@pants': the legs of the naked body
# (vanilla pants have no worn texture), with Pants/Pants as the item icon.
PIECES = {
    'TraumaShirt':      ('ShirtBasic/ShirtBasic', True, ('camo', TT_GREEN), None),
    'TraumaPants':      ('@pants', True, ('flat', TT_GREEN), None),
    'TraumaVest':       ('FlakVest/FlakVest', True, ('flat', TT_WHITE), None),
    'TraumaHelmet':     ('ReconArmorHelmet/ReconHelmet', False, ('flat', TT_WHITE), None),
    'MaxTacArmor':      ('PowerArmor/PowerArmor', True, (MT_BLACK, MT_DARK, MT_NAVY, None), 'mt_armor'),
    'MaxTacHelmet':     ('PowerArmorHelmet/PowerArmorHelmet', False, (MT_BLACK, MT_DARK, MT_NAVY, MT_RED), None),
    'MaxTacLeadHelmet': ('PowerArmorHelmet/PowerArmorHelmet', False, (MT_BLACK, MT_DARK, MT_NAVY, MT_RED), 'stripe_yellow'),
    # Arasaka: black gear, red helmets and rigs, white shirts for guards and execs
    'ArasakaShirtWhite':   ('ShirtButton/ShirtButton', True, ('flat', AR_WHITE), None),
    'ArasakaShirt':        ('ShirtButton/ShirtButton', True, ('flat', AR_BLACK_MID), None),
    'ArasakaPants':        ('@sprite', True, (AR_BLACK, AR_BLACK_MID, AR_BLACK_DARK, None), None),
    'ArasakaBodysuit':     ('EltexShirt/EltexShirt', True, ('flat', AR_BLACK_MID), None),
    'ArasakaVestLight':    ('EltexVest/EltexVest', True, ('flat', AR_BLACK), None),
    'ArasakaPlateCarrier': ('FlakVest/FlakVest', True, (AR_BLACK, AR_BLACK_MID, AR_BLACK_DARK, None), 'stripe_red'),
    'ArasakaJacket':       ('Jacket/Jacket', True, (AR_BLACK, AR_BLACK_MID, AR_BLACK_DARK, None), None),
    'ArasakaDuster':       ('Duster/Duster', True, (AR_BLACK, AR_BLACK_MID, AR_BLACK_DARK, None), None),
    'ArasakaReconArmor':   ('ReconArmor/ReconArmor', True, (AR_BLACK, AR_BLACK_MID, AR_BLACK_DARK, None), 'stripe_red'),
    'ArasakaHeavyArmor':   ('PowerArmor/PowerArmor', True, (AR_BLACK, AR_BLACK_MID, AR_BLACK_DARK, None), 'stripe_red'),
    'ArasakaCombatHelmet': ('AdvancedHelmet/AdvancedHelmet', False, (AR_RED, AR_RED_MID, AR_RED_DARK, None), None),
    'ArasakaReconHelmet':  ('ReconArmorHelmet/ReconHelmet', False, (AR_RED, AR_RED_MID, AR_RED_DARK, None), None),
    'ArasakaHeavyHelmet':  ('PowerArmorHelmet/PowerArmorHelmet', False, (AR_RED, AR_RED_MID, AR_RED_DARK, None), None),
    'ArasakaNetRig':       ('EltexSkullcap/EltexSkullcap', False, (AR_RED, AR_RED_MID, AR_RED_DARK, None), None),
    'ArasakaVisor':        ('Gunlink/Gunlink', False, (AR_RED, AR_RED_MID, AR_RED_DARK, None), None),
}


def recolour(img, light, mid, dark, glow):
    """Vanilla greyscale -> palette by brightness. Edge pixels and thin dark lines stay outline-black; solid dark areas
    (visors) take the glow colour when there is one."""
    img = img.convert('RGBA')
    a = img.getchannel('A')
    opaque = a.point(lambda v: 255 if v > 128 else 0)
    interior = opaque.filter(ImageFilter.MinFilter(5))
    deep = opaque.filter(ImageFilter.MinFilter(11))   # visors sit well inside the silhouette; thick outlines do not
    hull = opaque.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.MinFilter(9)).filter(ImageFilter.MinFilter(3))  # holes filled
    lum = img.convert('L')
    darkmask = Image.eval(lum, lambda v: 255 if v < 60 else 0)
    darkmask = Image.composite(darkmask, Image.new('L', img.size, 0), a.point(lambda v: 255 if v > 128 else 0))
    solid = darkmask.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))   # opening: drops 1-2px lines
    px, lp, ip, sp, dp, hp = img.load(), lum.load(), interior.load(), solid.load(), deep.load(), hull.load()
    out = Image.new('RGBA', img.size)
    op = out.load()
    for y in range(img.height):
        for x in range(img.width):
            al = px[x, y][3]
            if glow and 0 < al < 220 and hp[x, y]:      # see-through visor glass
                op[x, y] = glow + (235,)
                continue
            if al == 0:
                continue
            L = lp[x, y]
            if not ip[x, y] and L < 150:
                c, k = OUTLINE, 1.0
            elif L < 60:
                c, k = (glow, 1.0) if glow and sp[x, y] and dp[x, y] else (OUTLINE, 1.0)
            elif L < 115:
                c, k = dark, 0.85 + 0.15 * (L - 60) / 55
            elif L < 185:
                c, k = mid, 0.88 + 0.12 * (L - 115) / 70
            else:
                c, k = light, 0.93 + 0.07 * (L - 185) / 70
            op[x, y] = (min(255, int(c[0] * k)), min(255, int(c[1] * k)), min(255, int(c[2] * k)), al)
    return out


def flat(img, colour):
    """One flat colour inside the vanilla silhouette with an even 2px outline. Inner detail goes; see-through parts
    (visors) are filled solid."""
    img = img.convert('RGBA')
    opaque = img.getchannel('A').point(lambda v: 255 if v > 64 else 0)
    shape = opaque.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(5))   # closes small gaps such as visors
    inner = shape.filter(ImageFilter.MinFilter(5))                                      # 2px in from the edge
    out = Image.new('RGBA', img.size, (0, 0, 0, 0))
    out.paste(OUTLINE + (255,), (0, 0), shape)
    out.paste(colour + (255,), (0, 0), inner)
    return out


TT_CAMO = [((42, 110, 64), 0.35), ((66, 148, 92), 0.28), ((124, 132, 128), 0.14)]   # dark green, light green, grey accent


def camo_field(seed, size=128, cells=10):
    """Smooth noise in 0..255, the same for every body type and facing so the pattern stays consistent."""
    import random
    rnd = random.Random(seed)
    small = Image.new('L', (cells, cells))
    small.putdata([rnd.randrange(256) for _ in range(cells * cells)])
    f = small.resize((size, size), Image.BICUBIC).filter(ImageFilter.GaussianBlur(1.5))
    lo, hi = f.getextrema()
    return f.point(lambda v: int((v - lo) * 255 / max(1, hi - lo)))


def camo(img, base, tones=TT_CAMO):
    """Flat colour + outline (as flat()), then hard-edged blobs of slightly different tones (polish() smooths them).
    Each tone is its own noise field; a tone covers roughly its share of the canvas."""
    out = flat(img, base)
    inner = out.getchannel('A').point(lambda v: 255 if v == 255 else 0).filter(ImageFilter.MinFilter(5))
    for k, (colour, share) in enumerate(tones):
        field = camo_field(7 + k * 31, img.width, cells=22 if k < 2 else 30)
        cut = 255 - int(255 * share)
        blob = ImageChops.multiply(field.point(lambda v: 255 if v >= cut else 0), inner)
        out.paste(colour + (255,), (0, 0), blob)
    return out


def legs(body):
    """Pants: the naked body below the hips, outline kept."""
    body = body.convert('RGBA')
    x0, y0, x1, y1 = bbox(body)
    cut = y0 + int((y1 - y0) * 0.62)
    out = body.copy()
    ImageDraw.Draw(out).rectangle((0, 0, out.width, cut), fill=(0, 0, 0, 0))
    return out


def pants_on_body(sprite, body):
    """Vanilla pants sprite (icon, cropped to its content) scaled to the naked legs, feet to feet, centred on the body."""
    body = body.convert('RGBA')
    x0, y0, x1, y1 = bbox(body)
    sprite = sprite.convert('RGBA')
    sprite = sprite.crop(bbox(sprite))
    hip = y0 + (y1 - y0) * 0.5
    s = min((x1 - x0) / sprite.width, (y1 - hip) / sprite.height)
    w, h = max(1, round(sprite.width * s)), max(1, round(sprite.height * s))
    sprite = sprite.resize((w, h), Image.LANCZOS)
    out = Image.new('RGBA', body.size, (0, 0, 0, 0))
    out.paste(sprite, ((x0 + x1 - w) // 2, y1 - h), sprite)
    return out


def bbox(img):
    return img.getchannel('A').point(lambda v: 255 if v > 128 else 0).getbbox() or (0, 0, img.width, img.height)


def clip(layer, base):
    """Keep a decal only where the garment is opaque."""
    m = Image.new('L', base.size, 0)
    m.paste(base.getchannel('A').point(lambda v: 255 if v > 200 else 0))
    m = m.filter(ImageFilter.MinFilter(3))
    a = Image.composite(layer.getchannel('A'), Image.new('L', base.size, 0), m)
    layer.putalpha(a)
    base.alpha_composite(layer)
    return base


def cross(d, cx, cy, s, fill, edge):
    t = max(2, s // 3)
    for box in ((cx - t // 2, cy - s // 2, cx + (t + 1) // 2, cy + s // 2), (cx - s // 2, cy - t // 2, cx + s // 2, cy + (t + 1) // 2)):
        d.rectangle((box[0] - 1, box[1] - 1, box[2], box[3]), fill=edge)
    for box in ((cx - t // 2, cy - s // 2, cx + (t + 1) // 2, cy + s // 2), (cx - s // 2, cy - t // 2, cx + s // 2, cy + (t + 1) // 2)):
        d.rectangle((box[0], box[1], box[2] - 1, box[3] - 1), fill=fill)


def decal(kind, img, direction):
    x0, y0, x1, y1 = bbox(img)
    w, h = x1 - x0, y1 - y0
    cx = (x0 + x1) // 2
    layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    if kind == 'tt_armor':
        if direction == 'south':
            cross(d, cx + w // 6, y0 + int(h * 0.42), max(6, w // 7), TT_TEAL, OUTLINE)       # chest badge, wearer's left
        elif direction == 'north':
            cross(d, cx, y0 + int(h * 0.42), max(10, w // 4), TT_TEAL, OUTLINE)              # big back cross
        else:
            d.rectangle((x0, y0 + int(h * 0.30), x1, y0 + int(h * 0.30) + 2), fill=TT_TEAL)  # shoulder stripe
    elif kind == 'mt_armor':
        if direction == 'south':
            yy = y0 + int(h * 0.40)
            d.rectangle((cx - w // 5, yy, cx + w // 5, yy + 3), fill=MT_WHITE)               # chest bar
            d.rectangle((cx - 2, yy + 6, cx + 2, yy + 10), fill=MT_BLUE)
        elif direction == 'north':
            try:
                font = ImageFont.load_default(size=max(8, w // 7))
            except TypeError:
                font = ImageFont.load_default()
            text = 'MAXTAC'
            tw = d.textlength(text, font=font)
            d.text((cx - tw / 2, y0 + int(h * 0.30)), text, fill=MT_WHITE, font=font)
        else:
            d.rectangle((x0, y0 + int(h * 0.33), x1, y0 + int(h * 0.33) + 3), fill=MT_BLUE)
    elif kind in ('stripe_gold', 'stripe_yellow', 'stripe_red'):
        colour = {'stripe_gold': GOLD, 'stripe_yellow': YELLOW, 'stripe_red': AR_RED}[kind]
        if direction == 'east':
            d.rectangle((x0, y0 + int(h * 0.22), x1, y0 + int(h * 0.22) + 2), fill=colour)
        else:
            d.rectangle((cx - 2, y0, cx + 2, y0 + int(h * 0.45)), fill=colour)
    return clip(layer, img)


def sha(path):
    return hashlib.sha1(open(path, 'rb').read()).hexdigest()


TT_RED = (176, 22, 34)


def star_of_life(d, cx, cy, r, fill, edge):
    """Six-armed medical star: three bars at 0/60/120 degrees, outlined."""
    import math
    for col, grow in ((edge, 1.5), (fill, 0)):
        for ang in (90, 30, -30):
            a = math.radians(ang)
            dx, dy = math.cos(a), -math.sin(a)
            px, py = -dy, dx
            hw = r * 0.22 + grow
            ln = r + grow
            d.polygon([(cx + dx * ln + px * hw, cy + dy * ln + py * hw), (cx + dx * ln - px * hw, cy + dy * ln - py * hw),
                       (cx - dx * ln - px * hw, cy - dy * ln - py * hw), (cx - dx * ln + px * hw, cy - dy * ln + py * hw)], fill=col)


def tt_shuttle(img):
    """Vanilla shuttle (top-down, 512x256) in Trauma Team colours: white hull, green panels, dark glass, red star of life on
    the engine block and a green stripe along the hull."""
    out = recolour(img, TT_WHITE, (178, 184, 188), TT_GREEN, None)
    x0, y0, x1, y1 = bbox(out)
    w, h = x1 - x0, y1 - y0
    glass = recolour(img, TT_WHITE, (178, 184, 188), (54, 70, 82), None)   # cockpit canopy (right 40%) as dark glass
    m = Image.new('L', out.size, 0)
    ImageDraw.Draw(m).rectangle((x0 + int(w * 0.6), 0, out.width, out.height), fill=255)
    out = Image.composite(glass, out, m)
    layer = Image.new('RGBA', out.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    star_of_life(d, x0 + int(w * 0.11), y0 + int(h * 0.42), int(h * 0.16), TT_RED, OUTLINE)
    yy = y0 + int(h * 0.80)
    d.rectangle((x0 + int(w * 0.05), yy, x0 + int(w * 0.93), yy + max(3, h // 40)), fill=TT_GREEN)
    return clip(layer, out)


PIXEL_FONT = {  # 3x5 stencil letters (M is 5 wide)
    'M': ['10001', '11011', '10101', '10001', '10001'], 'A': ['010', '101', '111', '101', '101'], 'X': ['101', '101', '010', '101', '101'],
    'T': ['111', '010', '010', '010', '010'], 'C': ['111', '100', '100', '100', '111'], 'N': ['1001', '1101', '1011', '1001', '1001'],
    'P': ['110', '101', '110', '100', '100'], 'D': ['110', '101', '101', '101', '110'],
}


def stencil(d, x, y, text, scale, fill):
    for ch in text:
        g = PIXEL_FONT[ch]
        for r, row in enumerate(g):
            for k, v in enumerate(row):
                if v == '1': d.rectangle((x + k * scale, y + r * scale, x + (k + 1) * scale - 1, y + (r + 1) * scale - 1), fill=fill)
        x += (len(g[0]) + 1) * scale


def stencil_width(text, scale): return (sum(len(PIXEL_FONT[c][0]) for c in text) + len(text) - 1) * scale


MT_HULL, MT_HULL_MID, MT_GLASS, NCPD_BLUE = (58, 61, 68), (40, 42, 48), (72, 20, 26), (60, 110, 200)


def mt_shuttle(img):
    """Vanilla shuttle (top-down, 512x256) as a MaxTac AV: black hull, navy panels, dark red canopy, white MAXTAC stencil on
    the engine block, NCPD blue stripe along the hull."""
    out = recolour(img, MT_HULL, MT_HULL_MID, MT_NAVY, None)
    x0, y0, x1, y1 = bbox(out)
    w, h = x1 - x0, y1 - y0
    glass = recolour(img, MT_HULL, MT_HULL_MID, MT_GLASS, None)
    m = Image.new('L', out.size, 0)
    ImageDraw.Draw(m).rectangle((x0 + int(w * 0.6), 0, out.width, out.height), fill=255)
    out = Image.composite(glass, out, m)
    layer = Image.new('RGBA', out.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    scale = 3
    tx = x0 + int(w * 0.11) - stencil_width('MAXTAC', scale) // 2
    stencil(d, tx, y0 + int(h * 0.36), 'MAXTAC', scale, MT_WHITE)
    stencil(d, x0 + int(w * 0.11) - stencil_width('NCPD', 2) // 2, y0 + int(h * 0.36) + 7 * scale, 'NCPD', 2, NCPD_BLUE)
    yy = y0 + int(h * 0.80)
    d.rectangle((x0 + int(w * 0.05), yy, x0 + int(w * 0.93), yy + max(3, h // 40)), fill=NCPD_BLUE)
    d.rectangle((x0 + int(w * 0.05), yy + max(3, h // 40) + 1, x0 + int(w * 0.93), yy + max(3, h // 40) + 2), fill=MT_WHITE)
    return clip(layer, out)


# Arasaka weapons: defName -> (vanilla template, palette light/mid/dark). Placeholders: same vanilla gun shape, recoloured by
# type (power = black, smart = dark steel blue to match the blue tracers, tech = white and red).
WEAPON_BASE = 'Things/Item/Equipment/WeaponRanged/'
W_POWER = ((78, 78, 86), (48, 48, 56), (28, 28, 34))
W_SMART = ((96, 120, 158), (54, 72, 104), (30, 40, 62))
W_TECH = ((236, 236, 238), (200, 38, 44), (110, 20, 26))
ARASAKA_WEAPONS = {
    'RPC_Gun_ArasakaHJKE11Yukimura': ('Autopistol', W_SMART),
    'RPC_Gun_ArasakaHJSH18Masamune': ('AssaultRifle', W_POWER),
    'RPC_Gun_ArasakaJKEX2Kenshin': ('ChargeBlasterLight', W_TECH),
    'RPC_Gun_ArasakaNowaki': ('AssaultRifle', W_POWER),
    'RPC_Gun_ArasakaSenkohLX': ('ChargeRifle', W_TECH),
    'RPC_Gun_ArasakaShigure': ('HeavySMG', W_POWER),
    'RPC_Gun_ArasakaTKI20Shingen': ('HeavySMG', W_SMART),
    'RPC_Gun_ArasakaTamayura': ('Autopistol', W_POWER),
}


def weapon_draw(pal):
    return lambda img: recolour(img, pal[0], pal[1], pal[2], None)


# other custom art: output path under Common/Textures -> (vanilla template path, draw function)
EXTRA = {
    **{f'Things/RimPunk/Weapons/{dn}/{dn}': (WEAPON_BASE + tmpl, weapon_draw(pal)) for dn, (tmpl, pal) in ARASAKA_WEAPONS.items()},
    'Things/RimPunk/Vehicles/TraumaShuttle/TraumaShuttle': ('Things/Building/Misc/Shuttle', tt_shuttle),
    'Things/RimPunk/Vehicles/MaxTacShuttle/MaxTacShuttle': ('Things/Building/Misc/Shuttle', mt_shuttle),
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--templates', required=True)
    args = ap.parse_args()
    tracked = {}
    if os.path.exists(TRACK):
        for line in open(TRACK, encoding='utf-8'):
            if line.strip():
                rel, h = line.split()
                tracked[rel] = h
    made, kept = 0, []
    for name, (tmpl, bodies, pal, dec) in PIECES.items():
        jobs = [('', None)] + [(f'_{b}_{dr}' if bodies else f'_{dr}', dr) for b in (BODIES if bodies else [None]) for dr in DIRS]
        for suffix, direction in jobs:
            if tmpl == '@pants':
                src = os.path.join(args.templates, *(V + 'Pants/Pants').split('/')) + '.png' if not suffix else                     os.path.join(args.templates, 'Things', 'Pawn', 'Humanlike', 'Bodies', f'Naked{suffix}.png')
            elif tmpl == '@sprite':
                src = os.path.join(args.templates, *(V + 'Pants/Pants').split('/')) + '.png'
            else:
                src = os.path.join(args.templates, *(V + tmpl + suffix).split('/')) + '.png'
            rel = f'{name}/{name}{suffix}.png'
            dst = os.path.join(OUT, *rel.split('/'))
            if os.path.exists(dst) and tracked.get(rel) != sha(dst):
                kept.append(rel)
                continue
            img = Image.open(src)
            if tmpl == '@pants' and suffix:
                img = legs(img)
            if tmpl == '@sprite' and suffix:
                body = Image.open(os.path.join(args.templates, 'Things', 'Pawn', 'Humanlike', 'Bodies', f'Naked{suffix}.png'))
                img = pants_on_body(img, body)
            if tmpl == '@sprite':
                img = recolour(img, *pal)           # the vanilla sprite's own shading, in black
            elif pal[0] in ('flat', 'camo'):
                # same finish as hand-drawn drafts get from polish_art.py: smooth edges, anti-aliasing, soft shading
                img = polish(flat(img, pal[1]) if pal[0] == 'flat' else camo(img, pal[1]), seams=pal[0] != 'camo')
            else:
                img = recolour(img, *pal)
            if dec:
                img = decal(dec, img, direction or 'south')
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            img.save(dst, optimize=True)
            tracked[rel] = sha(dst)
            made += 1
    for rel_path, (tmpl, draw) in EXTRA.items():
        rel = rel_path + '.png'
        dst = os.path.join(DRAFTS, 'Textures', *rel.split('/'))
        if os.path.exists(dst) and tracked.get(rel) != sha(dst):
            kept.append(rel)
            continue
        img = draw(Image.open(os.path.join(args.templates, *tmpl.split('/')) + '.png'))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        img.save(dst, optimize=True)
        tracked[rel] = sha(dst)
        made += 1
    os.makedirs(os.path.dirname(TRACK), exist_ok=True)
    with open(TRACK, 'w', encoding='utf-8') as fh:
        fh.writelines(f'{k} {v}\n' for k, v in sorted(tracked.items()))
    print(f'{made} textures drawn, {len(kept)} kept (edited by hand)')


if __name__ == '__main__':
    main()
