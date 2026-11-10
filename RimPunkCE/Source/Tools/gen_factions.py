"""
Generates the RimPunk CE factions (prompts/factions.md): FactionDefs, PawnKindDefs and corporate TraderKindDefs.

    python -I Source/Tools/gen_factions.py        (run from the RimPunkCE folder)

Output (overwritten every run, do not hand-edit): Common/Defs/Factions/Generated_Factions.xml
Hand-written pieces (pawn kind base, backstories, street names, Trauma Team, incidents) are in Factions_Bases.xml.
"""
import os
import sys
from xml.sax.saxutils import escape

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gen_apparel import outfit, role_tag  # faction uniforms (run gen_apparel.py too)

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# cyberware pools (defNames). Every option is filtered by the pawn kind's tier range when written (loadout_xml):
# an item whose minimum tier is above the kind's max tier is dropped, so gangs don't roll T5-only chrome.
SANDEVISTANS = ['RPC_DynalarSandevistan', 'RPC_ZetatechSandevistan', 'RPC_WarpDancerSandevistan', 'RPC_FalconSandevistan', 'RPC_ApogeeSandevistan']
BERSERKS = ['RPC_MooreTechBerserk', 'RPC_BioDyneBerserk', 'RPC_ZetatechBerserk', 'RPC_MilitechBerserk']
DECKS = ['RPC_ArasakaCyberdeck', 'RPC_ParalineCyberdeck', 'RPC_RipplerCyberdeck', 'RPC_BiotechCyberdeck', 'RPC_RavenCyberdeck', 'RPC_NetDriverCyberdeck', 'RPC_CantoCyberdeck']
CHROME = ['RPC_ChromeCompressor']
# an operating-system roll picks one of these families with equal odds, then a model inside it (so adding models never shifts the odds);
# cyberdecks drop out at runtime in worlds without quickhacks (PawnLoadoutUtility.Allowed)
OS_ALL = SANDEVISTANS + BERSERKS + DECKS + CHROME
OS_NODECK = SANDEVISTANS + BERSERKS + CHROME
ARMS = ['RPC_GorillaArms', 'RPC_MantisBlades', 'RPC_ElectrifyingGorillaArms', 'RPC_ThermalGorillaArms', 'RPC_ToxicGorillaArms',
        'RPC_ElectrifyingMantisBlades', 'RPC_ThermalMantisBlades', 'RPC_ToxicMantisBlades', 'RPC_Monowire', 'RPC_ElectrifyingMonowire',
        'RPC_ThermalMonowire', 'RPC_ToxicMonowire', 'RPC_ProjectileLaunchSystem']
ARM_FAMILIES = [ARMS[0:1] + ARMS[2:5], ARMS[1:2] + ARMS[5:8], ARMS[8:12], ARMS[12:13]]  # gorilla, mantis, monowire, launcher
PFC = ['RPC_NewtonModule', 'RPC_RamUpgrade', 'RPC_MechatronicCore', 'RPC_MemoryBoost', 'RPC_Bioconductor', 'RPC_ExDisk', 'RPC_Axolotl', 'RPC_SelfIce']
FACE = ['RPC_BasicKiroshiOptics', 'RPC_KiroshiStalkerOptics', 'RPC_KiroshiSentryOptics', 'RPC_KiroshiCockatriceOptics']
HANDS = ['RPC_SmartLink', 'RPC_ShockAbsorber', 'RPC_ImmovableForce', 'RPC_Microgenerator']
LEGS = ['RPC_LeeroyLigamentSystem', 'RPC_JenkinsTendons', 'RPC_FortifiedAnkles']
SKELETON = ['RPC_BionicJoints', 'RPC_DenseMarrow', 'RPC_TitaniumBones', 'RPC_ParaBellum', 'RPC_SpringJoints', 'RPC_EpimorphicSkeleton']
NERVOUS = ['RPC_AdrenalineConverter', 'RPC_ReflexTuner', 'RPC_Kerenzikov', 'RPC_NeoFiber', 'RPC_TyrosineInjector', 'RPC_SynapticAccelerator']
CIRCULATORY = ['RPC_HealOnKill', 'RPC_Microrotors', 'RPC_ClutchPadding', 'RPC_BloodPump', 'RPC_AdrenalineBooster', 'RPC_SecondHeart']
SKIN = ['RPC_SubdermalArmor', 'RPC_ProxiShield', 'RPC_RangeGuard', 'RPC_Carapace', 'RPC_OpticalCamo', 'RPC_PainEditor']

import xml.etree.ElementTree as _ETc

