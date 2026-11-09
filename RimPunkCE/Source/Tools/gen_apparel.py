"""
Faction uniforms: one set of apparel per faction, worn by role (guard, soldier, heavy, netrunner, sniper, brawler, boss, trader).

    python -I Source/Tools/gen_apparel.py        (run from the RimPunkCE folder; then run gen_factions.py)

Output (overwritten every run, do not hand-edit): Common/Defs/Apparel/Generated_Apparel.xml
gen_factions.py imports OUTFITS / outfit() from here to give every pawn kind its apparelRequired list.

Looks reuse vanilla textures tinted per faction (they are greyscale, the game multiplies in the colour).
Trauma Team and MaxTac keep their hand-drawn textures unchanged. Other factions use vanilla textures with a single
primary tint from data/faction_colours.json. Retired Arasaka artwork stays in Source/Art. Armour values are CE scale.
"""
import os
import json
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
V = 'Things/Pawn/Humanlike/Apparel/'
CUSTOM = 'Things/RimPunk/Apparel/'
with open(os.path.join(os.path.dirname(__file__), 'data', 'faction_colours.json'), encoding='utf-8') as palette_file:
    FACTION_COLOURS = json.load(palette_file)

BODY_FULL = ['Torso', 'Neck', 'Shoulders', 'Arms', 'Legs']

