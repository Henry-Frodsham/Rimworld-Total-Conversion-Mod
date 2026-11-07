"""
Rudimentary textures for the cyberware animations (Visuals/CyberAnimations.cs). White / greyscale so the game tints
them per variant (steel, electrifying blue, thermal orange, toxic green). Every texture points "up" (towards the top
of the image): the animation code rotates that to face the target. Replace any PNG with real art, same name.

    python -I Source/Tools/gen_anim_textures.py        (run from the RimPunkCE folder)

Output: Common/Textures/RimPunk/Anim/*.png (overwritten every run).
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, 'Common', 'Textures', 'RimPunk', 'Anim')


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, name + '.png'))
    print(name)


def blade():
    """Mantis blade: a long curved blade, sharp tip at the top, thicker spine on the left."""
    w, h = 64, 256
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    spine = [(18, 250), (16, 180), (20, 110), (30, 50), (44, 4)]
    edge = [(44, 4), (40, 60), (38, 120), (40, 190), (36, 250)]
    d.polygon(spine + edge, fill=(235, 238, 245, 255))
    # spine shading + edge highlight
    d.line(spine, fill=(150, 155, 165, 255), width=4)
    d.line(edge, fill=(255, 255, 255, 255), width=2)
    # base mount
    d.rectangle([12, 236, 42, 255], fill=(110, 115, 125, 255))
    save(img, 'Blade')


def slash_arc():
    """A crescent swipe across the top half, fading towards both ends."""
    s = 256
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    px = img.load()
    cx, cy, r_out, r_in = s / 2, s * 0.62, s * 0.46, s * 0.36
    for y in range(s):
        for x in range(s):
            dx, dy = x - cx, cy - y
            r = math.hypot(dx, dy)
            if r < r_in or r > r_out or dy <= 0: continue
            ang = math.degrees(math.atan2(dy, dx))  # 0..180 over the top
            along = math.sin(math.radians(ang)) ** 1.5
            thick = 1 - abs((r - (r_in + r_out) / 2) / ((r_out - r_in) / 2))
            a = int(255 * along * max(0.0, thick) ** 0.6)
            px[x, y] = (255, 255, 255, a)
    save(img.filter(ImageFilter.GaussianBlur(1.2)), 'SlashArc')


def ring():
    """Shockwave ring (gorilla arm impact)."""
    s = 128
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    px = img.load()
    c = s / 2
    for y in range(s):
        for x in range(s):
            r = math.hypot(x - c, y - c) / c
            a = max(0.0, 1 - abs(r - 0.8) / 0.14)
            px[x, y] = (255, 255, 255, int(255 * a ** 1.5))
    save(img, 'Ring')


def glow():
    """Soft round glow (impact flash, fist)."""
    s = 64
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    px = img.load()
    c = s / 2
    for y in range(s):
        for x in range(s):
            r = math.hypot(x - c, y - c) / c
            px[x, y] = (255, 255, 255, int(255 * max(0.0, 1 - r) ** 2))
    save(img, 'Glow')


def barrel():
    """Arm launcher barrel: a short tube with ribs, muzzle at the top."""
    w, h = 48, 128
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([8, 10, 40, 124], radius=6, fill=(200, 205, 212, 255), outline=(90, 95, 105, 255), width=3)
    for y in (40, 62, 84):
        d.rectangle([6, y, 42, y + 6], fill=(120, 125, 135, 255))
    d.rectangle([12, 2, 36, 14], fill=(70, 72, 80, 255))  # muzzle
    d.rectangle([18, 4, 30, 12], fill=(20, 20, 24, 255))
    save(img, 'Barrel')


def flash():
    """Muzzle flash: a four-point burst, long towards the top."""
    s = 128
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    px = img.load()
    c = s / 2
    for y in range(s):
        for x in range(s):
            dx, dy = (x - c) / c, (c - y) / c
            r = math.hypot(dx, dy)
            if r > 1: continue
            ang = math.atan2(dy, dx)
            spikes = abs(math.cos(2 * ang)) ** 6
            forward = 1.6 if dy > 0 and abs(dx) < 0.25 else 1.0
            a = max(0.0, 1 - r / (0.35 + 0.65 * spikes * forward / 1.6)) if r < 1 else 0
            core = max(0.0, 1 - r / 0.3)
            px[x, y] = (255, 255, 255, int(255 * min(1.0, a + core)))
    save(img.filter(ImageFilter.GaussianBlur(1.0)), 'Flash')


if __name__ == '__main__':
    blade()
    slash_arc()
    ring()
    glow()
    barrel()
    flash()
