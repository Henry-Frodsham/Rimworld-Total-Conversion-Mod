"""
Placeholder item textures for the immunoblockers (Defs/Drugs/Immunoblockers.xml): capsules that shift from cool to hot
with strength, and a syringe for David's last stand. Replace any PNG with real art, same name.

    python -I Source/Tools/gen_drug_textures.py        (run from the RimPunkCE folder)

Output: Common/Textures/Things/RimPunk/Drugs/*.png (overwritten every run).
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, 'Common', 'Textures', 'Things', 'RimPunk', 'Drugs')
SCALE = 4  # drawn large, then scaled down for smooth edges
SIZE = 64 * SCALE
OUTLINE = (20, 20, 26, 255)
SHELL = (225, 230, 235, 255)

CAPSULES = {
    'Basewatch': (70, 200, 170, 255),
    'Bethaloperidol': (70, 150, 230, 255),
    'PrimeTime': (150, 100, 230, 255),
    'Surge': (240, 170, 50, 255),
    'BlackLace': (45, 40, 50, 255),
}


def capsule(colour):
    img = Image.new('RGBA', (SIZE, SIZE))
    half = Image.new('RGBA', (SIZE, SIZE))
    box = [SIZE * 0.14, SIZE * 0.36, SIZE * 0.86, SIZE * 0.64]
    width = SCALE * 2
    radius = (box[3] - box[1]) / 2
    ImageDraw.Draw(img).rounded_rectangle(box, radius, fill=SHELL, outline=OUTLINE, width=width)
    # colour the left half of the same shape
    ImageDraw.Draw(half).rounded_rectangle(box, radius, fill=colour, outline=OUTLINE, width=width)
    img.paste(half.crop((0, 0, SIZE // 2, SIZE)), (0, 0))
    ImageDraw.Draw(img).line([SIZE // 2, box[1], SIZE // 2, box[3]], fill=OUTLINE, width=width)
    return img.rotate(35, resample=Image.BICUBIC)


def syringe():
    img = Image.new('RGBA', (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    width = SCALE * 2
    d.line([SIZE * 0.84, SIZE * 0.5, SIZE * 0.97, SIZE * 0.5], fill=(190, 195, 205, 255), width=SCALE * 2)  # needle
    d.rectangle([SIZE * 0.26, SIZE * 0.38, SIZE * 0.84, SIZE * 0.62], fill=SHELL, outline=OUTLINE, width=width)  # barrel
    d.rectangle([SIZE * 0.42, SIZE * 0.41, SIZE * 0.82, SIZE * 0.59], fill=(235, 35, 70, 255))  # dose
    d.rectangle([SIZE * 0.1, SIZE * 0.46, SIZE * 0.26, SIZE * 0.54], fill=SHELL, outline=OUTLINE, width=width)  # plunger
    d.rectangle([SIZE * 0.05, SIZE * 0.36, SIZE * 0.1, SIZE * 0.64], fill=SHELL, outline=OUTLINE, width=width)
    return img.rotate(35, resample=Image.BICUBIC)


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    img.resize((64, 64), Image.LANCZOS).save(os.path.join(OUT, name + '.png'))
    print(name)


for drug, c in CAPSULES.items():
    save(capsule(c), drug)
save(syringe(), 'DavidsLastStand')