# key: look and stats. worn=None means vanilla has no worn texture (pants): covers, but is not drawn.
# cat: thingCategory. armor values roughly follow CE's patches of the vanilla item each one copies.
T = {
    # --- on skin
    'shirt':      dict(label='tank top', tex=V + 'ShirtBasic/ShirtBasic', worn=V + 'ShirtBasic/ShirtBasic', layers=['OnSkin'], groups=['Torso', 'Shoulders'],
                       cat='Apparel', sharp=0.3, blunt=0.6, bulk=1, wbulk=0, mass=0.3, hp=80, value=40, cold=4, cost={'Cloth': 25}),
    'collar':     dict(label='shirt', tex=V + 'ShirtButton/ShirtButton', worn=V + 'ShirtButton/ShirtButton', layers=['OnSkin'],
                       groups=['Torso', 'Neck', 'Shoulders', 'Arms'], cat='Apparel', sharp=0.4, blunt=0.8, bulk=1, wbulk=0, mass=0.4, hp=100, value=60, cold=6,
                       cost={'Cloth': 35}),
    'bodysuit':   dict(label='bodysuit', tex=V + 'EltexShirt/EltexShirt', worn=V + 'EltexShirt/EltexShirt', layers=['OnSkin'], groups=['Torso', 'Shoulders'],
                       cat='Apparel', sharp=0.8, blunt=1.5, bulk=1, wbulk=0, mass=0.4, hp=120, value=180, cold=6, cost={'Synthread': 25}),
    'pants':      dict(label='pants', tex=V + 'Pants/Pants', worn=None, layers=['OnSkin'], groups=['Legs'], cat='Apparel',
                       sharp=0.4, blunt=0.8, bulk=1, wbulk=0, mass=0.4, hp=100, value=50, cold=6, cost={'Cloth': 30}),
    'flakpants':  dict(label='armored pants', tex=V + 'FlakPants/FlakPants', worn=None, layers=['OnSkin', 'Middle'], groups=['Legs'], cat='ApparelArmor',
                       sharp=4, blunt=7, bulk=4, wbulk=2.5, mass=2.5, hp=160, value=280, cold=8, cost={'Steel': 30, 'Cloth': 30}),
    # --- middle
    'flakvest':   dict(label='armor vest', tex=V + 'FlakVest/FlakVest', worn=V + 'FlakVest/FlakVest', layers=['Middle'], groups=['Torso', 'Neck'],
                       cat='ApparelArmor', sharp=8, blunt=18, bulk=5, wbulk=3, mass=8, hp=150, value=420, cold=2, cost={'Steel': 50, 'ComponentIndustrial': 1}),
    'vest':       dict(label='vest', tex=V + 'EltexVest/EltexVest', worn=V + 'EltexVest/EltexVest', layers=['Middle'], groups=['Torso', 'Neck', 'Shoulders'],
                       cat='Apparel', sharp=0.8, blunt=1.5, bulk=2, wbulk=0.5, mass=0.6, hp=120, value=140, cold=6, cost={'Cloth': 40}),
    # --- shell
    'jacket':     dict(label='jacket', tex=V + 'Jacket/Jacket', worn=V + 'Jacket/Jacket', layers=['Shell'], groups=['Torso', 'Neck', 'Shoulders', 'Arms'],
                       cat='Apparel', sharp=1.5, blunt=3, bulk=5, wbulk=1, mass=1.6, hp=160, value=160, cold=18, cost={'Leather_Plain': 60}),
    'duster':     dict(label='long coat', tex=V + 'Duster/Duster', worn=V + 'Duster/Duster', layers=['Shell'], groups=BODY_FULL,
                       cat='Apparel', sharp=2, blunt=3.5, bulk=7.5, wbulk=2, mass=2.2, hp=200, value=220, cold=22, cost={'Leather_Plain': 80}),
    'parka':      dict(label='parka', tex=V + 'Parka/Parka', worn=V + 'Parka/Parka', layers=['Shell'], groups=['Torso', 'Neck', 'Shoulders', 'Arms'],
                       cat='Apparel', sharp=1.5, blunt=3, bulk=10, wbulk=5, mass=3, hp=180, value=180, cold=60, cost={'Cloth': 80}),
    'flakjacket': dict(label='armored jacket', tex=V + 'FlakJacket/FlakJacket', worn=V + 'FlakJacket/FlakJacket', layers=['Shell'],
                       groups=['Torso', 'Neck', 'Shoulders', 'Arms'], cat='ApparelArmor', sharp=4, blunt=7, bulk=5, wbulk=2.5, mass=4, hp=180, value=360, cold=20,
                       cost={'Steel': 40, 'Cloth': 40}),
    'robe':       dict(label='robe', tex=V + 'EltexRobe/EltexRobe', worn=V + 'EltexRobe/EltexRobe', layers=['Shell'], groups=BODY_FULL,
                       cat='Apparel', sharp=1, blunt=2, bulk=6, wbulk=1.5, mass=1.5, hp=160, value=200, cold=20, cost={'Synthread': 50}),
    # --- full armour (middle + shell)
    'recon':      dict(label='tactical armor', tex=V + 'ReconArmor/ReconArmor', worn=V + 'ReconArmor/ReconArmor', layers=['Middle', 'Shell'], groups=BODY_FULL,
                       cat='ApparelArmor', sharp=16, blunt=34, bulk=80, wbulk=12, mass=30, hp=400, value=1800, cold=20, heat=0.3,
                       cost={'Plasteel': 70, 'ComponentSpacer': 2, 'Synthread': 30}),
    'marine':     dict(label='heavy armor', tex=V + 'PowerArmor/PowerArmor', worn=V + 'PowerArmor/PowerArmor', layers=['Middle', 'Shell'], groups=BODY_FULL,
                       cat='ApparelArmor', sharp=20, blunt=45, bulk=100, wbulk=15, mass=50, hp=500, value=2600, cold=24, heat=0.4,
                       cost={'Plasteel': 110, 'ComponentSpacer': 4, 'Uranium': 10}, offsets={'CarryWeight': 80, 'CarryBulk': 10}),
    'prestige':   dict(label='officer armor', tex=V + 'PrestigeReconArmor/PrestigeReconArmor', worn=V + 'PrestigeReconArmor/PrestigeReconArmor', mask=True,
                       layers=['Middle', 'Shell'], groups=BODY_FULL, cat='ApparelArmor', sharp=18, blunt=40, bulk=80, wbulk=12, mass=30, hp=450, value=2600,
                       cold=22, heat=0.35, cost={'Plasteel': 80, 'ComponentSpacer': 3, 'Gold': 10}),
    'prestigeheavy': dict(label='officer heavy armor', tex=V + 'PrestigeMarineArmor/PrestigeMarineArmor', worn=V + 'PrestigeMarineArmor/PrestigeMarineArmor',
                       mask=True, layers=['Middle', 'Shell'], groups=BODY_FULL, cat='ApparelArmor', sharp=22, blunt=50, bulk=100, wbulk=15, mass=50, hp=550,
                       value=3600, cold=24, heat=0.45, cost={'Plasteel': 120, 'ComponentSpacer': 5, 'Gold': 15}, offsets={'CarryWeight': 80, 'CarryBulk': 10}),
    # --- head
    'simplehelm': dict(label='helmet', tex=V + 'SimpleHelmet/SimpleHelmet', worn=V + 'SimpleHelmet/SimpleHelmet', layers=['Overhead'], groups=['UpperHead'],
                       cat='ArmorHeadgear', sharp=4, blunt=8, bulk=3, wbulk=1, mass=1.5, hp=120, value=120, cold=2, cost={'Steel': 40}),
    'advhelm':    dict(label='combat helmet', tex=V + 'AdvancedHelmet/AdvancedHelmet', worn=V + 'AdvancedHelmet/AdvancedHelmet', layers=['Overhead'],
                       groups=['UpperHead'], cat='ArmorHeadgear', sharp=8, blunt=12, bulk=4, wbulk=1, mass=1.6, hp=150, value=320, cold=2,
                       cost={'Plasteel': 20, 'ComponentIndustrial': 1}),
    'reconhelm':  dict(label='tactical helmet', tex=V + 'ReconArmorHelmet/ReconHelmet', worn=V + 'ReconArmorHelmet/ReconHelmet', layers=['Overhead'],
                       groups=['FullHead'], cat='ArmorHeadgear', sharp=12, blunt=26, bulk=4, wbulk=1, mass=3, hp=190, value=650, cold=2, heat=0.3,
                       cost={'Plasteel': 30, 'ComponentSpacer': 1}),
    'marinehelm': dict(label='heavy helmet', tex=V + 'PowerArmorHelmet/PowerArmorHelmet', worn=V + 'PowerArmorHelmet/PowerArmorHelmet', layers=['Overhead'],
                       groups=['FullHead'], cat='ArmorHeadgear', sharp=16, blunt=36, bulk=5, wbulk=1, mass=4.8, hp=240, value=900, cold=4, heat=0.4,
                       cost={'Plasteel': 40, 'ComponentSpacer': 1}),
    'visor':      dict(label='visor', tex=V + 'Gunlink/Gunlink', worn=V + 'Gunlink/Gunlink', layers=['Overhead'], groups=['UpperHead'], cat='Headgear',
                       sharp=0.5, blunt=1, bulk=1, wbulk=0, mass=0.3, hp=80, value=260, cold=0, cost={'Plasteel': 10, 'ComponentIndustrial': 1},
                       offsets={'AimingAccuracy': 0.05}),
    'netrig':     dict(label='netrunner rig', tex=V + 'EltexSkullcap/EltexSkullcap', worn=V + 'EltexSkullcap/EltexSkullcap', layers=['Overhead'],
                       groups=['UpperHead'], cat='Headgear', sharp=0.5, blunt=1, bulk=1, wbulk=0, mass=0.4, hp=80, value=300, cold=2,
                       cost={'Synthread': 10, 'ComponentIndustrial': 2}),
    'beret':      dict(label='beret', tex=V + 'Beret/Beret', worn=V + 'Beret/Beret', layers=['Overhead'], groups=['UpperHead'], cat='Headgear',
                       sharp=0.2, blunt=0.4, bulk=1, wbulk=0, mass=0.1, hp=60, value=40, cold=2, cost={'Cloth': 15}),
    'cowboy':     dict(label='hat', tex=V + 'CowboyHat/CowboyHat', worn=V + 'CowboyHat/CowboyHat', layers=['Overhead'], groups=['UpperHead'], cat='Headgear',
                       sharp=0.3, blunt=0.6, bulk=1, wbulk=0, mass=0.2, hp=80, value=60, cold=3, cost={'Leather_Plain': 25}),
    'bowler':     dict(label='bowler hat', tex=V + 'BowlerHat/BowlerHat', worn=V + 'BowlerHat/BowlerHat', layers=['Overhead'], groups=['UpperHead'],
                       cat='Headgear', sharp=0.3, blunt=0.6, bulk=1, wbulk=0, mass=0.2, hp=80, value=60, cold=3, cost={'Cloth': 25}),
    'tuque':      dict(label='beanie', tex=V + 'Tuque/Tuque', worn=V + 'Tuque/Tuque', layers=['Overhead'], groups=['UpperHead'], cat='Headgear',
                       sharp=0.2, blunt=0.4, bulk=1, wbulk=0, mass=0.1, hp=60, value=30, cold=8, cost={'Cloth': 15}),
    'hood':       dict(label='hood', tex=V + 'Hood/Hood', worn=V + 'Hood/Hood', layers=['Overhead'], groups=['UpperHead'], cat='Headgear',
                       sharp=0.2, blunt=0.4, bulk=1, wbulk=0, mass=0.2, hp=60, value=40, cold=6, cost={'Cloth': 20}),
    'mask':       dict(label='face mask', tex=V + 'ClothMask/ClothMask', worn=V + 'ClothMask/ClothMask', layers=['Overhead'], groups=['Mouth'], cat='Headgear',
                       sharp=0.2, blunt=0.4, bulk=0.5, wbulk=0, mass=0.1, hp=50, value=30, cold=1, cost={'Cloth': 10}),
    'faceplate':  dict(label='faceplate', tex=V + 'WarMask/WarMask', worn=V + 'WarMask/WarMask', layers=['Overhead'], groups=['FullHead'], cat='ArmorHeadgear',
                       sharp=3, blunt=6, bulk=2, wbulk=0.5, mass=1, hp=120, value=120, cold=1, cost={'Steel': 25}),
    'veil':       dict(label='veil', tex=V + 'Veil/Veil', worn=V + 'Veil/Veil', layers=['Overhead'], groups=['FullHead'], cat='Headgear',
                       sharp=0.2, blunt=0.4, bulk=1, wbulk=0, mass=0.2, hp=60, value=40, cold=2, cost={'Cloth': 20}),
    # --- custom looks (own textures)
    'trauma_shirt':  dict(label='Trauma Team shirt', tex=CUSTOM + 'TraumaShirt/TraumaShirt', worn=CUSTOM + 'TraumaShirt/TraumaShirt', custom=True,
                          layers=['OnSkin'], groups=['Torso', 'Shoulders'], cat='Apparel', sharp=0.5, blunt=1, bulk=1, wbulk=0, mass=0.3, hp=100,
                          value=80, cold=5, cost={'Synthread': 20}),
    'trauma_pants':  dict(label='Trauma Team pants', tex=CUSTOM + 'TraumaPants/TraumaPants', worn=CUSTOM + 'TraumaPants/TraumaPants', custom=True,
                          layers=['OnSkin'], groups=['Legs'], cat='Apparel', sharp=0.5, blunt=1, bulk=1, wbulk=0, mass=0.4, hp=100, value=80, cold=6,
                          cost={'Synthread': 25}),
    'trauma_vest':   dict(label='Trauma Team combat vest', tex=CUSTOM + 'TraumaVest/TraumaVest', worn=CUSTOM + 'TraumaVest/TraumaVest', custom=True,
                          layers=['Middle'], groups=['Torso', 'Neck'], cat='ApparelArmor', sharp=12, blunt=26, bulk=6, wbulk=3, mass=8, hp=220,
                          value=1200, cold=3, heat=0.3, cost={'Plasteel': 40, 'ComponentSpacer': 1}),
    'trauma_helmet': dict(label='Trauma Team helmet', tex=CUSTOM + 'TraumaHelmet/TraumaHelmet', worn=CUSTOM + 'TraumaHelmet/TraumaHelmet', custom=True,
                          layers=['Overhead'], groups=['FullHead'], cat='ArmorHeadgear', sharp=14, blunt=30, bulk=4, wbulk=1, mass=3, hp=220, value=900, cold=2,
                          heat=0.35, cost={'Plasteel': 35, 'ComponentSpacer': 1}),
    'maxtac_armor':  dict(label='MaxTac armor', tex=CUSTOM + 'MaxTacArmor/MaxTacArmor', worn=CUSTOM + 'MaxTacArmor/MaxTacArmor', custom=True,
                          layers=['Middle', 'Shell'], groups=BODY_FULL, cat='ApparelArmor', sharp=26, blunt=58, bulk=100, wbulk=14, mass=55, hp=650, value=5000,
                          cold=24, heat=0.5, cost={'Plasteel': 140, 'ComponentSpacer': 6, 'Uranium': 20}, offsets={'CarryWeight': 100, 'CarryBulk': 15}),
    'maxtac_helmet': dict(label='MaxTac helmet', tex=CUSTOM + 'MaxTacHelmet/MaxTacHelmet', worn=CUSTOM + 'MaxTacHelmet/MaxTacHelmet', custom=True,
                          layers=['Overhead'], groups=['FullHead'], cat='ArmorHeadgear', sharp=22, blunt=50, bulk=5, wbulk=1, mass=5, hp=300, value=1500, cold=4,
                          heat=0.5, cost={'Plasteel': 50, 'ComponentSpacer': 2}),
    'maxtac_lead':   dict(label='MaxTac sergeant helmet', tex=CUSTOM + 'MaxTacLeadHelmet/MaxTacLeadHelmet', worn=CUSTOM + 'MaxTacLeadHelmet/MaxTacLeadHelmet',
                          custom=True, layers=['Overhead'], groups=['FullHead'], cat='ArmorHeadgear', sharp=22, blunt=50, bulk=5, wbulk=1, mass=5, hp=300,
                          value=1600, cold=4, heat=0.5, cost={'Plasteel': 50, 'ComponentSpacer': 2}),
}

