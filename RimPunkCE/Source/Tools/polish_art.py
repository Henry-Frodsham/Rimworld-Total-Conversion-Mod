"""Finish rough flat-colour drafts into game-ready textures.

    python -I Source/Tools/polish_art.py                      polish every draft under Source/Art/
    python -I Source/Tools/polish_art.py TraumaHelmet         only drafts whose path contains "TraumaHelmet"
    python -I Source/Tools/polish_art.py --no-shade ...       smoothing and outline only

Draw drafts in Source/Art/ using the same relative path the texture has under Common/Textures/
(e.g. Source/Art/Things/RimPunk/Apparel/TraumaHelmet/TraumaHelmet_south.png). Drafts are never modified; the result is
written to Common/Textures/<same path>. Drafts narrower than 128 are scaled up to 128 (64x64 -> 128x128); bigger
ones (vehicles, buildings) keep their size.

Draft rules (what the tool expects):
  - flat colours, no anti-aliasing, transparent background
  - a dark outline is optional: the tool draws its own even one around the silhouette
  - dark colours inside the shape (visors, grilles) are kept and get a glossy highlight
  - details of 1-4 pixels (rivets, dots) are kept as they are; bigger shapes get their corners rounded

Steps: colours snapped to the draft's palette -> 8x supersample -> each colour region smoothed (rounded corners, no
stair-steps) -> top-left light: bevel highlight on top-left edges, shadow on bottom-right, soft top-to-bottom falloff,
dark seam where two colours meet, gloss on dark regions -> even outline -> downsample (anti-aliased edges).
"""
import argparse
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
DRAFTS = os.path.join(ROOT, 'Source', 'Art')
OUT = os.path.join(ROOT, 'Common', 'Textures')
SIZE = 128
K = 8                                  # supersampling factor
OUTLINE = (18, 22, 26)
OUTLINE_PX = 1.6                       # outline width in final pixels


def lum(c): return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def scale(c, f): return tuple(max(0, min(255, int(v * f))) for v in c)


def mix(a, b, t): return tuple(int(x + (y - x) * t) for x, y in zip(a, b))


def palette(img):
    """Colours used by at least 3 pixels (draft noise snaps to the nearest of these)."""
    counts = {}
    for p in img.get_flattened_data():
        if p[3] > 128: counts[p[:3]] = counts.get(p[:3], 0) + 1
    pal = [c for c, n in counts.items() if n >= 3] or list(counts)
    return pal


def nearest(c, pal): return min(pal, key=lambda p: sum((a - b) ** 2 for a, b in zip(c, p)))


DARK = 38                              # below this brightness a colour counts as outline / glass (gets gloss, no shading)


def is_outline(c): return lum(c) < DARK


