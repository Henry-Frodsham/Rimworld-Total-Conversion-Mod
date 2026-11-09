"""
Generates RimPunk CE weapon content from data/weapons.json (Cyberpunk 2077 stats) and the balance tables below.

    python -I Source/Tools/gen_weapons.py        (run from the RimPunkCE folder)

Outputs (overwritten every run, do not hand-edit):
    Common/Defs/Weapons/Generated_Ammo.xml       calibers: one per manufacturer + weapon family
    Common/Defs/Weapons/Generated_Guns.xml       gun ThingDefs (vanilla-style)
    Common/Patches/Generated_Guns_CE.xml         CombatExtended.PatchOperationMakeGunCECompatible per gun
    Common/Defs/Weapons/Generated_Melee.xml      melee ThingDefs (ToolCE)
    Common/Defs/Weapons/Generated_Research.xml   research: one project per caliber + melee families

Rules (prompts/weapons/overview.md):
  - damage baseline = the matching Combat Extended caliber, scaled by manufacturer (Arasaka ~= Militech, top end above CE)
  - 1 m = 1 tile, max 40 tiles
  - tech: charged (aimed) shots pierce walls (Verb_ShootTech + BulletCE_Tech)
  - smart: Verb_ShootSmart, blue tracers
  - power weapons at the machining table, tech/smart at the fabrication bench
  - research per caliber, prerequisites = existing vanilla weapon research
"""
import json, os, re
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'data', 'weapons.json')
MAX_RANGE = 40
with open(os.path.join(os.path.dirname(__file__), 'data', 'weapon_sounds.json'), encoding='utf-8') as sound_config:
    WEAPON_SOUNDS = json.load(sound_config)['profiles']

# ---------------------------------------------------------------- balance tables

MANUFACTURER = {  # damage / penetration multiplier vs the CE baseline caliber
    'Arasaka': 1.35, 'Militech': 1.35, 'Kang Tao': 1.3, 'Malorian Arms': 1.45, 'Tsunami': 1.2,
    'Nokota': 1.15, 'Midnight Arms': 1.1, 'Darra Polytechnic': 1.1, 'Hansen Armory': 1.05,
    'Constitutional Arms': 1.0, 'Techtronika': 1.0, 'Rostović': 0.95, 'Budget Arms': 0.85,
}
MANUFACTURER_ALIAS = {'Arasaka & Jinguji': 'Arasaka'}
GENERIC = 'Street'  # melee without a manufacturer