# Keep existing Arasaka def names and stats for saves/loadouts, using the original vanilla templates.
def _arasaka(key, base, label, tex, **kw):
    T[key] = dict(T[base], **kw)


_arasaka('arasaka_shirt_white', 'collar', 'Arasaka white shirt', 'ArasakaShirtWhite')
_arasaka('arasaka_shirt', 'collar', 'Arasaka black shirt', 'ArasakaShirt')
_arasaka('arasaka_pants', 'pants', 'Arasaka pants', 'ArasakaPants')
_arasaka('arasaka_bodysuit', 'bodysuit', 'Arasaka bodysuit', 'ArasakaBodysuit')
_arasaka('arasaka_vest', 'vest', 'Arasaka light vest', 'ArasakaVestLight')
_arasaka('arasaka_carrier', 'flakvest', 'Arasaka plate carrier', 'ArasakaPlateCarrier')
_arasaka('arasaka_jacket', 'jacket', 'Arasaka jacket', 'ArasakaJacket')
_arasaka('arasaka_duster', 'duster', 'Arasaka long coat', 'ArasakaDuster')
_arasaka('arasaka_recon', 'recon', 'Arasaka tactical armor', 'ArasakaReconArmor')
_arasaka('arasaka_heavy', 'marine', 'Arasaka heavy armor', 'ArasakaHeavyArmor')
_arasaka('arasaka_helmet', 'advhelm', 'Arasaka combat helmet', 'ArasakaCombatHelmet')
_arasaka('arasaka_reconhelm', 'reconhelm', 'Arasaka tactical helmet', 'ArasakaReconHelmet')
_arasaka('arasaka_heavyhelm', 'marinehelm', 'Arasaka heavy helmet', 'ArasakaHeavyHelmet')
_arasaka('arasaka_netrig', 'netrig', 'Arasaka netrunner rig', 'ArasakaNetRig')
_arasaka('arasaka_visor', 'visor', 'Arasaka visor', 'ArasakaVisor')

