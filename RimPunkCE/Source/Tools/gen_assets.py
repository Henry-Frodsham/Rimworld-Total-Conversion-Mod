"""Art to-do list. Reads every RimPunk def and writes:
  Source/Tools/data/assets.csv   one row per asset: where to save it, what it currently falls back to, an image prompt
  Source/Tools/data/assets.html  contact sheet: done vs missing, category fallbacks first (they cover the most defs)

Lookup rules mirror Source/RimPunkCE/Visuals/TextureOverrides.cs: save a PNG at the listed path and restart the game,
no XML edits. Paths are under Common/Textures/.
    python Source/Tools/gen_assets.py
"""
import csv
import html
import os
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
DEFS = os.path.join(ROOT, 'Common', 'Defs')
TEX = os.path.join(ROOT, 'Common', 'Textures')
DATA = os.path.join(os.path.dirname(__file__), 'data')

STYLE = ('cyberpunk, Night City 2077 aesthetic, clean readable silhouette, painted game item art in the RimWorld style, '
         'flat soft shading, slight neon rim light, centered, transparent background, no text, no frame')
SIZES = {'cyberware': '128x128', 'weapon': '128x128', 'ability': '128x128', 'faction': '128x128 white on transparent',
         'settlement': '64x64 white on transparent'}


def defs():
    for dp, _, fs in os.walk(DEFS):
        for fn in fs:
            if fn.endswith('.xml'):
                for d in ET.parse(os.path.join(dp, fn)).getroot():
                    if isinstance(d.tag, str) and d.get('Abstract') != 'True' and d.findtext('defName'):
                        yield d


def tex_exists(path):
    return path is not None and os.path.exists(os.path.join(TEX, *path.split('/')) + '.png')


def tags(d, prefix):
    for li in d.findall('weaponTags/li'):
        if li.text and li.text.startswith(prefix):
            return li.text[len(prefix):]
    return None


def short(text, n=160):
    text = ' '.join((text or '').split())
    return text if len(text) <= n else text[:n].rsplit(' ', 1)[0] + '...'


all_defs = list(defs())
things = {d.findtext('defName'): d for d in all_defs if d.tag == 'ThingDef'}
rows = []


def add(kind, d, own, fallbacks, prompt):
    name = d.findtext('defName')
    chain = [own] + [f for f in fallbacks if f]
    hit = next((p for p in chain if tex_exists(p)), None)
    status = 'done' if hit == own else (f'fallback {hit.split("/")[-1]}' if hit else 'placeholder')
    rows.append(dict(kind=kind, defName=name, label=d.findtext('label') or name, status=status,
                     save_as=f'Common/Textures/{own}.png', fallbacks=' > '.join(f'{f}.png' for f in fallbacks if f),
                     size=SIZES[kind], prompt=prompt, shown=hit))


# cyberware items, via the hediff's CyberwareExtension
for h in all_defs:
    if h.tag != 'HediffDef':
        continue
    ext = h.find("modExtensions/li[@Class='RimPunkCE.CyberwareExtension']")
    if ext is None or not ext.findtext('item') or ext.findtext('item') not in things:
        continue
    item = things[ext.findtext('item')]
    slot = (ext.findtext('slot') or '').replace('RPC_Slot_', '')
    add('cyberware', item, f'RimPunk/Cyberware/{item.findtext("defName")}', [f'RimPunk/Cyberware/_{slot}'],
        f'{item.findtext("label")}, a {slot.lower()} cyberware implant, loose item lying flat, {short(item.findtext("description"))} {STYLE}')

for name, d in things.items():
    if name.startswith('RPC_Gun_'):
        manu, cls = tags(d, 'RPC_Manu_'), tags(d, 'RPC_Class_')
        add('weapon', d, f'RimPunk/Weapons/{name}',
            [f'RimPunk/Weapons/_{manu}_{cls}' if manu and cls else None, f'RimPunk/Weapons/_{cls}' if cls else None],
            f'{d.findtext("label")}, {manu} {(cls or "").lower()}, side view, muzzle pointing right, {short(d.findtext("description"))} {STYLE}')
    elif name.startswith('RPC_Melee_'):
        cls = tags(d, 'RPC_MeleeClass_')
        add('weapon', d, f'RimPunk/Weapons/{name}', [f'RimPunk/Weapons/_Melee_{cls}' if cls else None],
            f'{d.findtext("label")}, melee weapon, side view, tip pointing right, {short(d.findtext("description"))} {STYLE}')

for d in all_defs:
    name = d.findtext('defName')
    if d.tag == 'AbilityDef' and name.startswith('RPC_'):
        add('ability', d, f'RimPunk/Abilities/{name}', [],
            f'square ability icon for "{d.findtext("label")}", single bold symbol, {short(d.findtext("description"), 100)} '
            'cyberpunk neon on dark background, no text')
    elif d.tag == 'FactionDef' and d.find("modExtensions/li[@Class='RimPunkCE.CyberFactionExtension']") is not None:
        add('faction', d, f'RimPunk/Factions/{name}', [],
            f'faction emblem for {d.findtext("label")}, single flat white silhouette logo, transparent background, no text')
        add('settlement', d, f'RimPunk/Factions/{name}_Settlement', [],
            f'world map marker for a {d.findtext("label")} settlement, tiny flat white silhouette, transparent background')

# category fallbacks: one picture covers every def that lists it (single-def combos are left out: just draw that def)
cover = defaultdict(list)
for r in rows:
    if r['fallbacks'] and r['status'] != 'done':
        for f in r['fallbacks'].split(' > '):
            cover[f].append(r['defName'])