def _cyberware_min_tiers():
    """defName -> minTier, read from every cyberware XML (hand-written and generated)."""
    tiers = {}
    folder = os.path.join(ROOT, 'Common', 'Defs', 'Cyberware')
    for fn in os.listdir(folder):
        if not fn.endswith('.xml'): continue
        for d in _ETc.parse(os.path.join(folder, fn)).getroot().findall('HediffDef'):
            name = d.findtext('defName')
            ext = d.find("modExtensions/li[@Class='RimPunkCE.CyberwareExtension']")
            if name and ext is not None: tiers[name] = ext.findtext('minTier') or 'T1'
    return tiers

CW_MIN_TIER = _cyberware_min_tiers()
TIER_ORDER = ['T1', 'T2', 'T3', 'T4', 'T5', 'Iconic']

def opt(chance, hediffs, count=1): return dict(chance=chance, hediffs=hediffs, count=count)

# flat pool -> its families, for options that should roll family-first
FAMILIES = {tuple(OS_ALL): [SANDEVISTANS, BERSERKS, DECKS, CHROME], tuple(OS_NODECK): [SANDEVISTANS, BERSERKS, CHROME],
            tuple(SANDEVISTANS + BERSERKS): [SANDEVISTANS, BERSERKS], tuple(ARMS): ARM_FAMILIES}

LOADOUT_CORP = [opt(0.5, OS_ALL), opt(0.3, ARMS), opt(0.4, PFC, 2), opt(0.6, ['RPC_SmartLink']), opt(0.5, FACE), opt(0.5, SKELETON),
                opt(0.4, NERVOUS, 2), opt(0.4, CIRCULATORY, 2), opt(0.5, SKIN, 2), opt(0.3, LEGS, 2), opt(0.3, HANDS)]
LOADOUT_GANG = [opt(0.2, ['RPC_MooreTechBerserk', 'RPC_DynalarSandevistan', 'RPC_ZetatechSandevistan', 'RPC_BioDyneBerserk']), opt(0.3, ARMS),
                opt(0.2, ['RPC_NewtonModule', 'RPC_MechatronicCore']), opt(0.3, ['RPC_SmartImprint']), opt(0.3, FACE), opt(0.3, SKELETON),
                opt(0.25, NERVOUS), opt(0.25, CIRCULATORY), opt(0.35, SKIN), opt(0.15, LEGS, 2)]

