"""
Weapon balance benchmark (improvement cycle 1, phase 3). Read-only: changes no game data.

Compares the generated guns and melee (Common/Defs/Weapons, Common/Patches/Generated_Guns_CE.xml) against
vanilla + Combat Extended reference weapons (ModdingAPI/defs) and writes a nerf/buff proposal to review:
    prompts/weapon-balance-cycle1.md

Usage: python balance_weapons.py [output.md]

A proposal is a damage multiplier for that weapon's data/weapons.json row. Applying one means editing the json and
rerunning gen_weapons.py; this script never does either.

Model (ignores accuracy, recoil and quality, which CE handles at runtime):
  burst dps     = damage * pellets * burst / (warmup + cooldown + (burst - 1) * ticksBetween / 60)
  sustained dps = burst dps, with the reload spread over the magazine
  armored dps   = sustained dps against 4 mm sharp armor, using CE's own formula:
                  damage * clamp((pen - armor) / pen); a deflected hit counts as 0 here
Guns are judged on armored and sustained dps against the class reference, melee on dps.
"""
import glob
import os
import re
import statistics
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
REPO = os.path.dirname(ROOT)
API = os.path.join(REPO, 'ModdingAPI', 'defs')
CE = os.path.join(API, 'CombatExtended')
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, 'prompts', 'weapon-balance-cycle1.md')

ARMOR_MM = 4.0

# rough silver value of materials, for the build-cost column
MATERIAL_VALUE = {'Steel': 1.9, 'WoodLog': 1.2, 'Plasteel': 9, 'Uranium': 6, 'Gold': 10, 'Silver': 1, 'Chemfuel': 2.3,
                  'ComponentIndustrial': 32, 'ComponentSpacer': 200}

# class -> reference weapons (vanilla defName, CE stats)
GUN_REFERENCE = {
    'Pistol': ['Gun_Autopistol'],
    'Revolver': ['Gun_Revolver'],
    'SMG': ['Gun_HeavySMG', 'Gun_MachinePistol'],
    'Rifle': ['Gun_AssaultRifle'],
    'Precision': ['Gun_BoltActionRifle'],
    'Sniper': ['Gun_SniperRifle'],
    'Shotgun': ['Gun_PumpShotgun', 'Gun_ChainShotgun'],
    'MG': ['Gun_LMG'],
}
SPACER_REFERENCE = 'Gun_ChargeRifle'
MELEE_REFERENCE = {'Knife': 'MeleeWeapon_Knife', 'Machete': 'MeleeWeapon_Gladius', 'Katana': 'MeleeWeapon_LongSword',
                   'Chainsword': 'MeleeWeapon_LongSword', 'Axe': 'MeleeWeapon_Gladius', 'Club': 'MeleeWeapon_Club',
                   'HeavyClub': 'MeleeWeapon_Club', 'Hammer': 'MeleeWeapon_Mace'}

# a gun may run this far from its class reference before it gets flagged
GUN_HIGH, GUN_LOW = 1.6, 0.7
MELEE_HIGH, MELEE_LOW = 1.8, 0.6


def parse(path):
    try:
        return ET.parse(path).getroot()
    except ET.ParseError:
        return None


def num(node, tag, default=0.0):
    el = node.find(tag) if node is not None else None
    try:
        return float(el.text) if el is not None and el.text else default
    except ValueError:
        return default


def projectiles(paths):
    out = {}
    for path in paths:
        root = parse(path)
        if root is None:
            continue
        for t in root.iter('ThingDef'):
            name, pr = t.findtext('defName'), t.find('projectile')
            if name and pr is not None and pr.find('damageAmountBase') is not None:
                out[name] = dict(damage=num(pr, 'damageAmountBase'), pen=num(pr, 'armorPenetrationSharp'), pellets=max(1, num(pr, 'pelletCount', 1)))
    return out


def ce_patches(path):
    out = {}
    root = parse(path)
    if root is None:
        return out
    for op in root.iter('Operation'):
        name = op.findtext('defName')
        if not name or op.find('Properties') is None:
            continue
        st, pr, am = op.find('statBases'), op.find('Properties'), op.find('AmmoUser')
        out[name] = dict(cooldown=num(st, 'RangedWeapon_Cooldown'), mass=num(st, 'Mass'), warmup=num(pr, 'warmupTime'), range=num(pr, 'range'),
                         burst=max(1, num(pr, 'burstShotCount', 1)), tbs=num(pr, 'ticksBetweenBurstShots'), proj=pr.findtext('defaultProjectile'),
                         mag=num(am, 'magazineSize'), reload=num(am, 'reloadTime'))
    return out