# colour words used in the outfit tables, RGB 0-255 (the vanilla textures are near white, so this is roughly the final colour)
C = {
    'black': (40, 40, 44), 'charcoal': (70, 72, 78), 'grey': (130, 132, 136), 'white': (232, 232, 228), 'chrome': (190, 196, 205),
    'red': (190, 30, 36), 'crimson': (140, 20, 28), 'arasaka': (200, 24, 30),
    'olive': (96, 104, 62), 'khaki': (170, 156, 112), 'tan': (190, 168, 122), 'sand': (205, 185, 140), 'brown': (110, 76, 48), 'leather': (90, 58, 38),
    'rust': (150, 74, 40), 'orange': (220, 120, 40),
    'gold': (212, 168, 52), 'yellow': (230, 200, 60),
    'green': (60, 160, 80), 'lime': (150, 210, 70), 'teal': (40, 170, 170), 'cyan': (60, 210, 230),
    'navy': (34, 44, 82), 'blue': (50, 100, 190), 'steel': (80, 100, 130), 'skyblue': (110, 170, 230),
    'purple': (110, 60, 160), 'violet': (150, 90, 210), 'darkpurple': (60, 34, 84), 'pink': (230, 90, 170), 'magenta': (210, 40, 140),
    'custom': (255, 255, 255),
}

# Legacy outfit identities are retained below. Their templates always use vanilla art, regardless of files on disk.
ARASAKA_TINTED = {
    'Guard':     ('security guard', ['collar:white', 'flakvest:black', 'pants:black', 'advhelm:arasaka'], []),
    'Grunt':     ('soldier', ['recon:black', 'reconhelm:arasaka', 'collar:black', 'pants:black'], []),
    'Enforcer':  ('heavy gunner', ['marine:black', 'marinehelm:arasaka', 'collar:black', 'pants:black'], []),
    'Netrunner': ('netrunner', ['bodysuit:black', 'netrig:arasaka', 'pants:black'], ['vest:black']),
    'Sniper':    ('marksman', ['collar:black', 'flakvest:black', 'pants:black', 'visor:arasaka'], ['jacket:black']),
    'Boss':      ('commander', ['collar:white', 'duster:black', 'pants:black'], ['visor:arasaka']),
    'Trader':    ('corpo', ['collar:white', 'jacket:black', 'pants:black'], []),
}
ARASAKA_OWN_ART = {
    'Guard':     ('security guard', ['arasaka_shirt_white:custom', 'arasaka_carrier:custom', 'arasaka_pants:custom', 'arasaka_helmet:custom'], []),
    'Grunt':     ('soldier', ['arasaka_recon:custom', 'arasaka_reconhelm:custom', 'arasaka_shirt:custom', 'arasaka_pants:custom'], []),
    'Enforcer':  ('heavy gunner', ['arasaka_heavy:custom', 'arasaka_heavyhelm:custom', 'arasaka_shirt:custom', 'arasaka_pants:custom'], []),
    'Netrunner': ('netrunner', ['arasaka_bodysuit:custom', 'arasaka_netrig:custom', 'arasaka_pants:custom'], ['arasaka_vest:custom']),
    'Sniper':    ('marksman', ['arasaka_shirt:custom', 'arasaka_carrier:custom', 'arasaka_pants:custom', 'arasaka_visor:custom'], ['arasaka_jacket:custom']),
    'Boss':      ('commander', ['arasaka_shirt_white:custom', 'arasaka_duster:custom', 'arasaka_pants:custom'], ['arasaka_visor:custom']),
    'Trader':    ('corpo', ['arasaka_shirt_white:custom', 'arasaka_jacket:custom', 'arasaka_pants:custom'], []),
}