# kind: corp / gang / nomads / trauma / netwatch
# relation: permanent (always hostile), hostile (starts hostile, peace possible), neutral
F = [
    dict(id='Arasaka', label='Arasaka', kind='corp', color=(0.7, 0.1, 0.1), manus=['Arasaka', 'Tsunami'], rivals=['Militech'],
         desc='A Japanese megacorporation: security, banking and military hardware. Its private army is one of the best equipped in the world.'),
    dict(id='Militech', label='Militech', kind='corp', color=(0.35, 0.45, 0.2), manus=['Militech', 'ConstitutionalArms'], rivals=['Arasaka'],
         desc='The largest arms manufacturer in the world, and Arasaka\'s fiercest rival. Militech fields soldiers, drones and heavy weapons.'),
    dict(id='KangTao', label='Kang Tao', kind='corp', color=(0.85, 0.65, 0.1), manus=['KangTao'], classTags=['RPC_Type_smart'],
         desc='A fast-growing Chinese arms maker known for smart weapons.'),
    dict(id='Biotechnica', label='Biotechnica', kind='corp', color=(0.3, 0.75, 0.35), manus=['MidnightArms', 'DarraPolytechnic'],
         desc='A biotech giant that controls much of the world\'s food supply.'),
    dict(id='Petrochem', label='Petrochem', kind='corp', color=(0.25, 0.55, 0.8), manus=['Nokota', 'ConstitutionalArms'],
         desc='An energy and agriculture corporation with private security to protect its fields and refineries.'),
    dict(id='Zetatech', label='Zetatech', kind='corp', color=(0.55, 0.3, 0.8), manus=['DarraPolytechnic', 'Tsunami'],
         desc='A cyberware and electronics corporation.'),
    dict(id='NightCorp', label='Night Corp', kind='corp', color=(0.15, 0.15, 0.3), manus=['Arasaka', 'Tsunami'],
         desc='A secretive corporation with a hand in city infrastructure.'),
    dict(id='NetWatch', label='NetWatch', kind='netwatch', color=(0.2, 0.6, 0.9), manus=['Militech', 'KangTao'], classTags=['RPC_Type_smart'],
         desc='The net police. NetWatch hunts rogue AIs and netrunners who push too far.'),
    # Trauma Team: Kang Tao smart guns (Chao, Dian, Zhuo) plus the Nokota smart rifle (Kang Tao makes no rifle), and full
    # combat chrome: no cyberdecks, no netrunning PFC chips, no arm cyberware.
    dict(id='TraumaTeam', label='Trauma Team International', kind='trauma', color=(0.95, 0.95, 0.95), manus=['KangTao'],
         weapons=['RPC_W_KangTao_SMG', 'RPC_W_KangTao_Shotgun', 'RPC_W_KangTao_Pistol', 'RPC_WT_Rifle_smart'], tiers=('T4', 'Iconic'),
         loadout=[opt(1.0, SANDEVISTANS + BERSERKS), opt(1.0, ['RPC_SmartLink']), opt(1.0, FACE), opt(1.0, SKELETON, 2), opt(1.0, NERVOUS, 2),
                  opt(1.0, CIRCULATORY, 2), opt(1.0, SKIN, 2), opt(0.8, LEGS, 2), opt(0.8, ['RPC_NewtonModule', 'RPC_MechatronicCore', 'RPC_Bioconductor', 'RPC_Axolotl'], 2),
                  opt(0.5, ['RPC_ShockAbsorber', 'RPC_ImmovableForce', 'RPC_Microgenerator'])],
         desc='A paramedic company with an armed response. Pay for coverage and a Trauma Team squad will come for you when you go down.'),
    # MaxTac: NCPD's anti-cyberpsycho unit. never settles, only arrives by shuttle to put down a cyberpsycho (5 kills).
    # top tier chrome on every member, immune to cyberpsychosis (CyberpsychosisUtility.IsImmune).
    # armour-killers: only the high-penetration gun classes, always loaded with AP / charged rounds
    dict(id='MaxTac', label='MaxTac', kind='maxtac', color=(0.1, 0.2, 0.55), manus=['Militech'], tiers=('Iconic', 'Iconic'),
         roleClasses={'Operator': ['Rifle', 'MG', 'Precision'], 'Boss': ['Rifle', 'Precision']},
         ammo={'ArmorPiercing': 1, 'Charged': 1},
         loadout=[opt(1.0, ['RPC_DynalarSandevistan', 'RPC_MooreTechBerserk']), opt(1.0, ARMS, 2), opt(1.0, PFC, 3), opt(1.0, ['RPC_SmartLink'])],
         desc='NCPD Maximum Force Tactical Division. When a cyberpsycho starts piling up bodies, MaxTac drops in to put them down. They are chromed to the limit and immune to cyberpsychosis.'),
    # Debt collectors: the agency Trauma Team sells unpaid bills to. never settles, never visits; only raids a colony
    # whose Trauma Team debt has gone unpaid for 30 days (GameComponent_RimPunk.TraumaDebt). mid tier chrome.
    dict(id='DebtCollectors', label='Militech Receivables', kind='collector', color=(0.45, 0.45, 0.5), manus=['Militech', 'ConstitutionalArms', 'Tsunami'],
         tiers=('T2', 'T3'), bossTiers=('T3', 'T4'), noPeaceful=True, hidden=True,
         loadout=[opt(0.7, SANDEVISTANS + BERSERKS), opt(0.6, ARMS), opt(0.6, PFC, 2), opt(0.5, ['RPC_SmartLink', 'RPC_SmartImprint']),
                  opt(0.5, SKIN), opt(0.4, NERVOUS), opt(0.3, LEGS)],
         desc='A collection agency that buys unpaid medical debt from Trauma Team. When the bill comes due, they come to collect: in silver, in chrome, or in blood.'),

    dict(id='Maelstrom', label='Maelstrom', kind='gang', relation='permanent', color=(0.8, 0.1, 0.15), manus=['BudgetArms', 'Rostovic', 'Techtronika'],
         loadout=[opt(0.8, ['RPC_MooreTechBerserk', 'RPC_DynalarSandevistan']), opt(0.9, ARMS, 2), opt(0.7, PFC, 3), opt(0.7, ['RPC_SmartImprint'])],
         tiers=('T1', 'T3'), desc='A gang obsessed with chrome, more machine than human. Most of them live one bad day away from cyberpsychosis.'),
    dict(id='TygerClaws', label='Tyger Claws', kind='gang', relation='hostile', color=(0.2, 0.75, 0.75), manus=['Tsunami', 'BudgetArms'], meleeTags=['RPC_MeleeClass_Katana'],
         loadout=[opt(0.6, ['RPC_DynalarSandevistan']), opt(0.3, ['RPC_MantisBlades']), opt(0.3, ['RPC_SmartImprint'])],
         desc='A gang with deep roots in organised crime. They favour katanas and sandevistans.'),
    dict(id='Valentinos', label='Valentinos', kind='gang', relation='hostile', color=(0.85, 0.6, 0.1), classTags=['RPC_Class_Pistol', 'RPC_Class_Revolver'],
         rivals=['SixthStreet'], loadout=[opt(0.2, ['RPC_DynalarSandevistan']), opt(0.5, ['RPC_SmartImprint'])],
         desc='A neighbourhood gang bound by family, honour and gold. Pistols and revolvers.'),
    dict(id='SixthStreet', label='6th Street', kind='gang', relation='hostile', color=(0.3, 0.35, 0.55), manus=['Militech', 'ConstitutionalArms', 'MidnightArms'],
         classTags=['RPC_Class_Rifle', 'RPC_Class_Precision'], rivals=['Valentinos'], loadout=[opt(0.2, ['RPC_NewtonModule']), opt(0.4, ['RPC_SmartImprint'])],
         desc='Veterans turned gangers who run their turf like an army unit. Power rifles, little chrome.'),
    dict(id='Animals', label='Animals', kind='gang', relation='permanent', color=(0.55, 0.35, 0.25), meleeOnly=True,
         meleeTags=['RPC_MeleeClass_Club', 'RPC_MeleeClass_HeavyClub', 'RPC_MeleeClass_Hammer'],
         loadout=[opt(0.7, ['RPC_MooreTechBerserk']), opt(0.9, ['RPC_GorillaArms'], 2)],
         desc='Bodybuilders on combat stims and muscle grafts. They fight with fists, gorilla arms and berserk, never with guns.'),
    dict(id='VoodooBoys', label='Voodoo Boys', kind='gang', relation='hostile', color=(0.4, 0.2, 0.6), manus=['BudgetArms'], classTags=['RPC_Type_smart'],
         loadout=[opt(0.9, ['RPC_ArasakaCyberdeck']), opt(0.7, PFC, 2), opt(0.4, ['RPC_SmartImprint'])],
         desc='A gang of netrunners. Expect cyberdecks and quickhacks.'),
    dict(id='Mox', label='Mox', kind='gang', relation='neutral', color=(0.9, 0.4, 0.7), manus=['BudgetArms', 'DarraPolytechnic'],
         loadout=[opt(0.15, ARMS), opt(0.3, ['RPC_SmartImprint'])], trades=True,
         desc='A gang that protects the people nobody else will. They trade and keep to themselves unless provoked.'),
    dict(id='Scavengers', label='Scavengers', kind='gang', relation='permanent', color=(0.4, 0.4, 0.4), manus=['BudgetArms', 'Rostovic'],
         loadout=[opt(0.15, ARMS), opt(0.1, PFC)], desc='Scavs: organ harvesters and cyberware thieves who run on scrap gear.'),
    dict(id='Wraiths', label='Wraiths', kind='gang', relation='permanent', color=(0.6, 0.5, 0.3), manus=['Rostovic', 'Techtronika'],
         loadout=[opt(0.15, ARMS), opt(0.1, PFC)], desc='Badlands raiders in patched-together gear.'),
    dict(id='Barghest', label='Barghest', kind='gang', relation='hostile', color=(0.35, 0.3, 0.25), manus=['Militech', 'Rostovic', 'Techtronika'],
         tiers=('T2', 'T3'), loadout=[opt(0.4, OS_ALL), opt(0.4, ARMS), opt(0.4, PFC, 2), opt(0.6, ['RPC_SmartLink'])], techLevel='Spacer',
         desc='A paramilitary force with military-grade gear and discipline to match.'),
    dict(id='Aldecaldos', label='Aldecaldos', kind='nomads', relation='neutral', color=(0.8, 0.55, 0.25), manus=['Rostovic', 'Techtronika', 'Nokota'],
         rivals=['RaffenShiv'], trades=True, loadout=[opt(0.15, ARMS), opt(0.2, ['RPC_SmartImprint'])],
         desc='A nomad clan: family, the road and the deal, in that order.'),
    dict(id='RaffenShiv', label='Raffen Shiv', kind='nomads', relation='neutral', color=(0.6, 0.25, 0.2), manus=['Rostovic', 'BudgetArms'],
         rivals=['Aldecaldos'], trades=True, loadout=[opt(0.25, ARMS), opt(0.15, PFC)],
         desc='A nomad clan that turned raider. They trade when it suits them and fight when it doesn\'t.'),
]
IDS = {f['id'] for f in F}
for f in F:
    for r in f.get('rivals', []): assert r in IDS, r