def polish(src, shade=True, seams=True):
    """src: a file path or an Image. seams=False skips the dark line between colours (camo, patterns)."""
    draft = (src if isinstance(src, Image.Image) else Image.open(src)).convert('RGBA')
    pal = palette(draft)
    w, h = draft.size
    dp = draft.load()

    # label map at draft resolution (-1 = empty)
    labels = [[-1] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            p = dp[x, y]
            if p[3] > 128: labels[y][x] = pal.index(nearest(p[:3], pal))

    # small components (rivets, dots) are kept exact; everything else gets smoothed
    seen = [[False] * w for _ in range(h)]
    small = []
    for y in range(h):
        for x in range(w):
            if seen[y][x] or labels[y][x] < 0: continue
            lab, stack, comp = labels[y][x], [(x, y)], []
            seen[y][x] = True
            while stack:
                cx, cy = stack.pop(); comp.append((cx, cy))
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and labels[ny][nx] == lab:
                        seen[ny][nx] = True; stack.append((nx, ny))
            if len(comp) <= 4: small.append((lab, comp))

    # thick dark areas (visors, grilles) get gloss; thin dark lines (the draft's outline, seams) do not
    dark = Image.new('L', (w, h), 0)
    dk = dark.load()
    for y in range(h):
        for x in range(w):
            if labels[y][x] >= 0 and is_outline(pal[labels[y][x]]): dk[x, y] = 255
    thick_dark = dark.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(5))

    # work at the draft's own resolution; KK = hi-res pixels per draft pixel (final image is SIZE x SIZE, K per final px)
    # output: drafts under SIZE wide are scaled up (64 -> 128); bigger ones (buildings, vehicles) keep their size.
    # k = supersampling per output pixel, lower for big images to keep it fast
    scale = SIZE // w if w < SIZE else 1
    ow, oh = w * scale, h * scale
    k = max(2, K * SIZE // ow)
    KK = k * scale
    W, H = w * KK, h * KK
    sil = Image.new('L', (w, h), 0)
    sp = sil.load()
    for y in range(h):
        for x in range(w):
            if labels[y][x] >= 0: sp[x, y] = 255
    blur = ImageFilter.GaussianBlur(KK * 0.9)
    sil_hi = sil.resize((W, H), Image.NEAREST).filter(blur).point(lambda v: 255 if v >= 128 else 0)

    masks, thin = [], []
    for i in range(len(pal)):
        m = Image.new('L', (w, h), 0)
        mp = m.load()
        for y in range(h):
            for x in range(w):
                if labels[y][x] == i: mp[x, y] = 255
        masks.append(m.resize((W, H), Image.NEAREST).filter(blur))
        # 1-2px wide lines would be eaten by smoothing: keep them exactly (they still get anti-aliased at the end)
        opened = m.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
        thin.append(ImageChops.subtract(m, opened).resize((W, H), Image.NEAREST))
    # each hi-res pixel takes the colour whose smoothed mask is strongest there
    order = sorted(range(len(pal)), key=lambda i: -sum(1 for row in labels for v in row if v == i))
    region = Image.new('L', (W, H), 255)              # 255 = none yet
    best = Image.new('L', (W, H), 0)
    for i in order:
        stronger = ImageChops.subtract(masks[i], best).point(lambda v: 255 if v > 0 else 0)
        best = ImageChops.lighter(best, masks[i])
        region.paste(i, (0, 0), stronger)
    region = Image.composite(region, Image.new('L', (W, H), 255), sil_hi)

    hi = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    for i, c in enumerate(pal):
        m = region.point(lambda v, i=i: 255 if v == i else 0)
        hi.paste(c + (255,), (0, 0), m)

    for i, c in enumerate(pal):
        hi.paste(c + (255,), (0, 0), ImageChops.multiply(thin[i], sil_hi))
        region.paste(i, (0, 0), ImageChops.multiply(thin[i], sil_hi))

    ring_inner = sil_hi.filter(ImageFilter.MinFilter(int(2 * OUTLINE_PX * k) | 1))
    if shade:
        hi = shading(hi, region, ring_inner, pal, thick_dark.resize((W, H), Image.NEAREST).filter(blur), seams, k)

    # exact small details on top (as soft round dots)
    d = ImageDraw.Draw(hi)
    for lab, comp in small:
        for x, y in comp:
            d.ellipse((x * KK + KK * 0.1, y * KK + KK * 0.1, x * KK + KK * 0.9, y * KK + KK * 0.9), fill=pal[lab] + (255,))

    # even outline around the silhouette
    ring = ImageChops.subtract(sil_hi, ring_inner)
    hi.paste(OUTLINE + (255,), (0, 0), ring)
    hi.putalpha(sil_hi)

    out = hi.resize((ow, oh), Image.LANCZOS)
    a = out.getchannel('A').point(lambda v: 0 if v < 16 else v)
    out.putalpha(a)
    return out


def shading(hi, region, sil, pal, gloss_area, seams=True, K=K):
    W, H = hi.size
    e = int(K * 2.5)                               # bevel width in final px (sil here is the area inside the outline)
    up_left = ImageChops.offset(sil, e, e)         # pixels whose up-left neighbour is outside -> lit edge
    down_right = ImageChops.offset(sil, -e, -e)
    lit = ImageChops.subtract(sil, up_left).filter(ImageFilter.GaussianBlur(K))
    dark = ImageChops.subtract(sil, down_right).filter(ImageFilter.GaussianBlur(K * 1.5))
    # seams between colour regions: edges of the region map
    edges = region.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 0 else 0)
    edges = ImageChops.multiply(edges, sil.filter(ImageFilter.MinFilter(K * 3 + 1))).filter(ImageFilter.MaxFilter(3))

    px = hi.load()
    lp, dp, ep, rp = lit.load(), dark.load(), edges.load(), region.load()
    for y in range(H):
        fall = 1.06 - 0.16 * (y / H)                # light from above
        for x in range(W):
            p = px[x, y]
            if p[3] == 0: continue
            c = p[:3]
            base = pal[rp[x, y]] if rp[x, y] < len(pal) else c
            if lum(base) < DARK:
                f = 1.0                              # dark parts handled by gloss below
            else:
                f = fall - 0.22 * dp[x, y] / 255
            c = scale(c, f)
            if lp[x, y] and lum(base) >= DARK:
                c = mix(c, (255, 255, 255), 0.35 * lp[x, y] / 255)
            if seams and ep[x, y]:
                c = scale(c, 0.72)
            px[x, y] = c + (255,)

    # gloss: a soft diagonal sheen across dark regions (visors, grilles)
    for i, base in enumerate(pal):
        if lum(base) >= DARK: continue
        m = ImageChops.multiply(region.point(lambda v, i=i: 255 if v == i else 0), gloss_area.point(lambda v: 255 if v >= 128 else 0))
        bb = m.getbbox()
        if not bb or (bb[2] - bb[0]) < K * 4: continue
        sheen = Image.new('L', (W, H), 0)
        sd = ImageDraw.Draw(sheen)
        x0, y0, x1, y1 = bb
        bw = (x1 - x0)
        sd.polygon([(x0 + bw * 0.15, y1), (x0 + bw * 0.35, y1), (x0 + bw * 0.75, y0), (x0 + bw * 0.55, y0)], fill=70)
        sheen = ImageChops.multiply(sheen.filter(ImageFilter.GaussianBlur(K * 1.2)), m)
        hi.paste((200, 220, 230, 255), (0, 0), sheen)
    return hi


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('filter', nargs='*')
    ap.add_argument('--no-shade', action='store_true')
    args = ap.parse_args()
    n = 0
    for dp, _, fs in os.walk(DRAFTS):
        for f in fs:
            if not f.lower().endswith('.png'): continue
            src = os.path.join(dp, f)
            rel = os.path.relpath(src, DRAFTS)
            if args.filter and not any(s in rel for s in args.filter): continue
            dst = os.path.join(OUT, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            polish(src, shade=not args.no_shade).save(dst, optimize=True)
            print('polished', rel.replace(os.sep, '/'))
            n += 1
    print(f'{n} textures')


if __name__ == '__main__':
    main()