def dps(g, proj):
    b = g['burst']
    cycle = g['warmup'] + g['cooldown'] + (b - 1) * g['tbs'] / 60.0
    shot = proj['damage'] * proj['pellets']
    burst_dps = shot * b / max(cycle, 0.05)
    sustained = burst_dps
    if g['mag'] > 0:
        cycles = max(1.0, g['mag'] / b)
        sustained = shot * g['mag'] / (cycles * cycle + g['reload'])
    pen = proj['pen']
    frac = max(0.0, min(1.0, (pen - ARMOR_MM) / pen)) if pen > 0 else 0.0
    return dict(burst=burst_dps, sustained=sustained, armored=sustained * frac, pen=pen, shot=shot)


def cost_value(thing):
    cl = thing.find('costList')
    if cl is None:
        return 0.0
    return sum(MATERIAL_VALUE.get(c.tag, 2.0) * float(c.text) for c in cl)


def melee_best(thing_tools):
    """Tools are picked by weight in play; the best is close enough for a benchmark. (dps, pen, power)"""
    best = (0.0, 0.0, 0.0)
    for li in thing_tools:
        p, c = num(li, 'power'), num(li, 'cooldownTime', 1.0)
        v = p / max(c, 0.1)
        if v > best[0]:
            best = (v, num(li, 'armorPenetrationSharp'), p)
    return best


def round_multiplier(m):
    return round(m * 20) / 20


# ---------------------------------------------------------------------------------------- load
gun_stats = ce_patches(os.path.join(ROOT, 'Common', 'Patches', 'Generated_Guns_CE.xml'))
gun_projs = projectiles([os.path.join(ROOT, 'Common', 'Defs', 'Weapons', 'Generated_Ammo.xml')])
gun_root = parse(os.path.join(ROOT, 'Common', 'Defs', 'Weapons', 'Generated_Guns.xml'))
melee_root = parse(os.path.join(ROOT, 'Common', 'Defs', 'Weapons', 'Generated_Melee.xml'))

ce_gun_stats = ce_patches(os.path.join(CE, 'Patches', 'Core', 'ThingDefs_Misc', 'Weapons_Guns.xml'))
ce_projs = projectiles(glob.glob(os.path.join(CE, 'Defs', 'Ammo', '**', '*.xml'), recursive=True))

vanilla_things = {}
for path in glob.glob(os.path.join(API, 'Core', 'Defs', 'ThingDefs_Misc', 'Weapons', '*.xml')):
    root = parse(path)
    if root is not None:
        for t in root.iter('ThingDef'):
            if t.findtext('defName'):
                vanilla_things[t.findtext('defName')] = t

# CE melee tools (reference): keep it to the patch file's replacement tools
ce_melee = {}
root = parse(os.path.join(CE, 'Patches', 'Core', 'ThingDefs_Misc', 'Weapons_Melee.xml'))
if root is not None:
    for op in root.iter('Operation'):
        xp = op.findtext('xpath') or ''
        m = re.search(r'defName\s*=\s*"(MeleeWeapon_\w+)"', xp)
        if m and xp.rstrip().endswith('tools'):
            tools = op.find('value/tools')
            if tools is not None:
                ce_melee[m.group(1)] = list(tools)

# ---------------------------------------------------------------------------------------- guns
refs = {}
for cls, names in GUN_REFERENCE.items():
    rows = []
    for n in names:
        g = ce_gun_stats.get(n)
        if g and g['proj'] in ce_projs:
            rows.append(dict(name=n, **dps(g, ce_projs[g['proj']]), market=num(vanilla_things[n].find('statBases'), 'MarketValue') if n in vanilla_things else 0,
                             build=cost_value(vanilla_things[n]) if n in vanilla_things else 0))
    if rows:
        refs[cls] = rows
spacer = None
g = ce_gun_stats.get(SPACER_REFERENCE)
if g and g['proj'] in ce_projs:
    spacer = dps(g, ce_projs[g['proj']])