# per faction: role -> (label, required pieces, optional extras). piece = 'template:colour' (two-colour masked pieces: 'template:colour/colour2').
# lore notes from the Cyberpunk 2077 / Cyberpunk RED setting, kept to what reads at RimWorld scale.
OUTFITS = {
    # Arasaka: black and red corporate security. Guards in shirts and vests, soldiers in full black tactical gear with red helmets,
    # netrunners in black suits with red rigs, execs in black coats over white shirts.
    'Arasaka': ARASAKA_OWN_ART,
    # Militech: US military contractor. Olive drab and khaki, plate carriers, heavy suits for the line.
    'Militech': {
        'Guard':     ('security guard', ['collar:khaki', 'flakvest:olive', 'pants:khaki', 'simplehelm:olive'], []),
        'Grunt':     ('soldier', ['collar:khaki', 'flakvest:olive', 'flakjacket:olive', 'flakpants:olive', 'advhelm:olive'], []),
        'Enforcer':  ('heavy gunner', ['marine:olive', 'marinehelm:olive', 'collar:khaki', 'pants:khaki'], []),
        'Netrunner': ('netrunner', ['bodysuit:charcoal', 'netrig:olive', 'pants:khaki', 'flakvest:olive'], []),
        'Sniper':    ('marksman', ['collar:khaki', 'parka:olive', 'pants:khaki', 'visor:olive'], []),
        'Boss':      ('commander', ['prestigeheavy:olive/khaki', 'marinehelm:khaki', 'collar:khaki', 'pants:khaki'], []),
        'Trader':    ('contractor', ['collar:white', 'jacket:olive', 'pants:khaki'], []),
    },
    # Kang Tao: Chinese smart-weapon maker. Black with gold trim.
    'KangTao': {
        'Guard':     ('security guard', ['collar:black', 'jacket:gold', 'pants:black'], ['visor:gold']),
        'Grunt':     ('soldier', ['recon:black', 'reconhelm:gold', 'collar:black', 'pants:black'], []),
        'Enforcer':  ('heavy gunner', ['marine:black', 'marinehelm:gold', 'collar:black', 'pants:black'], []),
        'Netrunner': ('netrunner', ['bodysuit:gold', 'netrig:black', 'pants:black'], []),
        'Sniper':    ('smart-gun marksman', ['collar:black', 'flakvest:black', 'pants:black', 'visor:gold'], []),
        'Boss':      ('commander', ['prestige:black/gold', 'collar:gold', 'pants:black'], ['visor:gold']),
        'Trader':    ('corpo', ['collar:gold', 'jacket:black', 'pants:black'], []),
    },
    # Biotechnica: agri-biotech. White lab coats over green, green-and-white security.
    'Biotechnica': {
        'Guard':     ('security guard', ['collar:white', 'flakvest:green', 'pants:white', 'simplehelm:green'], []),
        'Grunt':     ('soldier', ['recon:white', 'reconhelm:green', 'collar:green', 'pants:white'], []),
        'Enforcer':  ('heavy security', ['marine:white', 'marinehelm:green', 'collar:green', 'pants:white'], []),
        'Netrunner': ('netrunner', ['bodysuit:green', 'netrig:white', 'pants:white'], []),
        'Sniper':    ('marksman', ['collar:green', 'jacket:white', 'pants:white', 'visor:green'], []),
        'Boss':      ('director', ['collar:green', 'duster:white', 'pants:white'], ['visor:green']),
        'Trader':    ('researcher', ['collar:green', 'duster:white', 'pants:white'], []),
    },
    # Petrochem: energy and agriculture. Blue and grey workwear, field security in armoured jackets.
    'Petrochem': {
        'Guard':     ('security guard', ['collar:skyblue', 'flakvest:grey', 'pants:steel', 'simplehelm:blue'], []),
        'Grunt':     ('soldier', ['collar:steel', 'flakjacket:blue', 'flakpants:steel', 'advhelm:white'], []),
        'Enforcer':  ('heavy gunner', ['marine:blue', 'marinehelm:white', 'collar:steel', 'pants:steel'], []),
        'Netrunner': ('netrunner', ['bodysuit:blue', 'netrig:white', 'pants:steel'], []),
        'Boss':      ('executive', ['collar:white', 'duster:steel', 'pants:steel'], []),
        'Trader':    ('corpo', ['collar:white', 'jacket:blue', 'pants:steel'], []),
    },
    # Zetatech: cyberware and electronics. Purple, heavy on netrunners.
    'Zetatech': {
        'Guard':     ('security guard', ['collar:charcoal', 'jacket:purple', 'pants:charcoal'], ['visor:violet']),
        'Grunt':     ('soldier', ['recon:charcoal', 'reconhelm:violet', 'collar:charcoal', 'pants:charcoal'], []),
        'Enforcer':  ('heavy gunner', ['marine:charcoal', 'marinehelm:violet', 'collar:charcoal', 'pants:charcoal'], []),
        'Netrunner': ('netrunner', ['bodysuit:violet', 'netrig:charcoal', 'pants:charcoal', 'vest:purple'], []),
        'Sniper':    ('marksman', ['collar:charcoal', 'flakvest:purple', 'pants:charcoal', 'visor:violet'], []),
        'Boss':      ('executive', ['collar:violet', 'duster:charcoal', 'pants:charcoal'], ['visor:violet']),
        'Trader':    ('corpo', ['collar:violet', 'jacket:charcoal', 'pants:charcoal'], []),
    },
    # Night Corp: secretive, city infrastructure. Navy and black, clean suits, faceless soldiers.
    'NightCorp': {
        'Guard':     ('security guard', ['collar:black', 'jacket:navy', 'pants:black'], ['visor:skyblue']),
        'Grunt':     ('soldier', ['recon:navy', 'reconhelm:black', 'collar:black', 'pants:black'], []),
        'Enforcer':  ('enforcer', ['marine:navy', 'marinehelm:black', 'collar:black', 'pants:black'], []),
        'Netrunner': ('netrunner', ['bodysuit:navy', 'netrig:skyblue', 'pants:black'], []),
        'Sniper':    ('marksman', ['collar:black', 'flakvest:navy', 'pants:black', 'visor:skyblue'], []),
        'Boss':      ('executive', ['collar:white', 'duster:black', 'pants:black'], []),
        'Trader':    ('corpo', ['collar:white', 'jacket:navy', 'pants:black'], []),
    },
    # NetWatch: net police. Agents in long black coats and blue visors, plenty of netrunners.
    'NetWatch': {
        'Grunt':     ('agent', ['collar:black', 'jacket:blue', 'pants:black', 'visor:skyblue'], []),
        'Enforcer':  ('tactical agent', ['recon:steel', 'reconhelm:blue', 'collar:black', 'pants:black'], []),
        'Netrunner': ('netrunner', ['bodysuit:blue', 'netrig:skyblue', 'pants:black', 'vest:black'], []),
        'Boss':      ('director', ['collar:white', 'duster:black', 'pants:black', 'visor:blue'], []),
    },
    # Trauma Team: green shirt and pants under a white combat vest, armoured helmet. Own textures.
    'TraumaTeam': {
        'Operator':  ('operator', ['trauma_shirt:custom', 'trauma_pants:custom', 'trauma_vest:custom', 'trauma_helmet:custom'], []),
        'Boss':      ('team lead', ['trauma_shirt:custom', 'trauma_pants:custom', 'trauma_vest:custom', 'trauma_helmet:custom'], []),
    },
    # MaxTac: NCPD anti-cyberpsycho unit. Black heavy armour, sealed helmets. Own textures.
    'MaxTac': {
        'Operator':  ('officer', ['maxtac_armor:custom', 'maxtac_helmet:custom', 'bodysuit:black', 'pants:black'], []),
        'Boss':      ('sergeant', ['maxtac_armor:custom', 'maxtac_lead:custom', 'bodysuit:black', 'pants:black'], []),
    },
    # Debt collectors (Trauma Team's collection agency): grey suits over plate carriers, red visors. Repo men, not soldiers.
    'DebtCollectors': {
        'Grunt':     ('collector', ['collar:white', 'flakvest:charcoal', 'pants:charcoal', 'visor:red'], ['jacket:charcoal']),
        'Enforcer':  ('repo heavy', ['marine:charcoal', 'marinehelm:red', 'collar:white', 'pants:charcoal'], []),
        'Melee':     ('repo ripper', ['collar:white', 'flakjacket:charcoal', 'flakpants:charcoal', 'faceplate:chrome'], []),
        'Netrunner': ('skip tracer', ['bodysuit:charcoal', 'netrig:red', 'pants:charcoal'], ['vest:charcoal']),
        'Boss':      ('account manager', ['collar:white', 'duster:charcoal', 'pants:charcoal', 'visor:red'], []),
    },

    # Maelstrom: chrome-obsessed, black leather and red, faceplates and glowing optics.
    'Maelstrom': {
        'Grunt':     ('ganger', ['shirt:crimson', 'jacket:black', 'pants:black', 'faceplate:chrome'], []),
        'Enforcer':  ('enforcer', ['shirt:black', 'flakjacket:black', 'flakpants:black', 'faceplate:red'], []),
        'Melee':     ('brawler', ['shirt:black', 'pants:black', 'faceplate:chrome'], []),
        'Netrunner': ('netrunner', ['bodysuit:crimson', 'netrig:black', 'pants:black'], ['faceplate:chrome']),
        'Boss':      ('boss', ['collar:black', 'duster:black', 'pants:black', 'faceplate:red'], []),
    },
    # Tyger Claws: Japantown yakuza-adjacent street gang. Teal and pink neon jackets, katanas, oni masks.
    'TygerClaws': {
        'Grunt':     ('ganger', ['shirt:black', 'jacket:teal', 'pants:black'], ['mask:pink']),
        'Enforcer':  ('enforcer', ['shirt:black', 'flakvest:black', 'jacket:pink', 'pants:black'], []),
        'Melee':     ('swordsman', ['collar:black', 'pants:black', 'mask:teal'], ['vest:teal']),
        'Netrunner': ('netrunner', ['bodysuit:pink', 'netrig:teal', 'pants:black'], []),
        'Boss':      ('boss', ['collar:teal', 'duster:black', 'pants:black'], []),
    },
    # Valentinos: Heywood family gang. White shirts, gold vests, red jackets, hats.
    'Valentinos': {
        'Grunt':     ('ganger', ['collar:white', 'vest:gold', 'pants:black'], []),
        'Enforcer':  ('enforcer', ['shirt:white', 'jacket:red', 'pants:black'], ['bowler:black']),
        'Melee':     ('brawler', ['shirt:white', 'pants:black'], ['bowler:black']),
        'Boss':      ('boss', ['collar:white', 'vest:gold', 'jacket:crimson', 'pants:black', 'cowboy:white'], []),
    },
    # 6th Street: veterans run like an army unit. Navy, khaki and red, old military gear, berets.
    '6thStreet': None,  # alias filled below (faction id is SixthStreet)
    'SixthStreet': {
        'Grunt':     ('ganger', ['shirt:khaki', 'flakvest:navy', 'pants:khaki', 'simplehelm:navy'], []),
        'Enforcer':  ('veteran', ['collar:khaki', 'flakvest:navy', 'flakjacket:khaki', 'flakpants:khaki', 'advhelm:navy'], []),
        'Melee':     ('brawler', ['shirt:navy', 'pants:khaki', 'tuque:navy'], []),
        'Sniper':    ('marksman', ['collar:khaki', 'jacket:navy', 'pants:khaki', 'beret:red'], []),
        'Boss':      ('boss', ['collar:white', 'flakjacket:navy', 'flakpants:khaki', 'beret:red'], []),
    },
    # Animals: gym-built brawlers, barely dressed.
    'Animals': {
        'Grunt':     ('ganger', ['shirt:grey', 'pants:black'], []),
        'Enforcer':  ('bruiser', ['shirt:orange', 'pants:charcoal'], ['tuque:black']),
        'Boss':      ('alpha', ['shirt:black', 'jacket:brown', 'pants:black'], []),
    },
    # Voodoo Boys: Pacifica netrunners. Dark purple, hoods and robes.
    'VoodooBoys': {
        'Grunt':     ('ganger', ['collar:darkpurple', 'pants:black', 'hood:black'], []),
        'Enforcer':  ('enforcer', ['shirt:purple', 'jacket:black', 'pants:black'], ['hood:darkpurple']),
        'Melee':     ('brawler', ['shirt:purple', 'pants:black'], []),
        'Netrunner': ('netrunner', ['bodysuit:darkpurple', 'robe:black', 'netrig:violet', 'pants:black'], []),
        'Boss':      ('houngan', ['bodysuit:violet', 'robe:darkpurple', 'netrig:violet', 'pants:black'], []),
    },
    # Mox: protectors of Jig-Jig Street. Pink and purple punk.
    'Mox': {
        'Grunt':     ('ganger', ['shirt:black', 'jacket:pink', 'pants:black'], []),
        'Enforcer':  ('enforcer', ['shirt:pink', 'flakvest:black', 'jacket:purple', 'pants:black'], []),
        'Melee':     ('brawler', ['shirt:pink', 'pants:black'], []),
        'Netrunner': ('netrunner', ['bodysuit:purple', 'netrig:pink', 'pants:black'], []),
        'Boss':      ('boss', ['collar:black', 'duster:pink', 'pants:black'], []),
        'Trader':    ('fixer', ['collar:pink', 'jacket:black', 'pants:black'], []),
    },
    # Scavengers: organ harvesters in dirty scrap gear and surgical masks.
    'Scavengers': {
        'Grunt':     ('scav', ['shirt:grey', 'parka:charcoal', 'pants:grey', 'mask:grey'], []),
        'Enforcer':  ('scav enforcer', ['shirt:grey', 'flakvest:rust', 'jacket:grey', 'pants:grey', 'simplehelm:rust'], []),
        'Melee':     ('scav butcher', ['shirt:grey', 'pants:grey', 'mask:white'], []),
        'Netrunner': ('scav netrunner', ['bodysuit:grey', 'netrig:rust', 'pants:grey'], []),
        'Boss':      ('scav boss', ['collar:charcoal', 'duster:grey', 'pants:grey', 'visor:rust'], []),
    },
    # Wraiths: badlands raiders in sand-blasted scavenged gear and veils.
    'Wraiths': {
        'Grunt':     ('raider', ['shirt:brown', 'duster:sand', 'pants:brown', 'veil:brown'], []),
        'Enforcer':  ('raider enforcer', ['shirt:brown', 'flakvest:rust', 'parka:sand', 'pants:brown', 'simplehelm:sand'], []),
        'Melee':     ('raider brawler', ['shirt:brown', 'pants:brown', 'veil:sand'], []),
        'Boss':      ('raider boss', ['collar:brown', 'duster:brown', 'pants:brown', 'faceplate:sand'], []),
    },
    # Barghest: Kurt Hansen's paramilitary. Desert tan tactical gear, disciplined.
    'Barghest': {
        'Grunt':     ('soldier', ['recon:tan', 'reconhelm:tan', 'collar:khaki', 'pants:khaki'], []),
        'Enforcer':  ('heavy gunner', ['marine:tan', 'marinehelm:brown', 'collar:khaki', 'pants:khaki'], []),
        'Melee':     ('shock trooper', ['collar:khaki', 'flakvest:tan', 'flakpants:khaki', 'advhelm:tan'], []),
        'Netrunner': ('netrunner', ['bodysuit:tan', 'netrig:brown', 'pants:khaki'], ['flakvest:tan']),
        'Sniper':    ('marksman', ['collar:khaki', 'jacket:tan', 'pants:khaki', 'visor:brown'], []),
        'Boss':      ('colonel', ['prestige:brown/tan', 'reconhelm:brown', 'collar:khaki', 'pants:khaki'], []),
    },
    # Aldecaldos: nomads. Brown leather, orange, wide hats.
    'Aldecaldos': {
        'Grunt':     ('nomad', ['shirt:orange', 'jacket:leather', 'pants:brown'], ['cowboy:brown']),
        'Enforcer':  ('road warrior', ['collar:tan', 'flakvest:brown', 'jacket:leather', 'pants:brown'], []),
        'Sniper':    ('scout', ['collar:tan', 'duster:tan', 'pants:brown', 'cowboy:brown'], []),
        'Boss':      ('chief', ['collar:orange', 'duster:leather', 'pants:brown', 'cowboy:tan'], []),
        'Trader':    ('trader', ['collar:tan', 'jacket:brown', 'pants:brown'], ['cowboy:brown']),
    },
    # Raffen Shiv: nomads gone raider. Rust and black, masks.
    'RaffenShiv': {
        'Grunt':     ('raider', ['shirt:black', 'parka:rust', 'pants:black', 'faceplate:black'], []),
        'Enforcer':  ('raider enforcer', ['shirt:black', 'flakvest:rust', 'jacket:black', 'pants:black'], ['faceplate:black']),
        'Sniper':    ('raider marksman', ['collar:black', 'duster:black', 'pants:black', 'veil:rust'], []),
        'Boss':      ('raider boss', ['collar:black', 'duster:rust', 'pants:black', 'faceplate:red'], []),
        'Trader':    ('trader', ['collar:rust', 'jacket:black', 'pants:black'], []),
    },
}
del OUTFITS['6thStreet']