# CE baselines per weapon family (from CE's patches of the vanilla guns, see ModdingAPI/defs/CombatExtended)
FAMILY = {
    'Pistol':          dict(fam='Pistol',    ref='autopistol', mass=1.11, cd=0.38, sights=0.7, spread=0.17, sway=1.07, bulk=2.1, warm=0.6, recoil=2.72,
                            dmg=12, penS=3.5, penB=10.86, speed=83, ammoBase='SmallAmmoBase', ammoTex='Pistol', auto=False,
                            tex='Things/Item/Equipment/WeaponRanged/Autopistol', snd='Shot_Autopistol', tail='GunTail_Light', interact='Interact_Autopistol',
                            work=7000, steel=30, comp=3, research='BlowbackOperation', tags=['CE_Sidearm', 'CE_OneHandedWeapon']),
    'Revolver':        dict(fam='Revolver',  ref='revolver', mass=1.39, cd=0.49, sights=0.7, spread=0.16, sway=1.39, bulk=2.79, warm=0.6, recoil=2.96,
                            dmg=15, penS=6, penB=23.28, speed=91, ammoBase='SmallAmmoBase', ammoTex='Pistol', auto=False,
                            tex='Things/Item/Equipment/WeaponRanged/Revolver', snd='Shot_Revolver', tail='GunTail_Light', interact='Interact_Revolver',
                            work=6000, steel=30, comp=2, research='Gunsmithing', tags=['CE_Sidearm', 'CE_OneHandedWeapon']),
    'Submachine Gun':  dict(fam='SMG',       ref='heavy SMG', mass=2.5, cd=0.37, sights=1.0, spread=0.14, sway=0.94, bulk=4.5, warm=0.6, recoil=1.79,
                            dmg=12, penS=3.5, penB=10.86, speed=83, ammoBase='SmallAmmoBase', ammoTex='Pistol', auto=True, burst=6, aimedBurst=3,
                            tex='Things/Item/Equipment/WeaponRanged/HeavySMG', snd='Shot_HeavySMG', tail='GunTail_Heavy', interact='Interact_SMG',
                            work=24500, steel=45, comp=5, research='BlowbackOperation', tags=[]),
    'Assault Rifle':   dict(fam='Rifle',     ref='assault rifle', mass=3.26, cd=0.36, sights=1.0, spread=0.07, sway=1.33, bulk=10.03, warm=1.1, recoil=1.5,
                            dmg=14, penS=6, penB=34.18, speed=165, ammoBase='MediumAmmoBase', ammoTex='Rifle', auto=True, burst=6, aimedBurst=3,
                            tex='Things/Item/Equipment/WeaponRanged/AssaultRifle', snd='Shot_AssaultRifle', tail='GunTail_Medium', interact='Interact_Rifle',
                            work=30000, steel=60, comp=7, research='PrecisionRifling', tags=[]),
    'Precision Rifle': dict(fam='Precision', ref='bolt-action rifle', mass=4.19, cd=0.8, sights=1.4, spread=0.03, sway=1.6, bulk=12.0, warm=1.1, recoil=2.04,
                            dmg=20, penS=7, penB=66.72, speed=165, ammoBase='MediumAmmoBase', ammoTex='Rifle', auto=False,
                            tex='Things/Item/Equipment/WeaponRanged/BoltActionRifle', snd='Shot_BoltActionRifle', tail='GunTail_Heavy', interact='Interact_Rifle',
                            work=24000, steel=60, comp=5, research='PrecisionRifling', tags=[]),
    'Sniper Rifle':    dict(fam='Sniper',    ref='sniper rifle', mass=7.3, cd=1.36, sights=2.6, spread=0.05, sway=1.35, bulk=11.92, warm=1.8, recoil=1.5,
                            dmg=24, penS=9, penB=80, speed=185, ammoBase='MediumAmmoBase', ammoTex='Rifle', auto=False,
                            tex='Things/Item/Equipment/WeaponRanged/SniperRifle', snd='Shot_SniperRifle', tail='GunTail_Heavy', interact='Interact_Rifle',
                            work=30000, steel=70, comp=7, research='PrecisionRifling', tags=[]),
    'Shotgun':         dict(fam='Shotgun',   ref='pump shotgun', mass=3.05, cd=0.99, sights=1.0, spread=0.14, sway=1.31, bulk=10.0, warm=0.6, recoil=2.72,
                            dmg=8, penS=4, penB=4.52, speed=60, pellets=9, ammoBase='MediumAmmoBase', ammoTex='Shotgun', auto=False, shotgun=True,
                            tex='Things/Item/Equipment/WeaponRanged/Shotgun', snd='Shot_Shotgun', tail='GunTail_Heavy', interact='Interact_Shotgun',
                            work=10000, steel=50, comp=4, research='Gunsmithing', tags=[]),
    'Light Machine Gun': dict(fam='MG',      ref='LMG', mass=8.7, cd=0.56, sights=1.0, spread=0.05, sway=1.37, bulk=12.9, warm=1.3, recoil=1.38,
                            dmg=20, penS=6.5, penB=67, speed=165, ammoBase='MediumAmmoBase', ammoTex='Rifle', auto=True, burst=10, aimedBurst=5,
                            tex='Things/Item/Equipment/WeaponRanged/LMG', snd='Shot_Minigun', tail='GunTail_Medium', interact='Interact_Rifle',
                            work=31500, steel=80, comp=8, research='GasOperation', tags=[]),
}
FAMILY['Double-Barrel Shotgun'] = dict(FAMILY['Shotgun'], ref='double-barrel shotgun', cd=0.6, mass=3.4, bulk=9.0, tex='Things/Item/Equipment/WeaponRanged/ChainShotgun')
FAMILY['Heavy Machine Gun'] = dict(FAMILY['Light Machine Gun'], ref='heavy machine gun', mass=14, bulk=16, dmg=26, penS=9, penB=90, sway=2.2, warm=1.6)

TYPE_BENCH = {'power': 'TableMachining', 'tech': 'FabricationBench', 'smart': 'FabricationBench'}
TYPE_RESEARCH = {'tech': ['ChargedShot'], 'smart': ['MicroelectronicsBasics']}
TYPE_EXTRA_COST = {'power': {}, 'tech': {'Plasteel': 15, 'ComponentSpacer': 1}, 'smart': {'Plasteel': 10, 'ComponentSpacer': 1}}