guns = []
for t in gun_root.iter('ThingDef'):
    name = t.findtext('defName')
    g = gun_stats.get(name)
    if not g or g['proj'] not in gun_projs:
        continue
    tags = [li.text for li in t.find('weaponTags')]
    cls = next((x[len('RPC_Class_'):].replace('_', ' ') for x in tags if x.startswith('RPC_Class_')), '?')
    typ = next((x[len('RPC_Type_'):] for x in tags if x.startswith('RPC_Type_')), 'power')
    projectile = dict(gun_projs[g['proj']])
    projectile['damage'] = round(projectile['damage'] * num(t.find('statBases'), 'RangedWeapon_DamageMultiplier', 1.0))
    d = dps(g, projectile)
    guns.append(dict(name=name, label=t.findtext('label'), cls=cls, type=typ, market=num(t.find('statBases'), 'MarketValue'), build=cost_value(t),
                     range=g['range'], burst_n=g['burst'], mag=g['mag'], **d))

for cls in sorted({x['cls'] for x in guns}):
    if cls not in refs:
        print('no CE reference for class', cls)

# class medians (our own weapons): outliers inside a class stand out even where the reference is a poor fit
medians = {}
for cls in {x['cls'] for x in guns}:
    rows = [x for x in guns if x['cls'] == cls]
    medians[cls] = dict(sustained=statistics.median(x['sustained'] for x in rows), armored=statistics.median(x['armored'] for x in rows),
                        market=statistics.median(x['market'] for x in rows))


def gun_verdict(x):
    r = refs.get(x['cls'])
    if not r:
        return 1.0, 1.0, ''
    ref = max(r, key=lambda q: q['sustained'])
    ratio = x['sustained'] / max(ref['sustained'], 0.1)
    armored = x['armored'] / max(ref['armored'], 0.1) if ref['armored'] > 0 else 1.0
    mult, why = 1.0, ''
    if ratio > GUN_HIGH:
        mult, why = GUN_HIGH / ratio, f"{ratio:.2f}x {ref['name']} dps"
    elif ratio < GUN_LOW:
        mult, why = GUN_LOW / ratio, f"{ratio:.2f}x {ref['name']} dps"
    # only a note when it pens clearly worse than its class reference: most low-tier guns bounce off armor by design
    if x['pen'] < ref['pen'] * 0.6:
        why = (why + '; ' if why else '') + f"{x['pen']:.1f} mm pen vs {ref['pen']:.1f} on {ref['name']}"
    return ratio, mult, why


# ---------------------------------------------------------------------------------------- melee
melees = []
for t in melee_root.iter('ThingDef'):
    tools = t.find('tools')
    if tools is None:
        continue
    tags = [li.text for li in t.find('weaponTags')]
    fam = next((x[len('RPC_MeleeClass_'):] for x in tags if x.startswith('RPC_MeleeClass_')), '?')
    v, pen, power = melee_best(list(tools))
    melees.append(dict(name=t.findtext('defName'), label=t.findtext('label'), fam=fam, dps=v, pen=pen, power=power, build=cost_value(t)))

melee_refs = {fam: melee_best(ce_melee[n]) + (n,) for fam, n in MELEE_REFERENCE.items() if n in ce_melee}


def melee_verdict(x):
    r = melee_refs.get(x['fam'])
    if not r:
        return 1.0, 1.0, ''
    ratio = x['dps'] / max(r[0], 0.1)
    if ratio > MELEE_HIGH:
        return ratio, MELEE_HIGH / ratio, f"{ratio:.2f}x {r[3]} dps"
    if ratio < MELEE_LOW:
        return ratio, MELEE_LOW / ratio, f"{ratio:.2f}x {r[3]} dps"
    return ratio, 1.0, ''


# ---------------------------------------------------------------------------------------- report
L = ['# weapon balance, cycle 1', '',
     'generated by Source/Tools/balance_weapons.py. nothing here has been applied. review, then edit data/weapons.json and rerun gen_weapons.py.', '',
     f'dps ignores accuracy, recoil and quality. armored = sustained dps against {ARMOR_MM:.0f} mm sharp armor, deflected hits count 0.', '']

L += ['## reference: vanilla + ce', '', '| class | weapon | shot | pen mm | burst dps | sustained | armored | build value |', '|---|---|---:|---:|---:|---:|---:|---:|']
for cls, rows in refs.items():
    for r in rows:
        L.append(f"| {cls} | {r['name']} | {r['shot']:.0f} | {r['pen']:.1f} | {r['burst']:.1f} | {r['sustained']:.1f} | {r['armored']:.1f} | {r['build']:.0f} |")