# readable colour names for labels
CNAME = {'arasaka': 'red', 'darkpurple': 'dark purple', 'skyblue': 'sky blue', 'custom': ''}


def ident(s): return ''.join(ch for ch in s.title() if ch.isalnum())


def piece_def(fid, piece):
    tmpl, colours = piece.split(':')
    c1, _, c2 = colours.partition('/')
    name = f'RPC_Apparel_{fid}_{ident(tmpl)}' + ('' if c1 == 'custom' else f'_{ident(c1)}') + (f'_{ident(c2)}' if c2 else '')
    return name, tmpl, c1, c2


def outfit(fid, role):
    """(label, required defNames, extra defNames) or None."""
    o = OUTFITS.get(fid, {}).get(role)
    if not o: return None
    return o[0], [piece_def(fid, p)[0] for p in o[1]], [piece_def(fid, p)[0] for p in o[2]]


def role_tag(fid, role): return f'RPC_Outfit_{fid}_{role}'


def rgb(c): return f'({C[c][0]},{C[c][1]},{C[c][2]})'


def fmt(x): return f'{x:g}'


def thing(fid, faction_label, name, tmpl, c1, c2, tags):
    t = T[tmpl]
    primary = FACTION_COLOURS.get(fid)
    colour_word = primary['name'] if primary else CNAME.get(c1, c1)
    label = f"{faction_label} {(colour_word + ' ') if colour_word and not t.get('custom') else ''}{t['label']}".replace('  ', ' ')
    if t.get('custom'): label = t['label']
    worn = f"\n      <wornGraphicPath>{t['worn']}</wornGraphicPath>" if t['worn'] else ''
    mask = '\n      <useWornGraphicMask>true</useWornGraphicMask>' if t.get('mask') else ''
    colour = '' if t.get('custom') else f"\n      <color>{rgb(c1)}</color>" + (f"\n      <colorTwo>{rgb(c2)}</colorTwo>" if c2 else '')
    if primary:
        tint = '(' + ','.join(str(v) for v in primary['rgb']) + ')'
        colour = f'\n      <color>{tint}</color>' + (f'\n      <colorTwo>{tint}</colorTwo>' if c2 or t.get('mask') else '')
    offsets = ''.join(f'\n      <{k}>{fmt(v)}</{k}>' for k, v in t.get('offsets', {}).items())
    offsets = f'\n    <equippedStatOffsets>{offsets}\n    </equippedStatOffsets>' if offsets else ''
    heat = f"\n      <ArmorRating_Heat>{fmt(t['heat'])}</ArmorRating_Heat>" if t.get('heat') else ''
    cost = ''.join(f'\n      <{k}>{v}</{k}>' for k, v in t['cost'].items())
    tech = 'Spacer' if t['sharp'] >= 12 or tmpl in ('bodysuit', 'netrig', 'visor', 'robe', 'vest', 'arasaka_bodysuit', 'arasaka_netrig', 'arasaka_visor', 'arasaka_vest') else 'Industrial'
    return f'''  <ThingDef ParentName="ApparelBase">
    <defName>{name}</defName>
    <label>{escape(label)}</label>
    <description>{escape(describe(fid, faction_label, tmpl, t))}</description>
    <techLevel>{tech}</techLevel>
    <graphicData>
      <texPath>{t['tex']}</texPath>
      <graphicClass>Graphic_Single</graphicClass>{colour}
    </graphicData>
    <thingCategories Inherit="False"><li>{t['cat']}</li></thingCategories>
    <tradeTags><li>RPC_FactionApparel</li></tradeTags>
    <statBases>
      <MaxHitPoints>{t['hp']}</MaxHitPoints>
      <MarketValue>{t['value']}</MarketValue>
      <Mass>{fmt(t['mass'])}</Mass>
      <Bulk>{fmt(t['bulk'])}</Bulk>
      <WornBulk>{fmt(t['wbulk'])}</WornBulk>
      <ArmorRating_Sharp>{fmt(t['sharp'])}</ArmorRating_Sharp>
      <ArmorRating_Blunt>{fmt(t['blunt'])}</ArmorRating_Blunt>{heat}
      <Insulation_Cold>{t['cold']}</Insulation_Cold>
      <Flammability>{'0.2' if t['sharp'] >= 8 else '0.8'}</Flammability>
      <EquipDelay>{'8' if t['bulk'] >= 50 else '2'}</EquipDelay>
    </statBases>{offsets}
    <costList>{cost}
    </costList>
    <generateCommonality>1</generateCommonality>
    <apparel>
      <bodyPartGroups>{''.join(f'<li>{g}</li>' for g in t['groups'])}</bodyPartGroups>{worn}{mask}
      <layers>{''.join(f'<li>{l}</li>' for l in t['layers'])}</layers>
      <tags>{''.join(f'<li>{x}</li>' for x in sorted(tags))}</tags>
      <canBeGeneratedToSatisfyWarmth>false</canBeGeneratedToSatisfyWarmth>
      <canBeGeneratedToSatisfyToxicEnvironmentResistance>false</canBeGeneratedToSatisfyToxicEnvironmentResistance>
    </apparel>
  </ThingDef>
'''