# melee baselines (CE values for vanilla knife / gladius / long sword / club / mace, no stuff scaling)
MELEE = {
    'Knife':           dict(fam='Knife', tex='Things/Item/Equipment/WeaponMelee/Knife', mass=0.5, bulk=1, parry=0.15, research='Smithing',
                            tools=[('blade', 'Cut', 12, 1.15, 0.6, 0.4), ('point', 'Stab', 13, 1.2, 0.8, 0.3), ('handle', 'Poke', 1, 1.25, 0, 0.25)]),
    'Machete':         dict(fam='Machete', tex='Things/Item/Equipment/WeaponMelee/Gladius', mass=1.0, bulk=3, parry=0.3, research='Smithing',
                            tools=[('edge', 'Cut', 24, 1.35, 0.7, 1.0), ('point', 'Stab', 14, 1.45, 0.6, 0.4), ('handle', 'Poke', 2, 1.45, 0, 0.4)]),
    'Katana':          dict(fam='Katana', tex='Things/Item/Equipment/WeaponMelee/LongSword', mass=1.3, bulk=6, parry=0.9, research='LongBlades',
                            # katanas are deliberately strong (weapons/overview.md)
                            tools=[('edge', 'Cut', 46, 1.4, 2.4, 3.6), ('point', 'Stab', 28, 1.5, 3.2, 1.2), ('handle', 'Poke', 4, 1.7, 0, 1.0)]),
    'Chainsword':      dict(fam='Chainsword', tex='Things/Item/Equipment/WeaponMelee/LongSword', mass=4.5, bulk=8, parry=0.5, research='LongBlades',
                            tools=[('chain', 'Cut', 40, 1.9, 1.6, 4.0), ('handle', 'Poke', 4, 1.8, 0, 1.0)]),
    'Axe':             dict(fam='Axe', tex='Things/Item/Equipment/WeaponMelee/Ikwa', mass=1.2, bulk=3, parry=0.2, research='Smithing',
                            tools=[('blade', 'Cut', 20, 1.6, 0.8, 2.0), ('handle', 'Blunt', 4, 1.6, 0, 0.8)]),
    'One-Handed Club': dict(fam='Club', tex='Things/Item/Equipment/WeaponMelee/Club', mass=1.2, bulk=3, parry=0.15, research='Smithing',
                            tools=[('head', 'Blunt', 13, 2.2, 0, 4.5), ('handle', 'Poke', 3, 1.7, 0, 1.0)]),
    'Two-Handed Club': dict(fam='HeavyClub', tex='Things/Item/Equipment/WeaponMelee/Club', mass=2.0, bulk=5, parry=0.2, research='Smithing',
                            tools=[('head', 'Blunt', 20, 2.6, 0, 7.0), ('handle', 'Poke', 4, 1.8, 0, 1.0)]),
    'Hammer':          dict(fam='Hammer', tex='Things/Item/Equipment/WeaponMelee/Mace', mass=4.0, bulk=6, parry=0.2, research='Smithing',
                            tools=[('head', 'Blunt', 26, 3.0, 0, 10.0), ('handle', 'Poke', 4, 1.8, 0, 1.0)]),
}
MELEE['One-handed Club'] = MELEE['One-Handed Club']
MELEE_EXTRA = {  # weapon-specific extra damage (by name)
    'Electric Baton Alpha': ('RPC_Shock', 6, 0.15), 'Electric Baton Beta': ('RPC_Shock', 8, 0.25), 'Electric Baton Gamma': ('RPC_Shock', 10, 0.35),
    'Neurotoxin Knife': ('RPC_Toxic', 5, 0.5),
}

# ---------------------------------------------------------------- helpers

def ident(s):
    s = s.replace('ć', 'c').replace('&', 'and')
    return re.sub(r'[^A-Za-z0-9]', '', s.title() if s.islower() else s)

def manu_of(w):
    m = MANUFACTURER_ALIAS.get(w['manufacturer'], w['manufacturer'])
    return m or GENERIC

def r2(x): return f'{x:.2f}'.rstrip('0').rstrip('.')

def caliber_id(manu, fam): return f'{ident(manu)}_{fam}'

def thing_defname(prefix, name, suffix):
    # RimWorld reserves ThingDef names ending in a digit (generated variants): add a suffix
    dn = prefix + ident(name)
    return dn + '_' + suffix if dn[-1].isdigit() else dn

# base market value per family (silver), scaled by manufacturer; tech/smart +30%
MARKET = {'Pistol': 280, 'Revolver': 300, 'SMG': 450, 'Rifle': 650, 'Precision': 700, 'Sniper': 900, 'Shotgun': 450, 'MG': 1100}

def describe(w, manu, kind):
    t = {'power': 'conventional', 'tech': 'tech', 'smart': 'smart'}.get(w['type'], w['type'])
    s = f"A {t} {w['class'].lower()} made by {manu}." if manu != GENERIC else f"A {w['class'].lower()}."
    if w['type'] == 'tech':
        s += ' Tech weapon: charged (aimed) shots pierce walls.'
    if w['type'] == 'smart':
        s += ' Smart weapon: with a smart link, every shot lands under perfect conditions; cover and obstacles still block shots.'
    if kind == 'thrown':
        s += ' Can be thrown; it returns to the thrower after a few seconds.'
    return escape(s)

# ---------------------------------------------------------------- build

weapons = json.load(open(DATA, encoding='utf-8'))
guns = [w for w in weapons if w['class'] in FAMILY]
melee = [w for w in weapons if w['class'] in MELEE]
unknown = [w['name'] for w in weapons if w['class'] not in FAMILY and w['class'] not in MELEE]
assert not unknown, unknown

# calibers
calibers = {}
for w in guns:
    manu = manu_of(w)
    fam = FAMILY[w['class']]
    cid = caliber_id(manu, fam['fam'])
    c = calibers.setdefault(cid, dict(id=cid, manu=manu, fam=fam, types=set(), weapons=[]))
    c['types'].add(w['type'])
    c['weapons'].append(w)