CULTURE = {'corp': 'Astropolitan', 'collector': 'Astropolitan', 'netwatch': 'Astropolitan', 'trauma': 'Astropolitan', 'maxtac': 'Astropolitan', 'gang': 'Kriminul', 'nomads': 'Rustican'}
EXT_KIND = {'corp': 'Corporation', 'collector': 'DebtCollector', 'gang': 'Gang', 'nomads': 'Nomads', 'trauma': 'TraumaTeam', 'netwatch': 'NetWatch', 'maxtac': 'MaxTac'}
BACKSTORY = {'corp': [('RPC_Corpo', 0.75), ('Offworld', 0.25)], 'collector': [('RPC_Corpo', 0.5), ('RPC_StreetKid', 0.5)], 'netwatch': [('RPC_Corpo', 0.8), ('Offworld', 0.2)], 'trauma': [('RPC_Corpo', 0.8), ('Offworld', 0.2)], 'maxtac': [('RPC_Corpo', 0.8), ('Offworld', 0.2)],
             'gang': [('RPC_StreetKid', 0.75), ('Outlander', 0.25)], 'nomads': [('RPC_Nomad', 0.75), ('Outlander', 0.25)]}
ICON = {'corp': 'World/WorldObjects/Expanding/Town', 'collector': 'World/WorldObjects/Expanding/Town', 'netwatch': 'World/WorldObjects/Expanding/Town', 'trauma': 'World/WorldObjects/Expanding/Town', 'maxtac': 'World/WorldObjects/Expanding/Town',
        'gang': 'World/WorldObjects/Expanding/PirateOutpost', 'nomads': 'World/WorldObjects/Expanding/TownRough'}