if spacer:
    L.append(f"| spacer | {SPACER_REFERENCE} | {spacer['shot']:.0f} | {spacer['pen']:.1f} | {spacer['burst']:.1f} | {spacer['sustained']:.1f} | {spacer['armored']:.1f} | |")
L.append('')
if melee_refs:
    L += ['| melee family | reference | best tool dps | pen mm |', '|---|---|---:|---:|']
    for fam, r in melee_refs.items():
        L.append(f'| {fam} | {r[3]} | {r[0]:.1f} | {r[1]:.1f} |')
    L.append('')

flagged = [(x, *gun_verdict(x)) for x in guns]
changes = [f for f in flagged if abs(f[2] - 1.0) > 0.02]
L += ['## proposed gun changes', '']
if changes:
    L += ['| weapon | class | type | sustained | vs ref | proposed damage x | reason |', '|---|---|---|---:|---:|---:|---|']
    for x, ratio, mult, why in sorted(changes, key=lambda f: f[2]):
        L.append(f"| {x['label']} | {x['cls']} | {x['type']} | {x['sustained']:.1f} | {ratio:.2f} | x{round_multiplier(mult):.2f} | {why} |")
else:
    L.append('none.')
L.append('')

notes = [f for f in flagged if abs(f[2] - 1.0) <= 0.02 and f[3]]
if notes:
    L += ['## guns with no damage change, worth a look', '', '| weapon | class | note |', '|---|---|---|']
    for x, _, _, why in notes:
        L.append(f"| {x['label']} | {x['cls']} | {why} |")
    L.append('')

mchanges = [(x, *melee_verdict(x)) for x in melees]
mflag = [m for m in mchanges if abs(m[2] - 1.0) > 0.02]
L += ['## proposed melee changes', '']
if mflag:
    L += ['| weapon | family | best dps | pen mm | vs ref | proposed damage x | reason |', '|---|---|---:|---:|---:|---:|---|']
    for x, ratio, mult, why in sorted(mflag, key=lambda f: f[2]):
        L.append(f"| {x['label']} | {x['fam']} | {x['dps']:.1f} | {x['pen']:.1f} | {ratio:.2f} | x{round_multiplier(mult):.2f} | {why} |")
else:
    L.append('none.')
L.append('')

L += ['## economy: value for money inside each class', '', 'dps per 100 silver of market value, against the class median. over 1.5x is cheap for what it does, under 0.67x is overpriced.', '',
      '| weapon | class | market | build value | sustained | value vs class |', '|---|---|---:|---:|---:|---:|']
for cls in sorted(medians):
    rows = [x for x in guns if x['cls'] == cls]
    med = statistics.median(x['sustained'] / max(x['market'], 1) for x in rows)
    for x in sorted(rows, key=lambda q: q['sustained'] / max(q['market'], 1)):
        v = (x['sustained'] / max(x['market'], 1)) / max(med, 1e-6)
        if v > 1.5 or v < 0.67:
            L.append(f"| {x['label']} | {cls} | {x['market']:.0f} | {x['build']:.0f} | {x['sustained']:.1f} | {v:.2f} |")
L.append('')

L += ['## full gun table', '', '| weapon | class | type | shot | pen mm | burst n | mag | range | burst dps | sustained | armored | market | build value |',
      '|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
for x in sorted(guns, key=lambda q: (q['cls'], -q['sustained'])):
    L.append(f"| {x['label']} | {x['cls']} | {x['type']} | {x['shot']:.0f} | {x['pen']:.1f} | {x['burst_n']:.0f} | {x['mag']:.0f} | {x['range']:.0f} | {x['burst']:.1f} | {x['sustained']:.1f} | {x['armored']:.1f} | {x['market']:.0f} | {x['build']:.0f} |")
L += ['', '## full melee table', '', '| weapon | family | best dps | power | pen mm | build value |', '|---|---|---:|---:|---:|---:|']
for x in sorted(melees, key=lambda q: (q['fam'], -q['dps'])):
    L.append(f"| {x['label']} | {x['fam']} | {x['dps']:.1f} | {x['power']:.0f} | {x['pen']:.1f} | {x['build']:.0f} |")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(L) + '\n')
print(f'{len(guns)} guns, {len(melees)} melee, {len(refs)} reference classes, {len(melee_refs)} melee references: {len(changes)} gun + {len(mflag)} melee changes proposed -> {OUT}')