ammo_xml, gun_xml, patch_xml, melee_xml, research_xml = [], [], [], [], []

ammo_xml.append('''  <ThingCategoryDef>
    <defName>RPC_Ammo</defName>
    <label>cyberpunk ammo</label>
    <parent>Ammo</parent>
    <iconPath>UI/Icons/ThingCategories/CaliberRifle</iconPath>
  </ThingCategoryDef>

  <CombatExtended.AmmoCategoryDef>
    <defName>RPC_Smart</defName>
    <label>smart</label>
    <labelShort>smart</labelShort>
    <description>Micro-guided rounds for smart weapons. They don't seek targets on their own; a smart link does the aiming. Blue tracers.</description>
  </CombatExtended.AmmoCategoryDef>
''')

# smart rounds (Edgerunners look): slow, near gravity-free (0 breaks CE's aim maths: 0/0 shot angle, round deleted on its first tick), BulletCE_Smart draws the long blue trail and curves onto the target
# with a smart link. Speed is low on purpose: at bullet speed the round lands before the homing can bend it.
SMART_SPEED = 30

def gun_tex(dn, fam, typ):
    """Own art when Common/Textures/Things/RimPunk/Weapons/<defName>/<defName>.png exists (Arasaka placeholders, see
    gen_apparel_textures.py), else the vanilla family texture."""
    own = f'Things/RimPunk/Weapons/{dn}/{dn}'
    if os.path.exists(os.path.join(ROOT, 'Common', 'Textures', *own.split('/')) + '.png'): return own
    return fam['tex'] if typ != 'tech' else 'Things/Item/Equipment/WeaponRanged/ChargeRifle'


def firing_sound(typ, family):
    if typ == 'tech':
        key = 'tech_' + ('light' if family in ('Pistol', 'SMG') else 'heavy' if family in ('Shotgun', 'Sniper', 'MG') else 'medium')
    elif typ == 'smart':
        key = 'smart_' + ('light' if family in ('Pistol', 'Revolver', 'SMG') else 'heavy')
    else:
        key = 'gun_' + family.lower()
    return WEAPON_SOUNDS[key]['defName']


def projectile(defname, label, base, dmg, penS, penB, speed, pellets=None, cls=None, tracer=False, charged=False, gravity=None):
    tex = 'Things/Projectile/Charged/ChargeShot' if charged else 'Things/Projectile/Bullet_Small'
    color = '\n      <color>(80,160,255)</color>' if tracer else ''
    thingclass = f'\n    <thingClass>{cls}</thingClass>' if cls else ''
    pel = f'\n      <pelletCount>{pellets}</pelletCount>\n      <spreadMult>8.9</spreadMult>' if pellets else ''
    if gravity is not None: pel += f'\n      <gravityFactor>{gravity}</gravityFactor>'
    return f'''  <ThingDef ParentName="BaseBulletCE">
    <defName>{defname}</defName>
    <label>{escape(label)}</label>{thingclass}
    <graphicData>
      <texPath>{tex}</texPath>
      <graphicClass>Graphic_Single</graphicClass>{color}
    </graphicData>
    <projectile Class="CombatExtended.ProjectilePropertiesCE">
      <damageDef>Bullet</damageDef>
      <speed>{speed}</speed>
      <damageAmountBase>{dmg}</damageAmountBase>
      <armorPenetrationSharp>{r2(penS)}</armorPenetrationSharp>
      <armorPenetrationBlunt>{r2(penB)}</armorPenetrationBlunt>{pel}
    </projectile>
  </ThingDef>
'''

def ammo(defname, label, catdef, ammoBase, tex, ammoClass, cookoff, research):
    return f'''  <ThingDef Class="CombatExtended.AmmoDef" ParentName="{ammoBase}">
    <defName>{defname}</defName>
    <label>{escape(label)}</label>
    <description>Cyberpunk ammunition. Only fits weapons of this caliber.</description>
    <statBases>
      <Mass>0.015</Mass>
      <Bulk>0.02</Bulk>
    </statBases>
    <tradeTags>
      <li>CE_AutoEnableTrade</li>
      <li>CE_AutoEnableCrafting</li>
      <li>RPC_Ammo</li>
    </tradeTags>
    <thingCategories>
      <li>{catdef}</li>
    </thingCategories>
    <graphicData>
      <texPath>{tex}</texPath>
      <graphicClass>Graphic_StackCount</graphicClass>
    </graphicData>
    <ammoClass>{ammoClass}</ammoClass>
    <cookOffProjectile>{cookoff}</cookOffProjectile>
  </ThingDef>
'''