# corporate-style factions: spacer tech, better gear and pay, 'operatives', canSiege
CORPLIKE = ('corp', 'netwatch', 'trauma', 'maxtac', 'collector')

def li(items): return ''.join(f'<li>{escape(x)}</li>' for x in items)

def loadout_xml(options, tiers):
    s = f'''      <li Class="RimPunkCE.CyberwareLoadoutExtension">
        <minTier>{tiers[0]}</minTier>
        <maxTier>{tiers[1]}</maxTier>
        <options>
'''
    top = TIER_ORDER.index(tiers[1])
    for o in options:
        missing = [h for h in o['hediffs'] if h not in CW_MIN_TIER]
        assert not missing, f'unknown cyberware in loadout: {missing}'
        hediffs = [h for h in o['hediffs'] if TIER_ORDER.index(CW_MIN_TIER[h]) <= top]
        if not hediffs: continue
        fams = FAMILIES.get(tuple(o['hediffs']))
        if fams:
            groups = ''.join(f'<li><hediffs>{li(g)}</hediffs></li>' for g in ([h for h in fam if h in hediffs] for fam in fams) if g)
            s += f"          <li><chance>{o['chance']}</chance><count>{o['count']}</count><groups>{groups}</groups></li>\n"
        else:
            s += f"          <li><chance>{o['chance']}</chance><count>{o['count']}</count><hediffs>{li(hediffs)}</hediffs></li>\n"
    return s + '''        </options>
      </li>
'''

import re as _re
import xml.etree.ElementTree as _ET

def _weapon_prices():
    """weaponTag -> list of market values, read from the generated weapon defs (run gen_weapons.py first)."""
    prices = {}
    base = os.path.join(ROOT, 'Common/Defs/Weapons')
    for fn, default in (('Generated_Guns.xml', 600), ('Generated_Melee.xml', 250)):
        root = _ET.parse(os.path.join(base, fn)).getroot()
        for d in root.findall('ThingDef'):
            mv = d.findtext('statBases/MarketValue')
            value = float(mv) if mv else default
            for t in d.findall('weaponTags/li'):
                prices.setdefault(t.text, []).append(value)
    return prices

PRICES = _weapon_prices()

def cheapest(tags):
    vals = [v for t in tags for v in PRICES.get(t, [])]
    return min(vals) if vals else 0

# role -> gun classes, combat power (before the corp multiplier), magazines, raid weight. Which roles a faction has comes
# from its outfit table in gen_apparel.py (OUTFITS); defNames stay RPC_<Faction>_<Role> so old saves keep their pawns.
ROLES = {
    'Grunt':     dict(classes=['Rifle', 'SMG', 'Shotgun'], power=70, mags=(3, 5), weight=10),
    'Guard':     dict(classes=['Pistol', 'SMG'], power=60, mags=(3, 5), weight=6),
    'Enforcer':  dict(classes=['MG', 'Shotgun', 'Rifle'], power=110, mags=(4, 7), weight=4),
    'Netrunner': dict(classes=['Pistol', 'SMG'], power=100, mags=(2, 4), weight=3),
    'Sniper':    dict(classes=['Sniper', 'Precision'], power=90, mags=(3, 5), weight=2),
    'Melee':     dict(classes=[], power=80, mags=(0, 0), weight=5),
    'Boss':      dict(classes=['Rifle', 'SMG', 'Pistol', 'Revolver'], power=170, mags=(5, 8), weight=1.5),
    'Operator':  dict(classes=['Rifle', 'SMG', 'Shotgun', 'MG'], power=150, mags=(4, 7), weight=10),
    'Trader':    dict(classes=['Pistol', 'Revolver'], power=40, mags=(1, 2), weight=0),
}
ROLE_ORDER = ['Grunt', 'Operator', 'Guard', 'Enforcer', 'Netrunner', 'Sniper', 'Melee', 'Boss', 'Trader']
NETRUNNER_LOADOUT = [opt(1.0, DECKS), opt(0.6, PFC, 2), opt(0.5, FACE), opt(0.4, SKIN), opt(0.3, NERVOUS)]


