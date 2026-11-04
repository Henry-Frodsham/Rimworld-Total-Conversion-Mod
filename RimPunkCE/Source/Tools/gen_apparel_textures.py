"""Draft textures for the custom faction gear (Trauma Team, MaxTac): vanilla armour shapes recoloured, plus simple emblems.
Meant as a starting point for hand refinement.

    python -I Source/Tools/gen_apparel_textures.py --templates <folder>

<folder> holds vanilla textures as PNGs in their game paths (Things/Pawn/Humanlike/Apparel/ReconArmor/ReconArmor_Male_south.png, ...).
RimWorld 1.7 ships them packed in Assets.zip, so they have to be exported first.

Files you have edited are left alone: every file this script writes is recorded with its hash in data/apparel_textures_generated.txt,
and a file whose hash no longer matches (you changed it) is skipped. Delete a file to have it redrawn.
"""
import argparse
import hashlib
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Common', 'Textures', 'Things', 'RimPunk', 'Apparel')
TRACK = os.path.join(os.path.dirname(__file__), 'data', 'apparel_textures_generated.txt')
V = 'Things/Pawn/Humanlike/Apparel/'
BODIES = ['Male', 'Female', 'Thin', 'Hulk', 'Fat']
DIRS = ['south', 'north', 'east']

TT_WHITE, TT_GREY, TT_TEAL, TT_GLOW, OUTLINE = (238, 242, 243), (150, 158, 164), (0, 150, 155), (90, 235, 235), (18, 22, 26)
MT_BLACK, MT_DARK, MT_BLUE, MT_RED, MT_WHITE = (70, 73, 80), (38, 40, 46), (34, 64, 120), (235, 45, 40), (225, 228, 230)
GOLD, YELLOW = (215, 170, 50), (235, 200, 40)

TT_DARK, MT_NAVY = (100, 110, 118), (30, 52, 96)

# name: (vanilla template, has body types, palette (light, mid, dark, visor glow or None), decal)
PIECES = {
    'TraumaArmor':      ('ReconArmor/ReconArmor', True, (TT_WHITE, TT_GREY, TT_DARK, None), 'tt_armor'),
    'TraumaHelmet':     ('ReconArmorHelmet/ReconHelmet', False, (TT_WHITE, TT_GREY, TT_DARK, TT_GLOW), None),
    'TraumaLeadHelmet': ('ReconArmorHelmet/ReconHelmet', False, (TT_WHITE, TT_GREY, TT_DARK, TT_GLOW), 'stripe_gold'),
    'MaxTacArmor':      ('PowerArmor/PowerArmor', True, (MT_BLACK, MT_DARK, MT_NAVY, None), 'mt_armor'),
    'MaxTacHelmet':     ('PowerArmorHelmet/PowerArmorHelmet', False, (MT_BLACK, MT_DARK, MT_NAVY, MT_RED), None),
    'MaxTacLeadHelmet': ('PowerArmorHelmet/PowerArmorHelmet', False, (MT_BLACK, MT_DARK, MT_NAVY, MT_RED), 'stripe_yellow'),
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
    elif kind in ('stripe_gold', 'stripe_yellow'):
        colour = GOLD if kind == 'stripe_gold' else YELLOW
        if direction == 'east':
            d.rectangle((x0, y0 + int(h * 0.22), x1, y0 + int(h * 0.22) + 2), fill=colour)
        else:
            d.rectangle((cx - 2, y0, cx + 2, y0 + int(h * 0.45)), fill=colour)
    return clip(layer, img)


def sha(path):
    return hashlib.sha1(open(path, 'rb').read()).hexdigest()


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
            src = os.path.join(args.templates, *(V + tmpl + suffix).split('/')) + '.png'
            rel = f'{name}/{name}{suffix}.png'
            dst = os.path.join(OUT, *rel.split('/'))
            if os.path.exists(dst) and tracked.get(rel) != sha(dst):
                kept.append(rel)
                continue
            img = recolour(Image.open(src), *pal)
            if dec:
                img = decal(dec, img, direction or 'south')
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