def recipe(ammodef, label, research, steel, extra):
    ings = [('Steel', steel)] + list(extra.items())
    ing_xml = ''.join(f'''
      <li>
        <filter><thingDefs><li>{d}</li></thingDefs></filter>
        <count>{c}</count>
      </li>''' for d, c in ings)
    fixed = ''.join(f'<li>{d}</li>' for d, _ in ings)
    return f'''  <RecipeDef ParentName="AmmoRecipeBase">
    <defName>Make{ammodef}</defName>
    <label>make {escape(label)} x200</label>
    <description>Craft 200 {escape(label)}.</description>
    <jobString>Making {escape(label)}.</jobString>
    <researchPrerequisite>{research}</researchPrerequisite>
    <ingredients>{ing_xml}
    </ingredients>
    <fixedIngredientFilter><thingDefs>{fixed}</thingDefs></fixedIngredientFilter>
    <products>
      <{ammodef}>200</{ammodef}>
    </products>
    <workAmount>1600</workAmount>
  </RecipeDef>
'''

ammo_sets = {}  # (cid, type) -> AmmoSet defName
for cid, c in sorted(calibers.items()):
    fam, manu = c['fam'], c['manu']
    mult = MANUFACTURER.get(manu, 1.0)
    research = f'RPC_Research_{cid}'
    cat = f'RPC_Ammo_{cid}'
    nice = f"{manu} {fam['fam'].lower()}"
    ammo_xml.append(f'''  <ThingCategoryDef>
    <defName>{cat}</defName>
    <label>{escape(nice)} rounds</label>
    <parent>RPC_Ammo</parent>
  </ThingCategoryDef>
''')
    dmg = max(1, round(fam['dmg'] * mult)); penS = fam['penS'] * mult; penB = fam['penB'] * mult
    tex = f"Things/Ammo/{fam['ammoTex']}"
    if 'power' in c['types']:
        if fam.get('shotgun'):
            kinds = [('Buck', 'BuckShot', dmg, penS, penB, fam['pellets'], 'Shot'), ('Slug', 'Slug', round(dmg * 3.5), penS * 1.5, penB * 18, None, 'Shot')]
        else:
            kinds = [('FMJ', 'FullMetalJacket', dmg, penS, penB, None, 'FMJ'), ('AP', 'ArmorPiercing', round(dmg * 0.65), penS * 2, penB, None, 'AP'),
                     ('HP', 'HollowPoint', round(dmg * 1.15), penS * 0.5, penB, None, 'HP')]
        entries = []
        for suffix, ammoClass, d, ps, pb, pel, t in kinds:
            ad, pd = f'Ammo_RPC_{cid}_{suffix}', f'Bullet_RPC_{cid}_{suffix}'
            texp = 'Things/Ammo/Shotgun/Shot' if fam.get('shotgun') else f'{tex}/{t}'
            ammo_xml.append(ammo(ad, f'{nice} ({suffix})', cat, fam['ammoBase'], texp, ammoClass, pd, research))
            ammo_xml.append(projectile(pd, f'{nice} bullet ({suffix})', None, d, ps, pb, fam['speed'], pellets=pel))
            ammo_xml.append(recipe(ad, f'{nice} ({suffix}) rounds', research, 12, {}))
            entries.append((ad, pd))
        sd = f'AmmoSet_RPC_{cid}'
        ammo_xml.append(f'''  <CombatExtended.AmmoSetDef>
    <defName>{sd}</defName>
    <label>{escape(nice)}</label>
    <ammoTypes>
''' + ''.join(f'      <{a}>{p}</{a}>\n' for a, p in entries) + '''    </ammoTypes>
  </CombatExtended.AmmoSetDef>
''')
        ammo_sets[(cid, 'power')] = sd
    if 'tech' in c['types']:
        ad, pd = f'Ammo_RPC_{cid}_Charged', f'Bullet_RPC_{cid}_Charged'
        ammo_xml.append(ammo(ad, f'{nice} (charged)', cat, fam['ammoBase'], f'{tex}/AP' if fam['ammoTex'] != 'Shotgun' else 'Things/Ammo/Shotgun/Shot', 'Charged', pd, research))
        ammo_xml.append(projectile(pd, f'{nice} charged bullet', None, round(dmg * 1.1), penS * 2.5, penB * 1.5, int(fam['speed'] * 1.3),
                                   pellets=fam.get('pellets'), cls='RimPunkCE.BulletCE_Tech', charged=True))
        ammo_xml.append(recipe(ad, f'{nice} (charged) rounds', research, 10, {'Plasteel': 2, 'ComponentIndustrial': 1}))
        sd = f'AmmoSet_RPC_{cid}_Tech'
        ammo_xml.append(f'''  <CombatExtended.AmmoSetDef>
    <defName>{sd}</defName>
    <label>{escape(nice)} (charged)</label>
    <ammoTypes>
      <{ad}>{pd}</{ad}>
    </ammoTypes>
  </CombatExtended.AmmoSetDef>
''')
        ammo_sets[(cid, 'tech')] = sd
    if 'smart' in c['types']:
        ad, pd = f'Ammo_RPC_{cid}_Smart', f'Bullet_RPC_{cid}_Smart'
        ammo_xml.append(ammo(ad, f'{nice} (smart)', cat, fam['ammoBase'], f'{tex}/FMJ' if fam['ammoTex'] != 'Shotgun' else 'Things/Ammo/Shotgun/Shot', 'RPC_Smart', pd, research))
        ammo_xml.append(projectile(pd, f'{nice} smart bullet', None, dmg, penS, penB, SMART_SPEED, pellets=fam.get('pellets'), cls='RimPunkCE.BulletCE_Smart', tracer=True, gravity=0.05))
        ammo_xml.append(recipe(ad, f'{nice} (smart) rounds', research, 12, {'ComponentIndustrial': 1}))
        sd = f'AmmoSet_RPC_{cid}_Smart'
        ammo_xml.append(f'''  <CombatExtended.AmmoSetDef>
    <defName>{sd}</defName>
    <label>{escape(nice)} (smart)</label>
    <ammoTypes>
      <{ad}>{pd}</{ad}>
    </ammoTypes>
  </CombatExtended.AmmoSetDef>
''')
        ammo_sets[(cid, 'smart')] = sd

    # research for this caliber
    prereqs = [fam['research']] + sorted({r for t in c['types'] for r in TYPE_RESEARCH.get(t, [])})
    cost = int(round(500 * {'Pistol': 1, 'Revolver': 1, 'SMG': 1.3, 'Shotgun': 1.2, 'Rifle': 1.6, 'Precision': 1.6, 'Sniper': 2, 'MG': 2}[fam['fam']] * mult / 50) * 50)
    research_xml.append(f'''  <ResearchProjectDef ParentName="RPC_WeaponResearchBase">
    <defName>{research}</defName>
    <label>{escape(nice)}</label>
    <description>Make the {escape(nice)} caliber and the weapons that fire it: {escape(', '.join(sorted(w['name'] for w in c['weapons'])))}.</description>
    <baseCost>{cost}</baseCost>
    <techLevel>{'Spacer' if c['types'] & {'tech', 'smart'} else 'Industrial'}</techLevel>
    <prerequisites>{''.join(f'<li>{p}</li>' for p in prereqs)}</prerequisites>
    <researchViewX>{['Pistol', 'Revolver', 'SMG', 'Shotgun', 'Rifle', 'Precision', 'Sniper', 'MG'].index(fam['fam'])}</researchViewX>
    <researchViewY>{0}</researchViewY>
  </ResearchProjectDef>
''')