def role_weapons(f, role):
    """Weapon tags for a role: the faction's manufacturers in the role's gun classes (RPC_W_<Manu>_<Class>), else the classes."""
    if f.get('meleeOnly') or role == 'Melee': return f.get('meleeTags', ['RPC_Melee'])
    if f.get('weapons'): return f['weapons']
    classes = f.get('roleClasses', {}).get(role, ROLES[role]['classes'])
    allowed = [t[len('RPC_Class_'):] for t in f.get('classTags', []) if t.startswith('RPC_Class_')]
    if allowed: classes = [c for c in classes if c in allowed] or allowed
    combos = [f'RPC_W_{m}_{c}' for m in f.get('manus', []) for c in classes if PRICES.get(f'RPC_W_{m}_{c}')]
    return combos or [f'RPC_Class_{c}' for c in classes]


def pawnkind(f, role, label, power, weapon_tags, money, mags, leader=False, trader=False):
    corp = f['kind'] in CORPLIKE
    tiers = f.get('tiers', ('T3', 'Iconic') if corp else ('T1', 'T2'))
    if role == 'Boss': tiers = f.get('bossTiers') or (tiers[0], 'Iconic' if corp else ('T3' if tiers[1] in ('T2', 'T3') else tiers[1]))
    options = f.get('loadout', LOADOUT_CORP if corp else LOADOUT_GANG)
    if role == 'Netrunner' and 'loadout' not in f: options = NETRUNNER_LOADOUT
    elif 'loadout' not in f: options = [opt(o['chance'], OS_NODECK, o['count']) if o['hediffs'] == OS_ALL else o for o in options]
    if trader: options = [o for o in options if o['hediffs'] != ARMS][:2]
    _, required, _ = outfit(f['id'], role)
    floor = int(cheapest(weapon_tags) * 1.1) + 1
    money = (max(money[0], floor), max(money[1], floor * 2))
    # CE LoadoutPropertiesExtension: load the gun with (and carry spares of) these ammo categories where the gun has them
    ammo_xml = ('\n        <weightedAmmoCategories>' +''.join(f'<{c}>{w}</{c}>' for c, w in f['ammo'].items()) + '</weightedAmmoCategories>') if f.get('ammo') else ''
    mag_xml = f'''      <li Class="CombatExtended.LoadoutPropertiesExtension">
        <primaryMagazineCount><min>{mags[0]}</min><max>{mags[1]}</max></primaryMagazineCount>{ammo_xml}
      </li>
''' if not (f.get('meleeOnly') or role == 'Melee') else ''
    return f'''  <PawnKindDef ParentName="RPC_PawnKindBase">
    <defName>RPC_{f['id']}_{role}</defName>
    <label>{escape(f['label'].lower())} {escape(label)}</label>
    <defaultFactionDef>RPC_Faction_{f['id']}</defaultFactionDef>
    <combatPower>{power}</combatPower>
    <isFighter>{'false' if trader else 'true'}</isFighter>
    <itemQuality>{'Good' if corp else 'Normal'}</itemQuality>
    <apparelRequired>{li(required)}</apparelRequired>
    <apparelTags>{li([role_tag(f['id'], role)])}</apparelTags>
    <apparelMoney>{money[0]}~{money[1]}</apparelMoney>
    <weaponMoney>{money[0]}~{money[1] * 2}</weaponMoney>
    <weaponTags>{li(weapon_tags)}</weaponTags>{'''
    <factionLeader>true</factionLeader>''' if leader else ''}{'''
    <trader>true</trader>''' if trader else ''}
    <modExtensions>
{loadout_xml(options, tiers)}{mag_xml}    </modExtensions>
  </PawnKindDef>
'''