BLURB = {
    'recon': 'Full-body tactical armour.', 'marine': 'Heavy powered armour for the front line.', 'prestige': 'Officer-grade tactical armour.',
    'prestigeheavy': 'Officer-grade heavy armour.', 'flakvest': 'A plate carrier.', 'flakjacket': 'An armoured jacket.', 'flakpants': 'Armoured trousers.',
    'netrig': 'A skull-mounted interface rig for jacking into the Net.', 'visor': 'A HUD visor.', 'bodysuit': 'A thin, close-fitting synthweave suit.',
    'faceplate': 'A metal faceplate.', 'reconhelm': 'A sealed tactical helmet.', 'marinehelm': 'A sealed heavy helmet.', 'advhelm': 'A modern combat helmet.',
    'simplehelm': 'A plain steel helmet.',
}


def describe(fid, flabel, tmpl, t):
    if fid == 'TraumaTeam':
        return 'Trauma Team International issue: armoured, sealed, and white so you know who just saved your life.'
    if fid == 'Arasaka':
        return 'Arasaka corporate security issue in Arasaka red.'
    if fid == 'MaxTac':
        return 'NCPD MaxTac issue. Built to walk through cyberpsycho fire and put them down.'
    return f"{BLURB.get(tmpl, 'Clothing.')} Worn by {flabel} in their colours."