# spread research nodes vertically per column
cols = {}
for i, block in enumerate(research_xml):
    x = re.search(r'<researchViewX>(\d+)</researchViewX>', block).group(1)
    y = cols.get(x, 0); cols[x] = y + 1
    research_xml[i] = block.replace('<researchViewY>0</researchViewY>', f'<researchViewY>{y * 0.75:.2f}</researchViewY>')

# guns
for w in guns:
    manu = manu_of(w)
    fam = FAMILY[w['class']]
    cid = caliber_id(manu, fam['fam'])
    mult = MANUFACTURER.get(manu, 1.0)
    dn = thing_defname('RPC_Gun_', w['name'], fam['fam'])
    typ = w['type'] or 'power'
    shot_sound = firing_sound(typ, fam['fam'])
    market = int(round(MARKET[fam['fam']] * mult * (1.3 if typ != 'power' else 1.0) / 10) * 10)
    verb = {'power': 'CombatExtended.Verb_ShootCE', 'tech': 'RimPunkCE.Verb_ShootTech', 'smart': 'RimPunkCE.Verb_ShootSmart'}[typ]
    rng = min(MAX_RANGE, int(round(w['range'] or 30)))
    mag = int(w['magazine'] or 10)
    reload = r2(max(1.0, w['reload'] or 3.0) * 1.3)
    speed = w['attackSpeed'] or 3
    cooldown = fam['cd']
    burst_xml, fm_xml = '', ''
    if fam.get('auto'):
        tbs = max(2, min(15, round(60 / max(speed, 1))))
        burst_xml = f"\n      <burstShotCount>{fam['burst']}</burstShotCount>\n      <ticksBetweenBurstShots>{tbs}</ticksBetweenBurstShots>"
        fm_xml = f"\n      <aimedBurstShotCount>{fam['aimedBurst']}</aimedBurstShotCount>\n      <aiUseBurstMode>TRUE</aiUseBurstMode>"
    else:
        cooldown = max(0.2, min(2.0, fam['cd'] * (3.0 / max(speed, 0.5)) ** 0.5)) if not fam.get('shotgun') else fam['cd']
    aim = 'AimedShot' if typ == 'tech' else 'Snapshot'
    extra = TYPE_EXTRA_COST[typ]
    steel = int(fam['steel'] * mult); comp = max(1, int(round(fam['comp'] * mult)))
    cost_xml = f'      <Steel>{steel}</Steel>\n      <ComponentIndustrial>{comp}</ComponentIndustrial>\n' + ''.join(f'      <{k}>{v}</{k}>\n' for k, v in extra.items())
    # RPC_W_<Manu>_<Class>: weaponTags are OR'd, so pawn kinds that want "Arasaka rifles" need one combined tag
    tags = ['RPC_Gun', f'RPC_Manu_{ident(manu)}', f"RPC_Class_{fam['fam']}", f'RPC_Type_{typ}', f"RPC_W_{ident(manu)}_{fam['fam']}",
            f"RPC_WT_{fam['fam']}_{typ}"]  # class + type, e.g. RPC_WT_Rifle_smart
    tag_xml = ''.join(f'      <li>{t}</li>\n' for t in tags + fam['tags'])
    trade_xml = f'      <li>RPC_Weapon</li>\n      <li>RPC_Manu_{ident(manu)}</li>\n'
    # default projectile: first ammo type of this gun's ammo set
    sd = ammo_sets[(cid, typ)]
    m = re.search(rf'<defName>{sd}</defName>.*?<ammoTypes>\s*<(\w+)>(\w+)<', '\n'.join(ammo_xml), re.S)
    first_proj = m.group(2)
    gun_xml.append(f'''  <ThingDef ParentName="RPC_GunBase">
    <defName>{dn}</defName>
    <label>{escape(w['name'])}</label>
    <description>{describe(w, manu, 'gun')}</description>
    <techLevel>{'Spacer' if typ != 'power' else 'Industrial'}</techLevel>
    <graphicData>
      <texPath>{gun_tex(dn, fam, typ)}</texPath>
      <graphicClass>Graphic_Single</graphicClass>
    </graphicData>
    <soundInteract>{fam['interact']}</soundInteract>
    <statBases>
      <WorkToMake>{fam['work']}</WorkToMake>
      <Mass>{fam['mass']}</Mass>
      <MarketValue>{market}</MarketValue>
      <RangedWeapon_DamageMultiplier>{r2(w.get('damageMultiplier', 1.0))}</RangedWeapon_DamageMultiplier>
    </statBases>
    <costList>
{cost_xml}    </costList>
    <recipeMaker>
      <recipeUsers Inherit="False"><li>{TYPE_BENCH[typ]}</li></recipeUsers>
      <researchPrerequisite>RPC_Research_{cid}</researchPrerequisite>
    </recipeMaker>
    <weaponTags>
{tag_xml}    </weaponTags>
    <tradeTags>
{trade_xml}    </tradeTags>
    <verbs>
      <li>
        <verbClass>Verb_Shoot</verbClass>
        <hasStandardCommand>true</hasStandardCommand>
        <defaultProjectile>{first_proj}</defaultProjectile>
        <range>{rng}</range>
        <soundCast>{shot_sound}</soundCast>
      </li>
    </verbs>
  </ThingDef>
''')
    patch_xml.append(f'''  <Operation Class="CombatExtended.PatchOperationMakeGunCECompatible">
    <defName>{dn}</defName>
    <statBases>
      <Mass>{fam['mass']}</Mass>
      <RangedWeapon_Cooldown>{r2(cooldown)}</RangedWeapon_Cooldown>
      <RangedWeapon_DamageMultiplier>{r2(w.get('damageMultiplier', 1.0))}</RangedWeapon_DamageMultiplier>
      <SightsEfficiency>{fam['sights']}</SightsEfficiency>
      <ShotSpread>{fam['spread']}</ShotSpread>
      <SwayFactor>{fam['sway']}</SwayFactor>
      <Bulk>{fam['bulk']}</Bulk>
      <WorkToMake>{fam['work']}</WorkToMake>
    </statBases>
    <Properties>
      <recoilAmount>{fam['recoil']}</recoilAmount>
      <verbClass>{verb}</verbClass>
      <hasStandardCommand>true</hasStandardCommand>
      <defaultProjectile>{first_proj}</defaultProjectile>
      <warmupTime>{fam['warm']}</warmupTime>
      <range>{rng}</range>{burst_xml}
      <soundCast>{shot_sound}</soundCast>
      <muzzleFlashScale>9</muzzleFlashScale>
    </Properties>
    <AmmoUser>
      <magazineSize>{mag}</magazineSize>
      <reloadTime>{reload}</reloadTime>
      <ammoSet>{sd}</ammoSet>
    </AmmoUser>
    <FireModes>
      <aiAimMode>{aim}</aiAimMode>{fm_xml}
    </FireModes>
  </Operation>
''')