def trader_kind(f, orbital):
    pre = 'Orbital' if orbital else 'Caravan'
    manus = f.get('manus', [])
    stock = ''.join(f'''      <li Class="StockGenerator_Tag">
        <tradeTag>RPC_Manu_{m}</tradeTag>
        <thingDefCountRange>2~4</thingDefCountRange>
        <countRange>1~2</countRange>
      </li>
''' for m in manus)
    return f'''  <TraderKindDef>
    <defName>RPC_{pre}_{f['id']}</defName>
    <label>{escape(f['label'])} {'orbital trader' if orbital else 'caravan'}</label>{'''
    <orbital>true</orbital>''' if orbital else ''}
    <faction>RPC_Faction_{f['id']}</faction>
    <commonality>{0.4 if orbital else 1}</commonality>
    <stockGenerators>
      <li Class="StockGenerator_SingleDef"><thingDef>Silver</thingDef><countRange>1500~3000</countRange></li>
      <li Class="StockGenerator_SingleDef"><thingDef>ComponentIndustrial</thingDef><countRange>5~15</countRange></li>
      <li Class="StockGenerator_SingleDef"><thingDef>MedicineIndustrial</thingDef><countRange>5~15</countRange></li>
{stock}      <li Class="StockGenerator_Tag">
        <tradeTag>RPC_Ammo</tradeTag>
        <thingDefCountRange>3~6</thingDefCountRange>
        <countRange>100~300</countRange>
      </li>
      <li Class="RimPunkCE.StockGenerator_Cyberware">
        <countRange>2~5</countRange>
        <minTier>T3</minTier>
        <maxTier>Iconic</maxTier>
      </li>
      <li Class="StockGenerator_BuyExpensiveSimple" />
      <li Class="StockGenerator_BuyTradeTag"><tag>RPC_Weapon</tag></li>
      <li Class="StockGenerator_BuyTradeTag"><tag>RPC_Cyberware</tag></li>
    </stockGenerators>
  </TraderKindDef>
'''