HEAD = {'FullHead': {'FullHead', 'UpperHead', 'Eyes', 'Mouth'}, 'UpperHead': {'UpperHead', 'FullHead'}, 'Mouth': {'Mouth', 'FullHead'}}


def overlaps(a, b):
    ta, tb = T[a], T[b]
    if not set(ta['layers']) & set(tb['layers']): return False
    ga = set().union(*(HEAD.get(g, {g}) for g in ta['groups']))
    return bool(ga & set(tb['groups']))


def faction_labels():
    import xml.etree.ElementTree as ET
    path = os.path.join(ROOT, 'Common/Defs/Factions/Generated_Factions.xml')
    if not os.path.exists(path): return {}
    return {d.findtext('defName')[len('RPC_Faction_'):]: d.findtext('label') for d in ET.parse(path).getroot().findall('FactionDef')}


def main():
    labels = faction_labels()
    for fid, roles in OUTFITS.items():
        for role, (_, req, extra) in roles.items():
            tm = [p.split(':')[0] for p in req]
            for i in range(len(tm)):
                for j in range(i + 1, len(tm)):
                    assert not overlaps(tm[i], tm[j]), f'{fid} {role}: {tm[i]} overlaps {tm[j]}'
    defs = {}
    for fid, roles in OUTFITS.items():
        for role, (label, req, extra) in roles.items():
            for i, piece in enumerate(req + extra):
                name, tmpl, c1, c2 = piece_def(fid, piece)
                assert tmpl in T, (fid, role, tmpl)
                assert c1 in C and (not c2 or c2 in C), (fid, role, piece)
                d = defs.setdefault(name, dict(fid=fid, tmpl=tmpl, c1=c1, c2=c2, tags=set()))
                if i >= len(req): d['tags'].add(role_tag(fid, role))
    out = [thing(d['fid'], labels.get(d['fid'], d['fid']), n, d['tmpl'], d['c1'], d['c2'], d['tags'] or {'RPC_FactionApparel'})
           for n, d in sorted(defs.items())]
    path = os.path.join(ROOT, 'Common/Defs/Apparel/Generated_Apparel.xml')
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('<?xml version="1.0" encoding="utf-8"?>\n<!-- GENERATED by Source/Tools/gen_apparel.py. Do not edit; change the generator and rerun. -->\n<Defs>\n\n')
        fh.write('\n'.join(out))
        fh.write('\n</Defs>\n')
    print(f'{len(defs)} apparel defs for {len(OUTFITS)} factions')


if __name__ == '__main__':
    main()