cover = {f: n for f, n in cover.items() if len(n) > 1}
CATEGORY = {'OperatingSystem': 'operating system chip, a sleek spinal-mounted cyberware module', 'PrefrontalCortex': 'neural chip, a small brain implant board',
            'Arm': 'cybernetic arm', 'Face': 'cybernetic eye, optic implant', 'Hands': 'cybernetic hand', 'Legs': 'cybernetic leg',
            'Skeleton': 'reinforced cybernetic bone and joint implant', 'NervousSystem': 'nerve implant, glowing fibre bundle',
            'CirculatorySystem': 'cybernetic heart and blood pump implant', 'IntegumentarySystem': 'subdermal armour plate, synthetic skin weave'}


def category_prompt(f):
    key = f.split('/')[-1][1:-4]
    if '/Cyberware/' in f:
        return f'{CATEGORY.get(key, key)}, generic cyberware item lying flat, {STYLE}'
    if key.startswith('Melee_'):
        return f'generic cyberpunk {key[6:].lower()}, melee weapon, side view, tip pointing right, {STYLE}'
    manu, _, cls = key.rpartition('_')
    return f'generic {manu + " " if manu else ""}{cls.lower()}, firearm, side view, muzzle pointing right, {STYLE}'

os.makedirs(DATA, exist_ok=True)
fields = ['kind', 'defName', 'label', 'status', 'save_as', 'fallbacks', 'size', 'prompt']
with open(os.path.join(DATA, 'assets.csv'), 'w', encoding='utf-8', newline='') as fh:
    w = csv.DictWriter(fh, fields, extrasaction='ignore')
    w.writeheader()
    for f, names in sorted(cover.items(), key=lambda kv: -len(kv[1])):
        kind = 'cyberware' if '/Cyberware/' in f else 'weapon'
        w.writerow(dict(kind=kind, defName='(category)', label=f'covers {len(names)}',
                        status='done' if tex_exists(f[:-4]) else 'missing', save_as=f'Common/Textures/{f}', size=SIZES[kind],
                        prompt=category_prompt(f)))
    w.writerows(sorted(rows, key=lambda r: (r['kind'], r['defName'])))


def img(path):
    if not path:
        return '<div class="ph">?</div>'
    rel = os.path.relpath(os.path.join(TEX, *path.split('/')) + '.png', DATA).replace(os.sep, '/')
    return f'<img src="{html.escape(rel)}">'


kinds = defaultdict(list)
for r in rows:
    kinds[r['kind']].append(r)
done = sum(r['status'] == 'done' for r in rows)
out = [f'''<!DOCTYPE html><html><head><meta charset="utf-8"><title>RimPunk art</title><style>
body{{background:#111;color:#ddd;font:13px sans-serif;margin:20px}} h2{{color:#5de0e6;border-bottom:1px solid #333}}
.grid{{display:flex;flex-wrap:wrap;gap:8px}} .c{{width:150px;background:#1b1b1b;padding:6px;border-radius:4px;border-left:3px solid #a33}}
.c.done{{border-color:#3a3}} .c.fallback{{border-color:#ca3}} img,.ph{{width:64px;height:64px;display:block;margin:auto;image-rendering:auto}}
.ph{{background:#222;color:#555;text-align:center;line-height:64px;font-size:28px}} .n{{font-size:11px;word-break:break-all;margin-top:4px}}
.s{{color:#888;font-size:11px}} code{{color:#9cf}}</style></head><body>
<h1>RimPunk art: {done}/{len(rows)} done</h1>
<p>Save PNGs at the <code>save_as</code> path from <code>assets.csv</code> (prompts are in there too), restart RimWorld, re-run this script.
Green = own art, amber = using a category fallback, red = placeholder.</p>
<h2>Category fallbacks (do these first)</h2><div class="grid">''']
for f, names in sorted(cover.items(), key=lambda kv: -len(kv[1])):
    st = 'done' if tex_exists(f[:-4]) else ''
    out.append(f'<div class="c {st}">{img(f[:-4] if st else None)}<div class="n">{html.escape(f)}</div><div class="s">covers {len(names)}</div></div>')
out.append('</div>')
for kind, rs in kinds.items():
    out.append(f'<h2>{kind} ({sum(r["status"] == "done" for r in rs)}/{len(rs)})</h2><div class="grid">')
    for r in sorted(rs, key=lambda r: r['defName']):
        cls = 'done' if r['status'] == 'done' else ('fallback' if r['shown'] else '')
        out.append(f'<div class="c {cls}" title="{html.escape(r["prompt"])}">{img(r["shown"])}<div class="n">{html.escape(r["label"])}</div>'
                   f'<div class="s">{html.escape(r["save_as"].replace("Common/Textures/", ""))}</div></div>')
    out.append('</div>')
out.append('</body></html>')
open(os.path.join(DATA, 'assets.html'), 'w', encoding='utf-8').write('\n'.join(out))

by = defaultdict(lambda: [0, 0])
for r in rows:
    by[r['kind']][0] += r['status'] == 'done'
    by[r['kind']][1] += 1
print(f'{done}/{len(rows)} assets done; ' + ', '.join(f'{k} {a}/{b}' for k, (a, b) in by.items()) +
      f'; {len(cover)} category fallbacks -> {os.path.relpath(DATA, ROOT)}/assets.csv, assets.html')