out = []
for i, f in enumerate(F):
    kind = f['kind']
    corp = kind in CORPLIKE
    tech = f.get('techLevel', 'Spacer' if corp else 'Industrial')
    money = (1200, 2500) if corp else (300, 900)
    p = 1.4 if corp else 1.0
    trades = kind in ('corp', 'nomads') or f.get('trades')

    # (role, label, power, mags, leader, trader, weight)
    roles = []
    for role in ROLE_ORDER:
        o = outfit(f['id'], role)
        if not o or (role == 'Trader' and not trades): continue
        spec = ROLES[role]
        power = {'trauma': {'Operator': 150, 'Boss': 200}, 'maxtac': {'Operator': 250, 'Boss': 300}}.get(kind, {}).get(role, int(spec['power'] * p))
        mags = (6, 9) if kind == 'maxtac' else spec['mags']
        roles.append((role, o[0], power, mags, role == 'Boss', role == 'Trader', spec['weight']))
    assert roles and roles[0][0] in ('Grunt', 'Operator'), f['id']
    assert not trades or any(r[5] for r in roles), f"{f['id']} trades but has no Trader outfit"
    if kind == 'maxtac': money = (3000, 6000)

    for role, label, power, mags, leader, trader, _ in roles:
        out.append(pawnkind(f, role, label, power, role_weapons(f, role), money if role != 'Boss' else (money[0] * 2, money[1] * 2), mags, leader, trader))

    fighters = [r for r in roles if not r[5]]
    combat = ''.join(f'          <RPC_{f["id"]}_{r[0]}>{r[6]:g}</RPC_{f["id"]}_{r[0]}>\n' for r in fighters)
    settlement = combat
    trader_group = ''
    peaceful_needed = f.get('relation', 'neutral') != 'permanent' and not f.get('noPeaceful')
    if peaceful_needed and not trades:
        guards = ''.join(f'          <RPC_{f["id"]}_{r[0]}>{w}</RPC_{f["id"]}_{r[0]}>\n' for r, w in zip(fighters[:2], [10, 4]))
        trader_group = f'''      <li>
        <kindDef>Peaceful</kindDef>
        <options>
{guards}        </options>
      </li>
'''
    if trades:
        guards = ''.join(f'          <RPC_{f["id"]}_{r[0]}>{w}</RPC_{f["id"]}_{r[0]}>\n' for r, w in zip(fighters[:2], [10, 4]))
        trader_group = f'''      <li>
        <kindDef>Trader</kindDef>
        <traders><RPC_{f['id']}_Trader>1</RPC_{f['id']}_Trader></traders>
        <carriers><Muffalo>6</Muffalo><Dromedary>5</Dromedary></carriers>
        <guards>
{guards}        </guards>
      </li>
      <li>
        <kindDef>Peaceful</kindDef>
        <options>
{guards}        </options>
      </li>
'''
    relation = f.get('relation', 'neutral')
    # hidden (like vanilla mechanoids): not in the faction list, no guests; needed when a faction has no Peaceful group
    hidden_xml = '\n    <hidden>true</hidden>' if f.get('hidden') else ''
    rel_xml = {'permanent': '\n    <permanentEnemy>true</permanentEnemy>', 'hostile': '\n    <naturalEnemy>true</naturalEnemy>', 'neutral': ''}[relation]
    settlements = 0 if kind in ('trauma', 'maxtac', 'collector') else 1
    caravans = f'''
    <caravanTraderKinds><li>RPC_Caravan_{f['id']}</li></caravanTraderKinds>
    <baseTraderKinds><li>RPC_Caravan_{f['id']}</li></baseTraderKinds>
    <visitorTraderKinds><li>Visitor_Outlander_Standard</li></visitorTraderKinds>''' if kind == 'corp' else ('''
    <caravanTraderKinds><li>Caravan_Outlander_BulkGoods</li><li>Caravan_Outlander_CombatSupplier</li></caravanTraderKinds>
    <baseTraderKinds><li>Base_Outlander_Standard</li></baseTraderKinds>
    <visitorTraderKinds><li>Visitor_Outlander_Standard</li></visitorTraderKinds>''' if trades else '')
    rivals = ''.join(f'<li>RPC_Faction_{r}</li>' for r in f.get('rivals', []))
    c = f['color']
    bs = ''.join(f'''
      <li>
        <categories><li>{cat}</li></categories>
        <commonality>{w}</commonality>
      </li>''' for cat, w in BACKSTORY[kind])
    out.append(f'''  <FactionDef ParentName="FactionBase">
    <defName>RPC_Faction_{f['id']}</defName>
    <label>{escape(f['label'])}</label>
    <fixedName>{escape(f['label'])}</fixedName>
    <description>{escape(f['desc'])}</description>
    <pawnSingular>{'operative' if corp else ('nomad' if kind == 'nomads' else 'ganger')}</pawnSingular>
    <pawnsPlural>{'operatives' if corp else ('nomads' if kind == 'nomads' else 'gangers')}</pawnsPlural>
    <categoryTag>{'RPC_Corporation' if corp else 'RPC_Gang'}</categoryTag>
    <listOrderPriority>{40 - i}</listOrderPriority>
    <settlementGenerationWeight>{settlements}</settlementGenerationWeight>
    <requiredCountAtGameStart>{1 if f.get('hidden') or kind in ('trauma', 'maxtac') else 0}</requiredCountAtGameStart>{hidden_xml}
    <startingCountAtWorldCreation>1</startingCountAtWorldCreation>
    <maxConfigurableAtWorldCreation>1</maxConfigurableAtWorldCreation>
    <configurationListOrderPriority>{i}</configurationListOrderPriority>
    <canSiege>{'true' if corp else 'false'}</canSiege>
    <canStageAttacks>true</canStageAttacks>
    <leaderTitle>{'CEO' if kind == 'corp' else ('director' if corp else ('chief' if kind == 'nomads' else 'boss'))}</leaderTitle>
    <factionIconPath>{ICON[kind]}</factionIconPath>
    <settlementTexturePath>World/WorldObjects/DefaultSettlement</settlementTexturePath>
    <settlementNameMaker>NamerSettlementOutlander</settlementNameMaker>
    <techLevel>{tech}</techLevel>
    <allowedCultures><li>{CULTURE[kind]}</li></allowedCultures>
    <colorSpectrum><li>({c[0]}, {c[1]}, {c[2]})</li></colorSpectrum>{rel_xml}
    <backstoryFilters>{bs}
    </backstoryFilters>
    <basicMemberKind>RPC_{f['id']}_{fighters[0][0]}</basicMemberKind>
    <raidLootMaker>RPC_RaidLootMaker</raidLootMaker>
    <allowedArrivalTemperatureRange>-40~45</allowedArrivalTemperatureRange>
    <maxPawnCostPerTotalPointsCurve>
      <points>
        <li>(0,35)</li>
        <li>(70,50)</li>
        <li>(700,120)</li>
        <li>(1300,200)</li>
        <li>(100000,10000)</li>
      </points>
    </maxPawnCostPerTotalPointsCurve>{caravans}
    <pawnGroupMakers>
      <li>
        <kindDef>Combat</kindDef>
        <options>
{combat}        </options>
      </li>
      <li>
        <kindDef>Settlement</kindDef>
        <options>
{settlement}        </options>
      </li>
{trader_group}    </pawnGroupMakers>
    <modExtensions>
      <li Class="RimPunkCE.CyberFactionExtension">
        <kind>{EXT_KIND[kind]}</kind>
        <rivals>{rivals}</rivals>
        <streetNameChance>{0.3 if corp else 0.8}</streetNameChance>
      </li>
    </modExtensions>
  </FactionDef>
''')
    if kind == 'corp':
        out.append(trader_kind(f, orbital=False))
        out.append(trader_kind(f, orbital=True))

path = os.path.join(ROOT, 'Common/Defs/Factions/Generated_Factions.xml')
with open(path, 'w', encoding='utf-8', newline='\n') as fh:
    fh.write('<?xml version="1.0" encoding="utf-8"?>\n<!-- GENERATED by Source/Tools/gen_factions.py. Do not edit; change the generator and rerun. -->\n<Defs>\n\n')
    fh.write('\n'.join(out))
    fh.write('\n</Defs>\n')
print(f'{len(F)} factions')
