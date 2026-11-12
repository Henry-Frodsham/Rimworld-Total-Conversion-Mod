"""
Placeholder item textures for cycle 4: the neural shard chip (tinted per shard in its def), the soulkiller chip, the engram,
and the health items (bounce back injectors, maxdoc inhalers, rarity-coloured Mk.1..Mk.5). Replace any PNG with real art, same name.

    python -I Source/Tools/gen_cycle4_textures.py        (run from the RimPunkCE folder)

Output: Common/Textures/Things/RimPunk/Shards/*.png and .../Health/*.png (overwritten every run).
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TEX = os.path.join(ROOT, 'Common', 'Textures', 'Things', 'RimPunk')
SCALE = 4
SIZE = 64 * SCALE
OUTLINE = (20, 20, 26, 255)
RARITY = [(185, 185, 185), (80, 210, 90), (70, 150, 250), (180, 100, 250), (255, 190, 50)]


def canvas():
    return Image.new('RGBA', (SIZE, SIZE)), None


def save(img, folder, name):
    os.makedirs(os.path.join(TEX, folder), exist_ok=True)
    img.resize((64, 64), Image.LANCZOS).save(os.path.join(TEX, folder, name + '.png'))
    print(folder, name)


def chip(body=(235, 235, 240, 255), mark=None):
    """A data shard: a chamfered chip with contact pins. White-ish so the def's colour tints it."""
    img = Image.new('RGBA', (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    w = SCALE * 2
    for i in range(4):  # contact pins
        x = SIZE * (0.34 + i * 0.1)
        d.rectangle([x, SIZE * 0.14, x + SIZE * 0.06, SIZE * 0.3], fill=(215, 175, 70, 255), outline=OUTLINE, width=SCALE)
    d.polygon([(SIZE * 0.22, SIZE * 0.3), (SIZE * 0.78, SIZE * 0.3), (SIZE * 0.78, SIZE * 0.84), (SIZE * 0.3, SIZE * 0.84), (SIZE * 0.22, SIZE * 0.76)],
              fill=body, outline=OUTLINE, width=w)
    d.rectangle([SIZE * 0.32, SIZE * 0.42, SIZE * 0.68, SIZE * 0.62], fill=(60, 70, 85, 255), outline=OUTLINE, width=SCALE)
    if mark == 'x':
        d.line([SIZE * 0.36, SIZE * 0.45, SIZE * 0.64, SIZE * 0.59], fill=(255, 255, 255, 255), width=SCALE * 2)
        d.line([SIZE * 0.36, SIZE * 0.59, SIZE * 0.64, SIZE * 0.45], fill=(255, 255, 255, 255), width=SCALE * 2)
    return img.rotate(-12, resample=Image.BICUBIC)


def engram():
    img = Image.new('RGBA', (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([SIZE * 0.16, SIZE * 0.2, SIZE * 0.84, SIZE * 0.8], SIZE * 0.06, fill=(24, 28, 40, 255), outline=(90, 220, 235, 255), width=SCALE * 2)
    d.ellipse([SIZE * 0.4, SIZE * 0.3, SIZE * 0.6, SIZE * 0.5], fill=(90, 220, 235, 255))               # head
    d.pieslice([SIZE * 0.3, SIZE * 0.5, SIZE * 0.7, SIZE * 0.95], 180, 360, fill=(90, 220, 235, 255))   # shoulders
    for y in (0.27, 0.73):  # data lines
        d.line([SIZE * 0.2, SIZE * y, SIZE * 0.34, SIZE * y], fill=(240, 90, 130, 255), width=SCALE)
    return img


def injector(colour):
    img = Image.new('RGBA', (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    w = SCALE * 2
    d.line([SIZE * 0.84, SIZE * 0.5, SIZE * 0.97, SIZE * 0.5], fill=(190, 195, 205, 255), width=SCALE * 2)           # needle
    d.rectangle([SIZE * 0.26, SIZE * 0.38, SIZE * 0.84, SIZE * 0.62], fill=(225, 230, 235, 255), outline=OUTLINE, width=w)   # barrel
    d.rectangle([SIZE * 0.42, SIZE * 0.41, SIZE * 0.82, SIZE * 0.59], fill=colour + (255,))                       # dose
    d.rectangle([SIZE * 0.1, SIZE * 0.46, SIZE * 0.26, SIZE * 0.54], fill=(225, 230, 235, 255), outline=OUTLINE, width=w)  # plunger
    d.rectangle([SIZE * 0.05, SIZE * 0.36, SIZE * 0.1, SIZE * 0.64], fill=(225, 230, 235, 255), outline=OUTLINE, width=w)
    d.rectangle([SIZE * 0.5, SIZE * 0.36, SIZE * 0.56, SIZE * 0.64], fill=(40, 45, 55, 255))                      # grip
    return img.rotate(35, resample=Image.BICUBIC)


def inhaler(colour):
    img = Image.new('RGBA', (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    w = SCALE * 2
    d.rounded_rectangle([SIZE * 0.34, SIZE * 0.14, SIZE * 0.62, SIZE * 0.74], SIZE * 0.05, fill=(225, 230, 235, 255), outline=OUTLINE, width=w)  # canister
    d.rectangle([SIZE * 0.34, SIZE * 0.34, SIZE * 0.62, SIZE * 0.52], fill=colour + (255,))                                                   # label band
    d.polygon([(SIZE * 0.34, SIZE * 0.74), (SIZE * 0.62, SIZE * 0.74), (SIZE * 0.84, SIZE * 0.9), (SIZE * 0.82, SIZE * 0.97), (SIZE * 0.5, SIZE * 0.88)],
              fill=(60, 66, 78, 255), outline=OUTLINE, width=w)                                                                                # mouthpiece
    return img.rotate(20, resample=Image.BICUBIC)


def main():
    save(chip(), 'Shards', 'Shard')
    save(chip((240, 235, 236, 255), mark='x'), 'Shards', 'SoulkillerShard')
    save(engram(), 'Shards', 'Engram')
    for n, colour in enumerate(RARITY, start=1):
        save(injector(colour), 'Health', f'BounceBack_{n}')
        save(inhaler(colour), 'Health', f'MaxDoc_{n}')


if __name__ == '__main__':
    main()