# melee
melee_cats = {}
for w in melee:
    spec = MELEE[w['class']]
    manu = manu_of(w)
    thrown = w['type'] == 'thrown'
    dn = thing_defname('RPC_Melee_', w['name'], spec['fam'])
    mult = MANUFACTURER.get(manu, 1.0)
    research = f"RPC_Research_Melee_{'Thrown' if thrown else ('Blades' if w['type'] == 'blade' else 'Blunt')}"
    melee_cats.setdefault(research, (spec['research'], []))[1].append(w['name'])
    tools = []
    extra = MELEE_EXTRA.get(w['name'])
    for (label, cap, power, cd, ps, pb) in spec['tools']:
        ex = ''
        if extra and cap != 'Poke':
            ex = f'''
        <extraMeleeDamages>
          <li><def>{extra[0]}</def><amount>{extra[1]}</amount><chance>{extra[2]}</chance></li>
        </extraMeleeDamages>'''
        pen = (f'\n        <armorPenetrationSharp>{r2(ps * mult)}</armorPenetrationSharp>' if ps else '') + f'\n        <armorPenetrationBlunt>{r2(pb * mult)}</armorPenetrationBlunt>'
        tools.append(f'''      <li Class="CombatExtended.ToolCE">
        <label>{label}</label>
        <capacities><li>{cap}</li></capacities>
        <power>{r2(power * mult * w.get('damageMultiplier', 1.0))}</power>
        <cooldownTime>{cd}</cooldownTime>{pen}{ex}
      </li>''')
    comps = ''
    if thrown:
        comps = '''
    <comps Inherit="False">
      <li Class="CompProperties_Forbiddable"/>
      <li Class="CompProperties_EquippableAbility">
        <abilityDef>RPC_Ability_ThrowWeapon</abilityDef>
      </li>
      <li Class="CompProperties_Styleable"/>
      <li><compClass>CompQuality</compClass></li>
      <li Class="CompProperties_Art">
        <nameMaker>NamerArtWeaponMelee</nameMaker>
        <descriptionMaker>ArtDescription_WeaponMelee</descriptionMaker>
        <minQualityForArtistic>Excellent</minQualityForArtistic>
      </li>
    </comps>'''
    tags = ['RPC_Melee', f"RPC_MeleeClass_{spec['fam']}", f'RPC_Manu_{ident(manu)}'] + (['RPC_Melee_Thrown'] if thrown else [])
    melee_xml.append(f'''  <ThingDef ParentName="RPC_MeleeBase">
    <defName>{dn}</defName>
    <label>{escape(w['name'])}</label>
    <description>{describe(w, manu, 'thrown' if thrown else 'melee')}</description>
    <graphicData>
      <texPath>{spec['tex']}</texPath>
      <graphicClass>Graphic_Single</graphicClass>
    </graphicData>
    <statBases>
      <Mass>{spec['mass']}</Mass>
      <Bulk>{spec['bulk']}</Bulk>
      <MeleeCounterParryBonus>{spec['parry']}</MeleeCounterParryBonus>
      <WorkToMake>{int(8000 * mult)}</WorkToMake>
    </statBases>
    <costList>
      <Steel>{int(40 * mult)}</Steel>
      <ComponentIndustrial>{1 if w['class'] in ('Chainsword',) or w['name'].startswith('Electric') else 0}</ComponentIndustrial>
    </costList>
    <recipeMaker>
      <researchPrerequisite>{research}</researchPrerequisite>
    </recipeMaker>
    <weaponTags>
''' + ''.join(f'      <li>{t}</li>\n' for t in tags) + f'''    </weaponTags>
    <tradeTags>
      <li>RPC_Weapon</li>
      <li>RPC_Manu_{ident(manu)}</li>
    </tradeTags>
    <tools>
''' + '\n'.join(tools) + f'''
    </tools>{comps}
  </ThingDef>
''')

for i, (research, (prereq, names)) in enumerate(sorted(melee_cats.items())):
    label = {'RPC_Research_Melee_Thrown': 'throwing weapons', 'RPC_Research_Melee_Blades': 'blades', 'RPC_Research_Melee_Blunt': 'blunt weapons'}[research]
    research_xml.append(f'''  <ResearchProjectDef ParentName="RPC_WeaponResearchBase">
    <defName>{research}</defName>
    <label>{label}</label>
    <description>Make these weapons: {escape(', '.join(sorted(names)))}.</description>
    <baseCost>{600 if 'Blades' in research else 400}</baseCost>
    <techLevel>Industrial</techLevel>
    <prerequisites><li>{prereq}</li></prerequisites>
    <researchViewX>8</researchViewX>
    <researchViewY>{i * 0.75:.2f}</researchViewY>
  </ResearchProjectDef>
''')

# fix ' 0,' typo guard (empty component counts)
melee_xml = [b.replace('      <ComponentIndustrial>0</ComponentIndustrial>\n', '') for b in melee_xml]

def write(rel, blocks, root='Defs'):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\n')
        f.write('<!-- GENERATED by Source/Tools/gen_weapons.py. Do not edit; change the generator / data and rerun. -->\n')
        f.write(f'<{root}>\n\n' + '\n'.join(blocks) + f'\n</{root}>\n')

write('Common/Defs/Weapons/Generated_Ammo.xml', ammo_xml)
write('Common/Defs/Weapons/Generated_Guns.xml', gun_xml)
write('Common/Patches/Generated_Guns_CE.xml', patch_xml, root='Patch')
write('Common/Defs/Weapons/Generated_Melee.xml', melee_xml)
write('Common/Defs/Weapons/Generated_Research.xml', research_xml)
print(f'{len(guns)} guns, {len(melee)} melee, {len(calibers)} calibers, {len(research_xml)} research projects')
