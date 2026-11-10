"""
Cyberware content generator (prompts/cyberware/*/manifest.md -> mod defs).

Output (overwritten every run, do not hand-edit): Common/Defs/Cyberware/Generated_Cyberware.xml
The original ten hand-written items (Gorilla Arms, Mantis Blades, Smart Link, Smart Imprint, Dynalar Sandevistan,
Moore Tech Berserk, Arasaka cyberdeck, Chrome Compressor, RAM Upgrade, Newton Module) stay in their own files.

Conventions (layout.md / terms.md):
  - a tiered number is a scalar (same at every tier), (min, max) across the item's own tier range, or a
    list of 6 (T1, T2, T3, T4, T5, Iconic);
  - times are seconds, fractions are 0.05 = 5%, armor is CE mm (sharp) / MPa (blunt);
  - "+x% attack speed" -> MeleeCooldownFactor x 1/(1+x); "+x% movement speed" -> MoveSpeed x (1+x).
Behaviour comes from the generic comps in Source/RimPunkCE/Cyberware/CyberSystems.cs and CyberSpecials.cs.
"""
import os
from xml.sax.saxutils import escape

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Common', 'Defs', 'Cyberware', 'Generated_Cyberware.xml')

TIERS = ['T1', 'T2', 'T3', 'T4', 'T5', 'Iconic']


# ------------------------------------------------------------------------------------------------ helpers

def lerp_tiers(v, lo, hi):
    """Evaluate a tiered number to 6 per-tier floats (same rule as TieredFloat.Evaluate)."""
    if isinstance(v, list):
        assert len(v) == 6, v
        return [float(x) for x in v]
    if not isinstance(v, tuple):
        return [float(v)] * 6
    a, b = v
    i0, i1 = TIERS.index(lo), TIERS.index(hi)
    out = []
    for i in range(6):
        f = 1.0 if i1 <= i0 else min(1.0, max(0.0, (i - i0) / (i1 - i0)))
        out.append(a + (b - a) * f)
    return out


def fmt(x):
    x = round(float(x), 4)
    return str(int(x)) if x == int(x) else ('%g' % x)


def tv(v):
    """TieredFloat XML body."""
    if isinstance(v, list):
        return '<perTier>' + ''.join(f'<li>{fmt(x)}</li>' for x in v) + '</perTier>'
    if isinstance(v, tuple):
        return f'<min>{fmt(v[0])}</min><max>{fmt(v[1])}</max>'
    return f'<min>{fmt(v)}</min><max>{fmt(v)}</max>'


def per_tier_map(v, lo, hi, fn):
    return [fn(x) for x in lerp_tiers(v, lo, hi)]


def speed(v, lo, hi):
    """+x% attack speed -> MeleeCooldownFactor values."""
    return per_tier_map(v, lo, hi, lambda x: 1.0 / (1.0 + x))


def plus1(v, lo, hi):
    return per_tier_map(v, lo, hi, lambda x: 1.0 + x)


def minus1(v, lo, hi):
    return per_tier_map(v, lo, hi, lambda x: 1.0 - x)


class E:
    """Effects block (TieredEffects)."""

    def __init__(self):
        self.offsets, self.factors, self.caps = [], [], []
        self.pain = None

    def off(self, stat, v):
        self.offsets.append((stat, v)); return self

    def fac(self, stat, v):
        self.factors.append((stat, v)); return self

    def cap(self, capacity, v):
        self.caps.append((capacity, v)); return self

    def armor(self, sharp, blunt):
        if sharp is not None: self.off('ArmorRating_Sharp', sharp)
        if blunt is not None: self.off('ArmorRating_Blunt', blunt)
        return self

    def empty(self):
        return not (self.offsets or self.factors or self.caps or self.pain is not None)

    def xml(self, ind='        '):
        if self.empty(): return ''
        s = f'{ind}<effects>\n'
        if self.offsets:
            s += f'{ind}  <statOffsets>\n' + ''.join(f'{ind}    <li><stat>{st}</stat><value>{tv(v)}</value></li>\n' for st, v in self.offsets) + f'{ind}  </statOffsets>\n'
        if self.factors:
            s += f'{ind}  <statFactors>\n' + ''.join(f'{ind}    <li><stat>{st}</stat><value>{tv(v)}</value></li>\n' for st, v in self.factors) + f'{ind}  </statFactors>\n'
        if self.caps:
            s += f'{ind}  <capMods>\n' + ''.join(f'{ind}    <li><capacity>{c}</capacity><offset>{tv(v)}</offset></li>\n' for c, v in self.caps) + f'{ind}  </capMods>\n'
        if self.pain is not None:
            s += f'{ind}  <painFactor>{tv(self.pain)}</painFactor>\n'
        return s + f'{ind}</effects>\n'


def values_xml(values, ind='        '):
    if not values: return ''
    return f'{ind}<values>\n' + ''.join(f'{ind}  <li><key>{k}</key><value>{tv(v)}</value></li>\n' for k, v in values.items()) + f'{ind}</values>\n'


# ---- comps

def trig(on, effects, weapon=None, chance=None, threshold=None, window=None, cooldown=None, active=None):
    """A CyberTrigger. chance/threshold/window/cooldown are value keys; effects: list of eff(...)"""
    s = f'          <li>\n            <on>{on}</on>\n'
    if weapon: s += f'            <weapon>{weapon}</weapon>\n'
    if chance: s += f'            <chanceKey>{chance}</chanceKey>\n'
    if threshold: s += f'            <thresholdKey>{threshold}</thresholdKey>\n'
    if window: s += f'            <windowKey>{window}</windowKey>\n'
    if cooldown: s += f'            <cooldownKey>{cooldown}</cooldownKey>\n'
    if active: s += f'            <activeDef>{active}</activeDef>\n'
    s += '            <effects>\n' + ''.join(effects) + '            </effects>\n          </li>\n'
    return s


def eff(kind, key=None, hediff=None, duration=None, radius=None, at_victim=False):
    s = f'              <li><kind>{kind}</kind>'
    if key: s += f'<valueKey>{key}</valueKey>'
    if hediff: s += f'<hediff>{hediff}</hediff>'
    if duration: s += f'<durationKey>{duration}</durationKey>'
    if radius: s += f'<radius>{radius}</radius>'
    if at_victim: s += '<atVictim>true</atVictim>'
    return s + '</li>\n'


def triggers(*ts):
    return '      <li Class="RimPunkCE.HediffCompProperties_CyberTriggers">\n        <triggers>\n' + ''.join(ts) + '        </triggers>\n      </li>\n'


def cond(when, effect, threshold=None, active=None):
    s = f'          <li><when>{when}</when><effect>{effect}</effect>'
    if threshold: s += f'<thresholdKey>{threshold}</thresholdKey>'
    if active: s += f'<activeDef>{active}</activeDef>'
    return s + '</li>\n'


def conditionals(*cs):
    return '      <li Class="RimPunkCE.HediffCompProperties_CyberConditionals">\n        <conditionals>\n' + ''.join(cs) + '        </conditionals>\n      </li>\n'


def onhit(rider=None, shock=None, burn=None, poison=None, bleed=None, fraction=0.3):
    s = '      <li Class="RimPunkCE.HediffCompProperties_CyberOnHit">\n'
    if rider: s += f'        <riderDamage>{rider}</riderDamage>\n        <riderFraction>{fraction}</riderFraction>\n'
    for tag, key in (('shockChanceKey', shock), ('burnChanceKey', burn), ('poisonChanceKey', poison), ('bleedChanceKey', bleed)):
        if key: s += f'        <{tag}>{key}</{tag}>\n'
    return s + '      </li>\n'


def give(*abilities, paired=None):
    s = '      <li Class="RimPunkCE.HediffCompProperties_GiveCyberAbilities">\n        <abilities>' + ''.join(f'<li>{a}</li>' for a in abilities) + '</abilities>\n'
    if paired: s += '        <abilitiesWhenPaired>' + ''.join(f'<li>{a}</li>' for a in paired) + '</abilitiesWhenPaired>\n'
    return s + '      </li>\n'


def tools(*ts):
    return '      <li Class="HediffCompProperties_VerbGiver">\n        <tools>\n' + ''.join(ts) + '        </tools>\n      </li>\n'


def tool(label, capacity, power, cooldown, sharp=None, blunt=1.0, sounds=None):
    s = f'          <li Class="CombatExtended.ToolCE">\n            <label>{label}</label>\n            <capacities><li>{capacity}</li></capacities>\n'
    s += f'            <power>{power}</power>\n            <cooldownTime>{cooldown}</cooldownTime>\n'
    if sharp is not None: s += f'            <armorPenetrationSharp>{sharp}</armorPenetrationSharp>\n'
    s += f'            <armorPenetrationBlunt>{blunt}</armorPenetrationBlunt>\n'
    if sounds: s += f'            <soundMeleeHit>{sounds[0]}</soundMeleeHit>\n            <soundMeleeMiss>{sounds[1]}</soundMeleeMiss>\n'
    return s + '          </li>\n'


QUICKHACK_EYES = '''    <renderNodeProperties>
      <li Class="PawnRenderNodeProperties_Eye">
        <workerClass>RimPunkCE.PawnRenderNodeWorker_QuickhackEyes</workerClass>
        <texPath>Things/RimPunk/Eyes/GlowEyes</texPath>
        <color>(0.2, 0.6, 1)</color>
        <parentTagDef>Head</parentTagDef>
        <anchorTag>RightEye</anchorTag>
        <rotDrawMode>Fresh, Rotting</rotDrawMode>
        <drawSize>0.2</drawSize>
        <side>Right</side>
        <drawData><defaultData><layer>56</layer><offset>(0, 0, -0.25)</offset></defaultData></drawData>
      </li>
      <li Class="PawnRenderNodeProperties_Eye">
        <workerClass>RimPunkCE.PawnRenderNodeWorker_QuickhackEyes</workerClass>
        <texPath>Things/RimPunk/Eyes/GlowEyes</texPath>
        <color>(0.2, 0.6, 1)</color>
        <parentTagDef>Head</parentTagDef>
        <anchorTag>LeftEye</anchorTag>
        <rotDrawMode>Fresh, Rotting</rotDrawMode>
        <drawSize>0.2</drawSize>
        <side>Left</side>
        <drawData>
          <defaultData><layer>56</layer><offset>(0, 0, -0.25)</offset><flip>true</flip></defaultData>
          <dataWest><flip>false</flip></dataWest>
        </drawData>
      </li>
    </renderNodeProperties>
'''

BERSERK_LOOK = '''    <comps>
      <li Class="HediffCompProperties_Effecter">
        <stateEffecter>Berserk</stateEffecter>
      </li>
    </comps>
'''

COST = {
    'T1': [('Steel', 30), ('ComponentIndustrial', 3)],
    'T2': [('Plasteel', 12), ('ComponentSpacer', 2)],
    'T3': [('Plasteel', 18), ('ComponentSpacer', 3)],
    'T4': [('Plasteel', 25), ('ComponentSpacer', 4)],
    'T5': [('Plasteel', 35), ('ComponentSpacer', 5), ('Gold', 10)],
}

SLOT = {
    'arm': 'RPC_Slot_Arm', 'hand': 'RPC_Slot_Hands', 'face': 'RPC_Slot_Face', 'leg': 'RPC_Slot_Legs',
    'os': 'RPC_Slot_OperatingSystem', 'pfc': 'RPC_Slot_PrefrontalCortex', 'skeleton': 'RPC_Slot_Skeleton',
    'nervous': 'RPC_Slot_NervousSystem', 'circulatory': 'RPC_Slot_CirculatorySystem', 'integumentary': 'RPC_Slot_IntegumentarySystem',
}

out = []
section = None


def header(title):
    out.append(f'\n  <!-- ============================================================ {title} -->\n')


# Explicit plating choices. Other implants retain their non-armour effects.
PLATING = {'Carapace', 'CellularAdapter', 'Chitin', 'CogitoLattice', 'NanoPlating', 'RangeGuard', 'SubdermalArmor', 'TitaniumBones'}
PLATING_ACTIVE = {'CogitoLatticeBoost', 'RangeGuardBoost'}

def balance_armour(id, effects, allowed):
    if effects is None: return
    result = []
    for stat, value in effects.offsets:
        if stat not in ('ArmorRating_Sharp', 'ArmorRating_Blunt'):
            result.append((stat, value))
        elif id in allowed:
            value = [v * 0.4 for v in value] if isinstance(value, list) else tuple(v * 0.4 for v in value) if isinstance(value, tuple) else value * 0.4
            result.append((stat, value))
    effects.offsets = result

def cyberware(id, label, desc, slot, cap, tiers, effects=None, values=None, comps='', os_type=None, extra='', note=None, role=None):
    balance_armour(id, effects, PLATING)
    if id == 'BionicJoints': desc = 'Replacement joints. Reinforce the body without adding armour plating.'
    lo, hi = tiers
    cost = list(COST[lo])
    extra_comp = cap // 20
    if extra_comp:
        name = 'ComponentIndustrial' if lo == 'T1' else 'ComponentSpacer'
        cost = [(k, v + extra_comp if k == name else v) for k, v in cost]
    cost_xml = ''.join(f'      <{k}>{v}</{k}>\n' for k, v in cost)
    if note: out.append(f'  <!-- {escape(note)} -->\n')
    out.append(f'''  <ThingDef ParentName="RPC_CyberwareItemBase">
    <defName>RPC_Item_{id}</defName>
    <label>{escape(label)}</label>
    <description>{escape(desc)}</description>
    <costList>
{cost_xml}    </costList>
    <recipeMaker>
      <researchPrerequisite>RPC_Cyberware_{lo}</researchPrerequisite>
    </recipeMaker>
  </ThingDef>

  <HediffDef ParentName="RPC_CyberwareHediffBase">
    <defName>RPC_{id}</defName>
    <label>{escape(label)}</label>
    <description>{escape(desc)}</description>
    <descriptionHyperlinks><ThingDef>RPC_Item_{id}</ThingDef></descriptionHyperlinks>
''' + (f'    <comps>\n{comps}    </comps>\n' if comps else '') + extra + f'''    <modExtensions>
      <li Class="RimPunkCE.CyberwareExtension">
        <slot>{SLOT[slot]}</slot>
''' + (f'        <osType>{os_type}</osType>\n' if os_type else '') + (f'        <combatRole>{role}</combatRole>\n' if role else '') + f'''        <capacityCost>{cap}</capacityCost>
        <minTier>{lo}</minTier>
        <maxTier>{hi}</maxTier>
        <item>RPC_Item_{id}</item>
''' + (effects.xml() if effects else '') + values_xml(values) + '''      </li>
    </modExtensions>
  </HediffDef>

''')


def active(id, label, desc, effects=None, ext='', comps='', color='(1, 0.85, 0.3)', stages=''):
    balance_armour(id, effects, PLATING_ACTIVE)
    out.append(f'''  <HediffDef ParentName="RPC_CyberActiveBase">
    <defName>RPC_{id}</defName>
    <label>{escape(label)}</label>
    <description>{escape(desc)}</description>
    <defaultLabelColor>{color}</defaultLabelColor>
''' + (comps if comps.lstrip().startswith('<comps>') else (f'    <comps>\n{comps}    </comps>\n' if comps else '')) + stages + '''    <modExtensions>
      <li Class="RimPunkCE.ActiveEffectExtension">
''' + (effects.xml('        ') if effects else '') + ext + '''      </li>
    </modExtensions>
  </HediffDef>

''')
    return f'RPC_{id}'


def buff_ability(id, label, desc, hediff, duration, ai_dist=30, ai=True, extra_comps='', adrenaline=0, ai_rule=None):
    out.append(f'''  <AbilityDef ParentName="RPC_CyberSelfAbilityBase">
    <defName>RPC_Ability_{id}</defName>
    <label>{escape(label)}</label>
    <description>{escape(desc)}</description>
''' + ('' if ai else '    <aiCanUse>false</aiCanUse>\n') + f'''    <comps>
{extra_comps}      <li Class="RimPunkCE.CompProperties_AbilityCyberBuff">
        <hediffDef>{hediff}</hediffDef>
        <defaultDurationSeconds>{duration}</defaultDurationSeconds>
        <aiMaxEnemyDistance>{ai_dist}</aiMaxEnemyDistance>
''' + (f'        <adrenalineCost>{adrenaline}</adrenalineCost>\n' if adrenaline else '') + (f'        <aiRule>{ai_rule}</aiRule>\n' if ai_rule else '') + f'''      </li>
    </comps>
  </AbilityDef>

''')
    return f'RPC_Ability_{id}'


def jump_ability(id, label, range_, charges=None, recharge=120):
    out.append(f'''  <AbilityDef>
    <defName>RPC_Ability_{id}</defName>
    <label>{escape(label)}</label>
    <description>Leap to a spot up to {range_} tiles away.</description>
    <category>RPC_Cyberware</category>
    <iconPath>UI/RimPunk/Placeholder_Ability</iconPath>
    <cooldownTicksRange>{recharge}</cooldownTicksRange>
''' + (f'    <charges>{charges}</charges>\n    <cooldownPerCharge>true</cooldownPerCharge>\n' if charges else '') + f'''    <hostile>false</hostile>
    <casterMustBeCapableOfViolence>false</casterMustBeCapableOfViolence>
    <showPsycastEffects>false</showPsycastEffects>
    <displayGizmoWhileUndrafted>true</displayGizmoWhileUndrafted>
    <disableGizmoWhileUndrafted>false</disableGizmoWhileUndrafted>
    <aiCanUse>false</aiCanUse>
    <jobDef>CastJump</jobDef>
    <verbProperties>
      <verbClass>Verb_CastAbilityJump</verbClass>
      <label>jump</label>
      <violent>false</violent>
      <forceNormalTimeSpeed>false</forceNormalTimeSpeed>
      <warmupTime>0.3</warmupTime>
      <range>{range_}</range>
      <requireLineOfSight>true</requireLineOfSight>
      <targetParams>
        <canTargetLocations>true</canTargetLocations>
        <canTargetPawns>false</canTargetPawns>
        <canTargetBuildings>false</canTargetBuildings>
      </targetParams>
    </verbProperties>
  </AbilityDef>

''')
    return f'RPC_Ability_{id}'


def target_ability(id, label, desc, range_, warmup, comps, hostile=True, ai=True):
    out.append(f'''  <AbilityDef>
    <defName>RPC_Ability_{id}</defName>
    <label>{escape(label)}</label>
    <description>{escape(desc)}</description>
    <abilityClass>RimPunkCE.CyberwareAbility</abilityClass>
    <category>RPC_Cyberware</category>
    <iconPath>UI/RimPunk/Placeholder_Ability</iconPath>
    <showPsycastEffects>false</showPsycastEffects>
    <cooldownTicksRange>120</cooldownTicksRange>
    <hostile>{'true' if hostile else 'false'}</hostile>
    <aiCanUse>{'true' if ai else 'false'}</aiCanUse>
    <ai_IsOffensive>true</ai_IsOffensive>
    <displayGizmoWhileUndrafted>false</displayGizmoWhileUndrafted>
    <verbProperties>
      <verbClass>Verb_CastAbility</verbClass>
      <range>{range_}</range>
      <warmupTime>{warmup}</warmupTime>
      <requireLineOfSight>true</requireLineOfSight>
      <violent>true</violent>
      <targetParams>
        <canTargetPawns>true</canTargetPawns>
        <canTargetBuildings>true</canTargetBuildings>
        <canTargetLocations>true</canTargetLocations>
      </targetParams>
    </verbProperties>
    <comps>
{comps}    </comps>
  </AbilityDef>

''')
    return f'RPC_Ability_{id}'


def shell(id, label, damage_def, damage, radius, sound='Explosion_Bomb', frags=0):
    """A CE explosive projectile (ballistic arc, CE explosion and fragments) fired by the arm launcher."""
    frag_xml = f'''
    <comps>
      <li Class="CombatExtended.CompProperties_Fragments">
        <fragments><Fragment_Small>{frags}</Fragment_Small></fragments>
      </li>
    </comps>''' if frags else ''
    out.append(f'''  <ThingDef ParentName="BaseExplosiveBullet">
    <defName>RPC_Shell_{id}</defName>
    <label>{escape(label)}</label>
    <thingClass>CombatExtended.ProjectileCE_Explosive</thingClass>
    <graphicData>
      <texPath>Things/Projectile/LauncherShot</texPath>
      <graphicClass>Graphic_Single</graphicClass>
      <drawSize>(0.5,0.5)</drawSize>
    </graphicData>
    <projectile Class="CombatExtended.ProjectilePropertiesCE">
      <speed>30</speed>
      <damageDef>{damage_def}</damageDef>
      <damageAmountBase>{int(round(damage))}</damageAmountBase>
      <explosionRadius>{fmt(radius)}</explosionRadius>
      <soundExplode>{sound}</soundExplode>
      <applyDamageToExplosionCellsNeighbors>true</applyDamageToExplosionCellsNeighbors>
    </projectile>{frag_xml}
  </ThingDef>

''')
    return f'RPC_Shell_{id}'


def launcher_ammo(id, label, ammo_class, tex, projectile, cost):
    """CE AmmoDef for the arm launcher (carried in inventory, loaded into the implant's magazine). Not used by any
    gun, so CE's AmmoInjector skips it: crafting and trade are wired here instead."""
    costs = ''.join(f'''
      <li>
        <filter><thingDefs><li>{k}</li></thingDefs></filter>
        <count>{v}</count>
      </li>''' for k, v in cost.items())
    out.append(f'''  <ThingDef Class="CombatExtended.AmmoDef" ParentName="MediumAmmoBase">
    <defName>Ammo_RPC_LauncherShell_{id}</defName>
    <label>arm launcher shell ({escape(label)})</label>
    <description>A compact shell for the projectile launch system arm implant. Carry them and the implant reloads from your inventory. They fit nothing else.</description>
    <statBases>
      <Mass>0.2</Mass>
      <Bulk>0.3</Bulk>
      <MarketValue>9</MarketValue>
    </statBases>
    <tradeTags>
      <li>RPC_Ammo</li>
      <li>RPC_LauncherShell</li>
    </tradeTags>
    <thingCategories>
      <li>RPC_Ammo_LauncherShells</li>
    </thingCategories>
    <stackLimit>200</stackLimit>
    <graphicData>
      <texPath>Things/Ammo/GrenadeLauncher/{tex}</texPath>
      <graphicClass>Graphic_StackCount</graphicClass>
    </graphicData>
    <ammoClass>{ammo_class}</ammoClass>
    <detonateProjectile>{projectile}</detonateProjectile>
  </ThingDef>

  <RecipeDef>
    <defName>MakeAmmo_RPC_LauncherShell_{id}</defName>
    <label>make arm launcher shells ({escape(label)}) x10</label>
    <description>Craft 10 arm launcher shells ({escape(label)}).</description>
    <jobString>Making arm launcher shells.</jobString>
    <workAmount>1800</workAmount>
    <workSpeedStat>GeneralLaborSpeed</workSpeedStat>
    <effectWorking>Smith</effectWorking>
    <soundWorking>Recipe_Machining</soundWorking>
    <workSkill>Crafting</workSkill>
    <researchPrerequisite>RPC_Cyberware_T2</researchPrerequisite>
    <recipeUsers><li>TableMachining</li><li>FabricationBench</li></recipeUsers>
    <ingredients>{costs}
    </ingredients>
    <fixedIngredientFilter><thingDefs>{''.join(f'<li>{k}</li>' for k in cost)}</thingDefs></fixedIngredientFilter>
    <products>
      <Ammo_RPC_LauncherShell_{id}>10</Ammo_RPC_LauncherShell_{id}>
    </products>
  </RecipeDef>

''')
    return f'Ammo_RPC_LauncherShell_{id}'


# cycle 1 arm set. gorilla: heaviest single hit, slow, no way to close. mantis: fast, dash. monowire: reach. launcher: range, ammo.
GORILLA_DMG = (0.10, 0.30)

# ================================================================================================ arms
header('arms (arms/manifest.md): one per arm, mixed freely')


def gorilla(id, label, desc, rider, chance_key):
    lo, hi = 'T2', 'Iconic'
    cyberware(id, label, desc, 'arm', 8, (lo, hi),
              E().off('MeleeDamageFactor', GORILLA_DMG),
              {chance_key: (0.05, 0.1)},
              tools(tool('gorilla fist', 'Blunt', 20, 1.7, blunt=6, sounds=('MeleeHit_BionicPunch', 'MeleeMiss_BionicPunch')))
              + onhit(rider=rider, fraction=0.15, **{chance_key.replace('Chance', ''): chance_key}), role='Melee')


gorilla('ElectrifyingGorillaArms', 'electrifying gorilla arm', 'Gorilla arm actuators wrapped in shock coils. Every punch carries a jolt.', 'RPC_Electrical', 'shockChance')
gorilla('ThermalGorillaArms', 'thermal gorilla arm', 'Gorilla arm with heating elements in the knuckles. Punches burn and can set the target alight.', 'RPC_Thermal', 'burnChance')
gorilla('ToxicGorillaArms', 'toxic gorilla arm', 'Gorilla arm with toxin injectors in the knuckles.', 'RPC_Toxic', 'poisonChance')

MANTIS_TOOLS = (tool('blade edge', 'Cut', 20, 0.9, sharp=8.0, blunt=1.0), tool('blade point', 'Stab', 22, 1.1, sharp=12.0, blunt=1.0))


def mantis(id, label, desc, tiers, rider=None, chance_key=None, chance=(0.1, 0.2), fraction=0.3):
    hit = onhit(rider=rider, fraction=fraction, **({chance_key.replace('Chance', ''): chance_key} if chance_key else {})) if (rider or chance_key) else ''
    values = {'cooldown': (12, 6)}
    if chance_key: values[chance_key] = chance
    cyberware(id, label, desc, 'arm', 8, tiers, E().off('MeleeDamageFactor', (0.05, 0.25)), values,
              tools(*MANTIS_TOOLS) + '      <li Class="RimPunkCE.HediffCompProperties_MantisBlades" />\n' + give('RPC_Ability_MantisDash') + hit)


mantis('ToxicMantisBlades', 'toxic mantis blade', 'Mantis blades with toxin channels along the edge.', ('T2', 'Iconic'), 'RPC_Toxic', 'poisonChance')
mantis('ElectrifyingMantisBlades', 'electrifying mantis blade', 'Mantis blades charged with a high-voltage current.', ('T2', 'Iconic'), 'RPC_Electrical', 'shockChance')
mantis('ThermalMantisBlades', 'thermal mantis blade', 'Mantis blades heated until they glow.', ('T2', 'Iconic'), 'RPC_Thermal', 'burnChance')
mantis('MaxTacMantisBlades', 'MaxTac mantis blade', 'Heavy mantis blades issued to MaxTac. Serrated edges leave wounds that bleed.', ('T4', 'Iconic'), None, 'bleedChance', (0.15, 0.2))


def monowire(id, label, desc, damage_def, rider=None, chance_key=None):
    lash = target_ability(id + 'Lash', label + ' lash', 'Whip the monowire at a target up to 6 tiles away.', 5.9, 0.3,
                          f'      <li Class="RimPunkCE.CompProperties_AbilityMonowireLash">\n        <damageDef>{damage_def}</damageDef>\n      </li>\n')
    hit = onhit(rider=rider, **({chance_key.replace('Chance', ''): chance_key} if chance_key else {})) if (rider or chance_key) else ''
    values = {'lashDamage': (14, 28)}
    if chance_key: values[chance_key] = (0.1, 0.2)
    cyberware(id, label, desc, 'arm', 8, ('T2', 'Iconic'), E().off('MeleeDamageFactor', (0.03, 0.15)), values,
              tools(tool('monowire', 'Cut', 16, 1.0, sharp=2.2, blunt=0.5)) + give(lash) + hit,
              note='monowire: adjacent cut plus a 6-tile lash ability')


monowire('Monowire', 'monowire', 'A thin, impossibly sharp wire that whips out of the wrist.', 'RPC_MonowireCut')
monowire('ElectrifyingMonowire', 'electrifying monowire', 'A monowire carrying an electrical charge.', 'RPC_Electrical', None, 'shockChance')
monowire('ToxicMonowire', 'toxic monowire', 'A monowire coated in neurotoxin.', 'RPC_MonowireCut', 'RPC_Toxic', 'poisonChance')
monowire('ThermalMonowire', 'thermal monowire', 'A monowire heated to a searing temperature.', 'RPC_Thermal', None, 'burnChance')

# projectile launch system: 4 CE shell types (own ammo items, carried in inventory), loaded into the implant's magazine
# (size by tier) by a reload job. snap (instant, scatter) and aimed (warmup, +30% damage, +25% radius, accurate).
# (id, label, damageDef, damage, radius, sound, ammoClass, texture, frags, cost per 10)
SHELLS = [('HE', 'HE shell', 'Bomb', 28, 1.9, 'Explosion_Bomb', 'GrenadeHE', 'HE', 15, {'Steel': 20, 'ComponentIndustrial': 1}),
          ('Toxic', 'toxic shell', 'RPC_Toxic', 16, 2.2, 'Explosion_Bomb', 'Toxic', 'Tox', 0, {'Steel': 15, 'Neutroamine': 2}),
          ('Electrifying', 'electrifying shell', 'RPC_Electrical', 16, 2.2, 'Explosion_EMP', 'GrenadeEMP', 'EMP', 0, {'Steel': 15, 'ComponentIndustrial': 2}),
          ('Incendiary', 'incendiary shell', 'Flame', 14, 2.2, 'Explosion_Flame', 'GrenadeIncendiary', 'AIR', 0, {'Steel': 15, 'Chemfuel': 15})]
LAUNCHER_MAG = {'T2': 3, 'T3': 4, 'T4': 5, 'T5': 6, 'Iconic': 8}
out.append('''  <ThingCategoryDef>
    <defName>RPC_Ammo_LauncherShells</defName>
    <label>arm launcher shells</label>
    <parent>RPC_Ammo</parent>
    <iconPath>UI/Icons/ThingCategories/CaliberGrenade</iconPath>
  </ThingCategoryDef>

''')
snap_shells, aimed_shells, shell_ammo = [], [], []
for sid, slabel, ddef, dmg, rad, snd, aclass, tex, frags, cost in SHELLS:
    snap_shells.append(shell('Launcher' + sid, slabel, ddef, dmg, rad, snd, frags))
    aimed_shells.append(shell('LauncherAimed' + sid, 'aimed ' + slabel, ddef, dmg * 1.3, rad * 1.25, snd, int(frags * 1.3)))
    shell_ammo.append(launcher_ammo(sid, slabel.replace(' shell', ''), aclass, tex, snap_shells[-1], cost))
out.append('''  <CombatExtended.AmmoSetDef>
    <defName>RPC_AmmoSet_ArmLauncher</defName>
    <label>arm launcher shells</label>
    <ammoTypes>
''' + ''.join(f'      <{a}>{p}</{a}>\n' for a, p in zip(shell_ammo, snap_shells)) + '''    </ammoTypes>
  </CombatExtended.AmmoSetDef>

''')
snap = target_ability('LauncherSnap', 'launcher: snap shot', 'Fire the arm launcher without aiming. Fast but scattered. Uses one loaded shell.', 24.9, 0,
                      '      <li Class="RimPunkCE.CompProperties_AbilityLauncher">\n        <aimed>false</aimed>\n        <scatter>2.5</scatter>\n      </li>\n')
aimed = target_ability('LauncherAimed', 'launcher: aimed shot', 'Take aim with the arm launcher: +30% damage, +25% explosion radius, much more accurate. Uses one loaded shell.', 29.9, 1.5,
                       '      <li Class="RimPunkCE.CompProperties_AbilityLauncher">\n        <aimed>true</aimed>\n        <scatter>0.6</scatter>\n      </li>\n')
cyberware('ProjectileLaunchSystem', 'projectile launch system', 'A grenade launcher built into the forearm. Fires HE, toxic, electrifying or incendiary shells from a small internal magazine, reloaded from shells carried in the inventory.',
          'arm', 8, ('T2', 'Iconic'), None, None,
          give(snap, aimed) + '      <li Class="RimPunkCE.HediffCompProperties_LauncherAmmo">\n'
          + '        <shells>' + ''.join(f'<li>{s}</li>' for s in snap_shells) + '</shells>\n'
          + '        <aimedShells>' + ''.join(f'<li>{s}</li>' for s in aimed_shells) + '</aimedShells>\n'
          + '        <ammo>' + ''.join(f'<li>{a}</li>' for a in shell_ammo) + '</ammo>\n'
          + '        <shellLabels>' + ''.join(f'<li>{x[1]}</li>' for x in SHELLS) + '</shellLabels>\n'
          + '        <magazineByTier>' + ''.join(f'<li>{LAUNCHER_MAG.get(t, 0)}</li>' for t in ('T1', 'T2', 'T3', 'T4', 'T5', 'Iconic')) + '</magazineByTier>\n      </li>\n',
          note='CE ammo: shells are items (Ammo_RPC_LauncherShell_*), loaded into a magazine sized by tier (3/4/5/6/8), reloaded from inventory')

# ================================================================================================ hands
header('hands (hands/manifest.md): one, in either hand')
hw = active('HandleWrapActive', 'handle wrap', 'Grenade crits are more likely.', E().off('RPC_CritChanceGrenades', (0.08, 0.27)))
cyberware('HandleWrap', 'handle wrap', 'Grip-assist weave for throwables. Throwing a grenade boosts grenade crit chance for a while.', 'hand', 8, ('T3', 'Iconic'),
          None, {'duration': 60}, triggers(trig('UsedGrenade', [eff('ApplyActive', hediff=hw)])),
          note='trigger: throwing a grenade (equipping one is not tracked)')
lo, hi = 'T2', 'Iconic'
cyberware('ImmovableForce', 'Immovable Force', 'Hand stabilisers that anchor the user\'s aim: less recoil, tighter spread, harder to suppress.', 'hand', 35, (lo, hi),
          E().fac('RPC_RecoilFactor', minus1((0.18, 0.35), lo, hi)).fac('RPC_SpreadFactor', 0.75).fac('Suppressability', minus1((0.18, 0.35), lo, hi)))
cyberware('Microgenerator', 'microgenerator', 'Reloading an empty weapon charges the next shot with an electroshock around the point of impact.', 'hand', 9, ('T2', 'Iconic'),
          None, {'shockDamage': (7.5, 25)}, '      <li Class="RimPunkCE.HediffCompProperties_Microgenerator" />\n')
lo, hi = 'T1', 'Iconic'
cyberware('ShockAbsorber', 'shock absorber', 'Dampers in the wrist that soak up recoil.', 'hand', 12, (lo, hi), E().fac('RPC_RecoilFactor', minus1((0.08, 0.24), lo, hi)))

# ================================================================================================ face
header('face (face/manifest.md): installed in the eyes, two max')
ACC_T1 = [0.04, 0.06, 0.06, 0.08, 0.10, 0.10]   # +4/6/8/10% (t1/t2/t4/t5)


def acc(values):
    return [1 + x for x in values]


cyberware('BasicKiroshiOptics', 'basic Kiroshi optics', 'Entry-level optics with digital zoom.', 'face', 1, ('T1', 'Iconic'), E().fac('AimingAccuracy', acc(ACC_T1)))

reveal_active = []


def optics(id, label, desc, cap, tiers, accuracy, enemies=None, traps=None, turrets=None):
    ext = ''
    if enemies: ext += f'        <revealEnemies>{tv(enemies)}</revealEnemies>\n'
    if traps: ext += f'        <revealTraps>{tv(traps)}</revealTraps>\n'
    if turrets: ext += f'        <revealTurrets>{tv(turrets)}</revealTurrets>\n'
    h = active(id + 'Scan', label + ' scan', 'Scanning the area: hidden threats show through the fog.', None, ext, color='(0.4, 0.8, 1)')
    ab = buff_ability(id + 'Scan', label + ' scan', 'Scan the surroundings for 600 seconds, revealing hidden threats.', h, 600, ai=False)
    cyberware(id, label, desc, 'face', cap, tiers, E().fac('AimingAccuracy', acc(accuracy)), {'duration': 600}, give(ab))


optics('KiroshiClairvoyantOptics', 'Kiroshi Clairvoyant optics', 'Optics that pick up heat and movement through walls, revealing enemies in the fog of war.', 8, ('T3', 'Iconic'),
       [0.06, 0.06, 0.06, 0.08, 0.10, 0.10], enemies=(12.5, 19.5))
optics('KiroshiDoomsayerOptics', 'Kiroshi Doomsayer optics', 'Optics tuned to spot tripwires, mines and IEDs.', 2, ('T1', 'Iconic'), ACC_T1, traps=(10, 29))
optics('KiroshiSentryOptics', 'Kiroshi Sentry optics', 'Optics that ping automated defences.', 2, ('T1', 'Iconic'), ACC_T1, turrets=(15, 39))
optics('KiroshiOracleOptics', 'Kiroshi Oracle optics', 'Top-end scanning optics: enemies, turrets and traps all show up.', 10, ('T4', 'Iconic'),
       [0.08, 0.08, 0.08, 0.08, 0.10, 0.10], enemies=(15, 19.5), traps=(22, 29), turrets=(30, 39))
cyberware('KiroshiCockatriceOptics', 'Kiroshi Cockatrice optics', 'Combat optics that highlight weak points.', 'face', 30, ('T4', 'Iconic'),
          E().off('RPC_RangedCritChance', (0.25, 0.35)).off('MeleeCritChance', (0.25, 0.35)).fac('AimingAccuracy', 1.10))
cyberware('KiroshiStalkerOptics', 'Kiroshi Stalker optics', 'Optics that track targets behind cover.', 'face', 2, ('T1', 'Iconic'),
          E().fac('AimingAccuracy', [a + b for a, b in zip(acc(ACC_T1), lerp_tiers((0.10, 0.24), 'T1', 'Iconic'))]),
          note='stalker: the vs-cover bonus applies to all shots (cover is not checked per shot)')

# ================================================================================================ legs
header('legs (legs/manifest.md): one per leg, a matching pair doubles everything')
jump = jump_ability('CyberJump', 'jump', 10)
jump_pair = jump_ability('CyberJumpPaired', 'jump (paired)', 20)
cyberware('FortifiedAnkles', 'fortified ankles', 'Reinforced ankle joints that let the user leap like a jump pack.', 'leg', 6, ('T1', 'Iconic'),
          E().armor(lerp_tiers((1.75, 5.15), 'T2', 'Iconic')[0:1] and [0] + lerp_tiers((1.75, 5.15), 'T2', 'Iconic')[1:],
                    [0] + lerp_tiers((3.9, 11.6), 'T2', 'Iconic')[1:]),
          None, give(jump, paired=[jump_pair]))
fresh = active('JenkinsTendonsSprint', 'sprint', 'Fresh legs: extra movement speed.', E().fac('MoveSpeed', 1.2))
cyberware('JenkinsTendons', 'Jenkins\' tendons', 'Fast-twitch tendons: +30% movement speed, tapering to +10% after 50 seconds of continuous movement.', 'leg', 6, ('T2', 'Iconic'),
          E().armor(1.1, 2.5).fac('MoveSpeed', 1.1), {'freshSeconds': 50}, conditionals(cond('MovingFresh', fresh, threshold='freshSeconds')))
lo, hi = 'T2', 'Iconic'
cyberware('LeeroyLigamentSystem', 'Leeroy ligament system', 'Synthetic ligaments for a faster stride.', 'leg', 8, (lo, hi),
          E().armor((0.5, 1.3), (1.1, 2.9)).fac('MoveSpeed', plus1((0.12, 0.2), lo, hi)))
jump2 = jump_ability('CyberJumpCharges', 'jump', 10, charges=2, recharge=300)
jump2_pair = jump_ability('CyberJumpChargesPaired', 'jump (paired)', 20, charges=4, recharge=300)
cyberware('ReinforcedTendons', 'reinforced tendons', 'Tendons that store energy for two jumps in a row.', 'leg', 8, ('T2', 'Iconic'),
          E().armor(1.0, 2.3), None, give(jump2, paired=[jump2_pair]))

# ================================================================================================ operating systems
header('operating systems (operating_system/manifest.md): one per pawn')


def sandevistan(id, label, desc, cap, tiers, consc, dur, cd, effects, ext=''):
    e = effects.cap('Consciousness', consc).fac('MoveSpeed', 1 + consc)
    h = active(id + 'Active', label.lower() + ' active', 'Time crawls for this pawn.', e, '        <afterimages>true</afterimages>\n        <adrenalineGainFactor>0.5</adrenalineGainFactor>\n' + ext)
    ab = buff_ability(id, 'activate ' + label, 'Slow down time: much faster thinking and moving for the duration.', h, 80, ai_dist=40, adrenaline=25, ai_rule='CloseDistance')
    cyberware(id, label, desc, 'os', cap, tiers, None, {'duration': dur, 'cooldown': cd}, give(ab), os_type='Sandevistan')


sandevistan('ApogeeSandevistan', 'Apogee sandevistan', 'A top-end sandevistan. Downing or killing enemies while it runs keeps it going.', 44, ('T5', 'Iconic'), 1.7, 60, (60, 50),
            E().off('RPC_HeadshotDamageFactor', (0.15, 0.2)).off('RPC_RangedCritChance', (0.15, 0.2)).off('MeleeCritChance', (0.15, 0.2)).off('RPC_CritDamageFactor', (0.15, 0.2)),
            '        <onNeutralizeExtendFraction>0.1</onNeutralizeExtendFraction>\n        <onNeutralizeAdrenaline>0.22</onNeutralizeAdrenaline>\n')
sandevistan('FalconSandevistan', 'Falcon sandevistan', 'A military sandevistan that heals the user as they cut through enemies.', 39, ('T4', 'Iconic'), 1.4, (90, 100), (60, 40),
            E().fac('RPC_DamageFactorAll', (1.05, 1.10)).off('RPC_RangedCritChance', (0.05, 0.15)).off('MeleeCritChance', (0.05, 0.15)).off('RPC_CritDamageFactor', (0.05, 0.10)),
            '        <onNeutralizeExtendFraction>0.05</onNeutralizeExtendFraction>\n        <onNeutralizeHealFraction><min>0.10</min><max>0.12</max></onNeutralizeHealFraction>\n')
sandevistan('WarpDancerSandevistan', 'Warp Dancer sandevistan', 'A defensive sandevistan: mitigation and elemental resistance while it runs.', 14, ('T3', 'Iconic'), 0.4, (90, 100), (140, 80),
            E().off('RPC_MitigationChance', (0.14, 0.24)).off('RPC_MitigationStrength', (0.07, 0.15))
            .off('RPC_ThermalResistance', (0.3, 0.6)).off('RPC_ChemicalResistance', (0.3, 0.6)).off('RPC_ElectricalResistance', (0.3, 0.6)),
            '        <onNeutralizeExtendFraction>0.05</onNeutralizeExtendFraction>\n        <onNeutralizeHealFraction><min>0.10</min><max>0.12</max></onNeutralizeHealFraction>\n')
sandevistan('ZetatechSandevistan', 'Zetatech sandevistan', 'A sandevistan built for precision killing: more damage, especially to the head and organs.', 20, ('T2', 'Iconic'), 0.6, (90, 100), (140, 80),
            E().fac('RPC_DamageFactorAll', (1.06, 1.24)).off('RPC_HeadshotDamageFactor', (0.25, 0.4)).fac('RPC_OrganDamageFactor', (1.25, 1.4)))


def berserk(id, label, desc, cap, tiers, dur, cd, incoming, effects, ext=''):
    h = active(id + 'Active', 'berserk active', 'No pain, melee only, and very hard to put down.', effects,
               (f'        <incomingDamageFactor>{tv(incoming)}</incomingDamageFactor>\n' if incoming is not None else '')
               + '        <partHealthFloor>0.25</partHealthFloor>\n        <meleeOnly>true</meleeOnly>\n        <adrenalineGainFactor>1.5</adrenalineGainFactor>\n'
               + '        <healPerNeutralizeOnEnd>0.25</healPerNeutralizeOnEnd>\n' + ext,
               comps=BERSERK_LOOK, color='(1, 0.25, 0.25)', stages='    <stages Inherit="False">\n      <li>\n        <painFactor>0</painFactor>\n      </li>\n    </stages>\n')
    ab = buff_ability(id, 'activate ' + label, 'Go berserk: melee only, no pain, much harder to kill. Heals for every enemy downed or killed when it ends.', h, 80, ai_dist=8, adrenaline=40, ai_rule='Pinned')
    cyberware(id, label, desc, 'os', cap, tiers, None, {'duration': dur, 'cooldown': cd}, give(ab), os_type='Berserk')


lo, hi = 'T2', 'Iconic'
berserk('BioDyneBerserk', 'BioDyne berserk', 'A berserk that sharpens melee: faster swings and brutal crits.', 20, (lo, hi), (80, 110), (120, 70), (0.7, 0.5),
        E().fac('MeleeCooldownFactor', speed((0.15, 0.3), lo, hi)).off('MeleeCritChance', (0.05, 0.2)).off('RPC_CritDamageFactor', 1.0))
lo, hi = 'T4', 'Iconic'
berserk('MilitechBerserk', 'Militech berserk', 'Military berserk: invulnerable while it runs, hitting harder the closer the user is to death.', 35, (lo, hi), (110, 120), (80, 50), 0,
        E().fac('MeleeCooldownFactor', speed((0.25, 0.3), lo, hi)).fac('MoveSpeed', plus1((0.15, 0.2), lo, hi)),
        '        <lowHealthDamageBonus>0.5</lowHealthDamageBonus>\n')
lo, hi = 'T3', 'Iconic'
berserk('ZetatechBerserk', 'Zetatech berserk', 'A balanced berserk: tough and quick.', 16, (lo, hi), (90, 110), (90, 50), (0.65, 0.5),
        E().fac('MeleeCooldownFactor', speed((0.2, 0.3), lo, hi)))


# every cyberdeck has a base RAM bank by tier (quickhack RAM costs follow the wiki: 2-32), plus the deck's own bonus
DECK_BASE_RAM = [6, 8, 10, 12, 14, 16]


def cyberdeck(id, label, desc, cap, tiers, effects=None, values=None, note=None):
    lo, hi = tiers
    e = effects or E()
    if not any(st == 'RPC_RamMax' for st, _ in e.offsets):
        e.off('RPC_RamMax', (1, 10))
    e.offsets = [(st, [b + x for b, x in zip(DECK_BASE_RAM, lerp_tiers(v, lo, hi))]) if st == 'RPC_RamMax' else (st, v) for st, v in e.offsets]
    # quickhacks are learned from shards (gen_quickhacks.py); the deck itself grants Overclock
    cyberware(id, label, desc, 'os', cap, tiers, e, values, give('RPC_Ability_Overclock'), os_type='Cyberdeck', extra=QUICKHACK_EYES, note=note)


cyberdeck('BiotechCyberdeck', 'Biotech cyberdeck', 'A cyberdeck built around damage over time. From tier 3, quickhacks hit harder; from tier 4, monowire hits harder on burning or poisoned targets.', 16, ('T2', 'Iconic'),
          E().fac('RPC_QuickhackDamageFactor', [1, 1, 1.1, 1.1, 1.1, 1.1]),
          {'monowireVsDot': [0, 0, 0, 0.25, 0.25, 0.25], 'combatQuickhackDuration': (0.15, 0.6)},
          note='Biotech: overclock reset (t5) not implemented')
cyberdeck('CantoCyberdeck', 'Canto Mk.6 cyberdeck', 'A black-market cyberdeck of terrifying power. At iconic tier it unlocks the Blackwall Gateway quickhack.', 33, ('T5', 'Iconic'), E().off('RPC_RamMax', (8, 12)),
          {'blackwallGateway': [0, 0, 0, 0, 0, 1]})
cyberdeck('ParalineCyberdeck', 'Paraline cyberdeck', 'A netrunner deck that pairs with monowire. From tier 2, quickhacks hit harder; from tier 3, monowire gains damage for every RAM unit spent.', 14, ('T1', 'Iconic'),
          E().fac('RPC_QuickhackDamageFactor', [1, 1.1, 1.1, 1.1, 1.1, 1.1]), {'monowirePerUsedRam': [0, 0, 0.02, 0.02, 0.02, 0.02]},
          note='Paraline: smart-weapon upload acceleration (t4) and overclock damage (t5) wait for overclock / upload interaction')
cyberdeck('NetDriverCyberdeck', 'NetDriver Mk.1 cyberdeck', 'A combat cyberdeck: quickhacks hit much harder.', 25, ('T5', 'Iconic'),
          E().off('RPC_RamMax', (8, 12)).fac('RPC_QuickhackDamageFactor', (1.15, 1.3)), {'combatQuickhackDuration': (0.2, 0.5)})
cyberdeck('RavenCyberdeck', 'Raven Microcyber cyberdeck', 'A cyberdeck that spreads quickhacks further between targets.', 20, ('T3', 'Iconic'), E().off('RPC_RamMax', (4, 10)),
          {'quickhackSpreadDistance': [0, 0, 0.4, 0.6, 0.8, 1.0]}, note='Raven: instant spread (t4) and overclock spread chance (t5) not implemented')
cyberdeck('RipplerCyberdeck', 'Rippler cyberdeck', 'A cyberdeck with a large RAM bank at high tiers.', 16, ('T1', 'Iconic'),
          E().off('RPC_RamMax', [a + b for a, b in zip(lerp_tiers((1, 8), 'T1', 'Iconic'), [0, 0, 0, 5, 8, 12])]),
          note='Rippler: non-combat quickhack synergies (t2, t3) and overclock auto-upload (t5) wait for those systems')

# ================================================================================================ prefrontal cortex
header('prefrontal cortex (prefrontal_cortex/manifest.md): three max')
cyberware('Axolotl', 'Axolotl', 'Cuts all cyberware cooldowns a little every time the user damages an enemy.', 'pfc', 48, ('T4', 'Iconic'), None,
          {'cooldownReductionOnDamage': (0.05, 0.075)}, '      <li Class="RimPunkCE.HediffCompProperties_CooldownOnCombat" />\n')
cyberware('Bioconductor', 'bioconductor', 'Lets quickhacks land critical hits.', 'pfc', 16, ('T1', 'Iconic'), E().off('RPC_QuickhackCritChance', (0.15, 0.35)).off('RPC_RamMax', 4))
cyberware('Cox2CybersomaticOptimizer', 'COX-2 cybersomatic optimizer', 'Makes nearly every quickhack a critical hit.', 'pfc', 50, ('T3', 'Iconic'),
          E().off('RPC_QuickhackCritChance', (0.8, 1.0)).off('RPC_RamMax', [4, 4, 4, 5.3, 6.7, 8]))
cyberware('CamilloRamManager', 'Camillo RAM manager', 'Instantly restores RAM when it runs low.', 'pfc', 8, ('T4', 'Iconic'), E().off('RPC_RamMax', 2),
          {'ramThreshold': (0.15, 0.2), 'ramRestore': (0.20, 0.23), 'cooldown': 160},
          triggers(trig('RamBelow', [eff('RamFraction', key='ramRestore')], threshold='ramThreshold', cooldown='cooldown')))
cyberware('ExDisk', 'Ex-Disk', 'Extra RAM and faster quickhack uploads.', 'pfc', 10, ('T3', 'Iconic'), E().off('RPC_RamMax', (4, 6)).off('RPC_QuickhackUploadSpeed', (0.2, 0.35)))
kerenzikov_active = 'RPC_KerenzikovActive'
cyberware('KerenzikovBoostSystem', 'Kerenzikov boost system', 'Pushes Kerenzikov further: even more consciousness while it runs. Does nothing without a Kerenzikov.', 'pfc', 3, ('T3', 'Iconic'),
          None, None, conditionals(cond('ActivePresent', 'RPC_KerenzikovBoost', active=kerenzikov_active)))
active('KerenzikovBoost', 'Kerenzikov boost', 'Boosted Kerenzikov.', E().cap('Consciousness', (0.5, 1.0)))
cyberware('MechatronicCore', 'mechatronic core', 'Targeting routines for mechanoid weak points.', 'pfc', 5, ('T1', 'Iconic'), E().off('RPC_DamageVsMechanoids', (0.15, 0.4)).off('RPC_RamMax', (1, 2)))
cyberware('MemoryBoost', 'memory boost', 'Restores RAM every time the user damages an enemy.', 'pfc', 18, ('T2', 'Iconic'), E().off('RPC_RamMax', 1), {'ramGain': (0.4, 1.25)},
          triggers(trig('DealtDamage', [eff('RamFlat', key='ramGain')])))
lo, hi = 'T5', 'Iconic'
cyberware('QuantumTuner', 'quantum tuner', 'Whenever another cyberware ability is used, takes a large chunk off its cooldown. All other cyberware cools down faster.', 'pfc', 45, (lo, hi),
          E().fac('RPC_CyberwareCooldownFactor', minus1((0.10, 0.15), lo, hi)), {'restoreSeconds': (60, 100), 'cooldown': 120},
          triggers(trig('OtherAbilityUsed', [eff('RestoreOtherCooldown', key='restoreSeconds')], cooldown='cooldown')))
cyberware('RamReallocator', 'RAM reallocator', 'Instantly restores a big share of RAM when it runs low.', 'pfc', 40, ('T3', 'Iconic'), E().off('RPC_RamMax', 2),
          {'ramThreshold': (0.15, 0.2), 'ramRestore': (0.30, 0.45), 'cooldown': (190, 170)},
          triggers(trig('RamBelow', [eff('RamFraction', key='ramRestore')], threshold='ramThreshold', cooldown='cooldown')))
cyberware('SelfIce', 'Self-ICE', 'Intrusion countermeasures: automatically negates a hostile quickhack.', 'pfc', 5, ('T3', 'Iconic'), E().off('RPC_RamMax', [1, 1, 1, 1.3, 1.7, 2]),
          {'cooldown': (90, 40)}, '      <li Class="RimPunkCE.HediffCompProperties_SelfIce" />\n')

# ================================================================================================ skeleton
header('skeleton (skeleton/manifest.md): two max, whole body')


def skel(id, label, desc, cap, tiers, sharp, blunt, effects=None, values=None, comps='', note=None):
    e = effects or E()
    e.armor(sharp, blunt)
    cyberware(id, label, desc, 'skeleton', cap, tiers, e, values, comps, note=note)


skel('BionicJoints', 'bionic joints', 'Armoured replacement joints. Simple but effective.', 8, ('T1', 'Iconic'), (1.25, 6.5), (2.8, 14.6))
skel('DenseMarrow', 'dense marrow', 'Heavier bones that put more weight behind every blow, but melee builds adrenaline more slowly.', 16, ('T2', 'Iconic'), (0.9, 2.7), (2.0, 6.1),
     E().off('MeleeDamageFactor', (0.15, 0.27)).fac('RPC_AdrenalineGainMelee', 0.8))
skel('EpimorphicSkeleton', 'epimorphic skeleton', 'A regenerative skeleton that makes every body part tougher.', 40, ('T4', 'Iconic'), (2.8, 4.3), (4.8, 8.9),
     E().fac('RPC_MaxHealthFactor', (1.10, 1.13)))
active('FeenXBoost', 'Feen-X', 'RAM regenerating much faster.', E().fac('RPC_RamRegen', 3.5))
skel('FeenX', 'Feen-X', 'Boosts RAM regeneration a lot when RAM runs low.', 16, ('T1', 'Iconic'), (0.4, 0.9), (0.9, 2.3), None, {'ramBelow': (3, 7)},
     conditionals(cond('RamBelow', 'RPC_FeenXBoost', threshold='ramBelow')))
active('KineticFrameBoost', 'kinetic frame', 'Mitigation chance up while adrenaline is high.', E().off('RPC_MitigationChance', (0.08, 0.18)))
skel('KineticFrame', 'kinetic frame', 'Raises mitigation chance while adrenaline is above 50%.', 16, ('T1', 'Iconic'), (0.85, 2.85), (1.9, 5.2), None, {'adrenalineAbove': 0.5},
     conditionals(cond('AdrenalineAbove', 'RPC_KineticFrameBoost', threshold='adrenalineAbove')))
lo, hi = 'T3', 'Iconic'
skel('ParaBellum', 'Para Bellum', 'Reinforces all other armour.', 25, (lo, hi), (1.5, 7.5), (3.4, 16.9),
     E().fac('ArmorRating_Sharp', plus1((0.08, 0.12), lo, hi)).fac('ArmorRating_Blunt', plus1((0.08, 0.12), lo, hi)))
skel('RamRecoup', 'RAM recoup', 'Turns pain into RAM: taking damage restores RAM.', 14, ('T1', 'Iconic'), (0.5, 1.3), (1.1, 3.2), E().off('RPC_RamMax', [1, 1.3, 1.7, 2, 2, 2]),
     {'ramPerDamage': (0.02, 0.03)}, triggers(trig('TookDamage', [eff('RamPerDamage', key='ramPerDamage')])),
     note='RAM recoup: 2-3% of damage taken (the manifest 0.2-0.3% x10, damage numbers are /10 in this mod)')
cyberware('RaraAvis', 'Rara Avis', 'A rare reinforcement lattice that makes all armour far more effective.', 'skeleton', 45, (lo, hi),
          E().fac('ArmorRating_Sharp', plus1((0.3, 0.4), lo, hi)).fac('ArmorRating_Blunt', plus1((0.3, 0.4), lo, hi)))
lo, hi = 'T1', 'Iconic'
active('ScarCoalescerBoost', 'scar coalescer', 'Armour up while badly hurt.',
       E().fac('ArmorRating_Sharp', plus1((0.10, 0.22), lo, hi)).fac('ArmorRating_Blunt', plus1((0.10, 0.22), lo, hi)))
skel('ScarCoalescer', 'scar coalescer', 'Hardens scar tissue on the fly: more armour below 50% health.', 20, (lo, hi), (0.5, 1.3), (1.1, 3.2), None, {'healthBelow': 0.5},
     conditionals(cond('HealthBelow', 'RPC_ScarCoalescerBoost', threshold='healthBelow')))
active('ScarabBraced', 'scarab braced', 'Standing still: extra armour.', E().armor((1.5, 6.5), (3.4, 14.6)))
skel('Scarab', 'scarab', 'Locks the frame when the user stands still, adding armour.', 14, ('T1', 'Iconic'), (0.5, 2.3), (1.1, 3.2), None, None,
     conditionals(cond('StandingStill', 'RPC_ScarabBraced')))
skel('SpringJoints', 'spring joints', 'Spring-loaded joints that turn blocked hits into adrenaline.', 16, ('T2', 'Iconic'), (0.9, 1.3), (2.0, 3.2), E().off('RPC_MitigationStrength', (0.10, 0.18)))
lo, hi = 'T1', 'Iconic'
skel('TitaniumBones', 'titanium bones', 'Titanium-laced bones: the user can carry far more.', 6, (lo, hi), (0.5, 1.3), (1.1, 3.2),
     E().fac('CarryWeight', plus1((0.3, 0.66), lo, hi)).fac('CarryBulk', plus1((0.3, 0.66), lo, hi)))

# ================================================================================================ nervous system
header('nervous system (nervous_system/manifest.md): three max')
h = active('AdrenalineConverterActive', 'adrenaline converter', 'Combat adrenaline: faster movement.', E().fac('MoveSpeed', plus1((0.2, 0.4), 'T1', 'Iconic')))
cyberware('AdrenalineConverter', 'adrenaline converter', 'A burst of speed on entering combat.', 'nervous', 6, ('T1', 'Iconic'), None, {'duration': (30, 90)},
          triggers(trig('EnteredCombat', [eff('ApplyActive', hediff=h)])))
h = active('AdrenoTriggerActive', 'adreno-trigger', 'Combat adrenaline: faster movement.', E().fac('MoveSpeed', plus1((0.2, 0.3), 'T3', 'Iconic')))
cyberware('AdrenoTrigger', 'adreno-trigger', 'A long-lasting speed boost on entering combat.', 'nervous', 20, ('T3', 'Iconic'), None, {'duration': (100, 350)},
          triggers(trig('EnteredCombat', [eff('ApplyActive', hediff=h)])))
cyberware('DeepFieldVisualInterface', 'deep-field visual interface', 'Long-range targeting: crit chance grows with distance to the target, up to the maximum at 40 tiles.', 'nervous', 40, ('T3', 'Iconic'),
          None, {'distanceCrit': (0.7, 1.0), 'distanceCritRange': 40})
active('KerenzikovActive', 'Kerenzikov', 'Reflexes in overdrive after a dodge.', E().cap('Consciousness', 1.2))
cyberware('Kerenzikov', 'Kerenzikov', 'Kicks in after the user dodges a melee attack: a huge burst of consciousness.', 'nervous', 12, ('T2', 'Iconic'), None,
          {'duration': (20, 37.5), 'cooldown': (16, 12)}, triggers(trig('Dodged', [eff('ApplyActive', hediff='RPC_KerenzikovActive')], cooldown='cooldown')))
cyberware('NeoFiber', 'NeoFiber', 'Synthetic muscle fibre that improves mitigation.', 'nervous', 14, ('T3', 'Iconic'), E().off('RPC_MitigationChance', (0.08, 0.11)).off('RPC_MitigationStrength', (0.05, 0.11)))
h = active('ReflexTunerActive', 'reflex tuner', 'Near death: reflexes in overdrive.', E().cap('Consciousness', (0.4, 1.2)))
cyberware('ReflexTuner', 'reflex tuner', 'When health drops below 25%, a burst of consciousness.', 'nervous', 5, ('T1', 'Iconic'), None,
          {'healthBelow': 0.25, 'duration': (20, 45), 'cooldown': (120, 70)},
          triggers(trig('HealthBelow', [eff('ApplyActive', hediff=h)], threshold='healthBelow', cooldown='cooldown')))
h = active('RevulsorActive', 'revulsor', 'Near death: reflexes in overdrive and wounds ignored.', E().cap('Consciousness', (0.8, 1.2)),
           stages='    <stages Inherit="False">\n      <li>\n        <painFactor>0</painFactor>\n      </li>\n    </stages>\n')
cyberware('Revulsor', 'revulsor', 'When health drops below 25%, a big burst of consciousness and no slowdown from pain.', 'nervous', 35, ('T3', 'Iconic'), None,
          {'healthBelow': 0.25, 'duration': (35, 45), 'cooldown': (100, 70)},
          triggers(trig('HealthBelow', [eff('ApplyActive', hediff=h)], threshold='healthBelow', cooldown='cooldown')),
          note='revulsor: "movement is not slowed" = no pain while active')
active('StabberBlade', 'stabber', 'Blade crits up.', E().off('MeleeCritChance', (0.1, 0.2)))
cyberware('Stabber', 'stabber', 'More crits with blades and grenades.', 'nervous', 12, ('T2', 'Iconic'), E().off('RPC_CritChanceGrenades', (0.1, 0.2)), None,
          conditionals(cond('WieldingBlade', 'RPC_StabberBlade')))
h = active('SynapticAcceleratorActive', 'synaptic accelerator', 'Targeted: reflexes in overdrive.', E().cap('Consciousness', (0.4, 1.0)))
cyberware('SynapticAccelerator', 'synaptic accelerator', 'A burst of consciousness when the user comes under attack.', 'nervous', 5, ('T2', 'Iconic'), None,
          {'duration': (20, 45), 'cooldown': 120}, triggers(trig('EnteredCombat', [eff('ApplyActive', hediff=h)], cooldown='cooldown')),
          note='synaptic accelerator: "when an enemy first targets the pawn" = entering combat')
lo, hi = 'T1', 'Iconic'
h = active('TyrosineInjectorActive', 'tyrosine rush', 'After a takedown: better headshots and faster movement.',
           E().off('RPC_HeadshotDamageFactor', (0.10, 0.21)).fac('MoveSpeed', plus1((0.05, 0.11), lo, hi)))
cyberware('TyrosineInjector', 'tyrosine injector', 'Downing or killing an enemy boosts headshot damage and movement speed.', 'nervous', 8, (lo, hi), None,
          {'duration': 150}, triggers(trig('Neutralized', [eff('ApplyActive', hediff=h)])))
cyberware('VisualCortexSupport', 'visual cortex support', 'Crit chance grows with distance to the target, up to the maximum at 30 tiles.', 'nervous', 20, ('T1', 'Iconic'),
          None, {'distanceCrit': (0.16, 0.30), 'distanceCritRange': 30})

# ================================================================================================ circulatory system
header('circulatory system (circulatory_system/manifest.md): three max')
cyberware('AdrenalineBooster', 'adrenaline booster', 'Floods the user with adrenaline whenever they take down an enemy in melee.', 'circulatory', 14, ('T1', 'Iconic'), None,
          {'adrenaline': (0.10, 0.25)}, triggers(trig('Neutralized', [eff('Adrenaline', key='adrenaline')], weapon='Melee')))
cyberware('BlackMamba', 'Black Mamba', 'A poison-handling system: more damage against poisoned enemies, almost immune to toxins.', 'circulatory', 16, ('T3', 'Iconic'),
          E().fac('RPC_DamageVsPoisoned', (1.10, 1.22)).off('RPC_ChemicalResistance', 0.9),
          note='Black Mamba: "-90% poison damage" = 90% chemical resistance')
h = active('BloodPumpActive', 'blood pump', 'Regenerating health.', None, '        <healPerSecond><min>0.9</min><max>2.3</max></healPerSecond>\n', color='(1, 0.4, 0.4)')
ab = buff_ability('BloodPump', 'blood pump', 'Instantly restore some health, then regenerate for 60 seconds.', h, 60, ai_dist=40,
                  extra_comps='      <li Class="RimPunkCE.CompProperties_AbilityCyberHeal" />\n')
cyberware('BloodPump', 'blood pump', 'An auxiliary pump that flushes the user\'s system with regenerative fluid on demand.', 'circulatory', 15, ('T2', 'Iconic'), None,
          {'instantHeal': (4.5, 11), 'duration': 60, 'cooldown': 100}, give(ab))
lo, hi = 'T1', 'Iconic'
cyberware('ClutchPadding', 'clutch padding', 'Recoil feedback padding: shooting builds adrenaline faster.', 'circulatory', 12, (lo, hi), E().fac('RPC_AdrenalineGainRanged', plus1((0.07, 0.20), lo, hi)))
cyberware('ElectromagRecycler', 'electromag recycler', 'Recycles energy from tech weapon hits into health and adrenaline.', 'circulatory', 40, ('T3', 'Iconic'), None,
          {'gain': (0.025, 0.05)}, triggers(trig('DealtDamage', [eff('HealFraction', key='gain'), eff('Adrenaline', key='gain')], weapon='Tech')))
cyberware('FeedbackCircuit', 'feedback circuit', 'Heals the user a little with every tech weapon hit.', 'circulatory', 14, ('T3', 'Iconic'), None,
          {'heal': (0.015, 0.035)}, triggers(trig('DealtDamage', [eff('HealFraction', key='heal')], weapon='Tech')))
cyberware('HealOnKill', 'heal-on-kill', 'Heals the user whenever they down or kill an enemy.', 'circulatory', 10, ('T1', 'Iconic'), None,
          {'heal': (0.03, 0.075)}, triggers(trig('Neutralized', [eff('HealFraction', key='heal')])))
lo, hi = 'T3', 'Iconic'
cyberware('IsometricStabilizer', 'isometric stabilizer', 'Keeps the body primed: all adrenaline builds faster.', 'circulatory', 40, (lo, hi), E().fac('RPC_AdrenalineGain', plus1((0.13, 0.20), lo, hi)))
lo, hi = 'T1', 'Iconic'
cyberware('Microrotors', 'microrotors', 'Tiny rotors in the arms for faster melee swings.', 'circulatory', 12, (lo, hi), E().fac('MeleeCooldownFactor', speed((0.10, 0.25), lo, hi)))
cyberware('SecondHeart', 'Second Heart', 'A backup heart. When the user would die or go down, it brings them back at full health.', 'circulatory', 30, ('T4', 'Iconic'), None,
          {'cooldown': (600, 440)}, '      <li Class="RimPunkCE.HediffCompProperties_SecondHeart" />\n')
active('ThreatEvacBoost', 'threat evac', 'Badly hurt: running for it.', E().fac('MoveSpeed', plus1((0.05, 0.29), lo, hi)))
active('ThreatEvacBoostCritical', 'threat evac (critical)', 'Nearly dead: running for it.', E().fac('MoveSpeed', plus1((0.10, 0.10), lo, hi)))
cyberware('ThreatEvac', 'ThreatEvac', 'Faster movement once health drops to 25%, more the lower it goes.', 'circulatory', 5, (lo, hi), None, {'below': 0.25, 'belowCritical': 0.1},
          conditionals(cond('HealthBelow', 'RPC_ThreatEvacBoost', threshold='below'), cond('HealthBelow', 'RPC_ThreatEvacBoostCritical', threshold='belowCritical')),
          note='ThreatEvac: the "more the lower health goes" scaling is two steps (25% and 10% health)')

# ================================================================================================ integumentary system
header('integumentary system (integumentary_system/manifest.md): three max, whole body')


def skin(id, label, desc, cap, tiers, sharp, blunt, effects=None, values=None, comps='', note=None):
    e = effects or E()
    e.armor(sharp, blunt)
    cyberware(id, label, desc, 'integumentary', cap, tiers, e, values, comps, note=note)


skin('Carapace', 'carapace', 'Layered subdermal plates that work best against hits from the side and rear.', 16, ('T2', 'Iconic'), (1.0, 2.0), (3.0, 6.0), None,
     {'rearArmorEffectiveness': (0.20, 0.32)})
skin('CellularAdapter', 'cellular adapter', 'Adaptive skin that gets better the more its user knows about crafting: explosion resistance and tech weapon damage per crafting level.', 10, ('T2', 'Iconic'),
     (2.0, 4.0), (5.0, 8.0), None, {'explosionResistPerCrafting': 0.01, 'techDamagePerCrafting': 0.005},
     note='cellular adapter: the health item recharge part waits for health items')
skin('Chitin', 'chitin', 'An extremely durable subcutaneous shell made of genetically modified chitin. Wounds heal twice as fast.', 50, ('T3', 'Iconic'), (7, 10), (7.0, 12.0),
     E().fac('InjuryHealingFactor', 2.0))
lo, hi = 'T1', 'Iconic'
sharp_c, blunt_c = lerp_tiers((0.9, 2.7), lo, hi), lerp_tiers((2.0, 6.1), lo, hi)
mult = lerp_tiers((2.0, 2.4), lo, hi)
active('CogitoLatticeBoost', 'cogito lattice', 'Low RAM: the lattice hardens.', E().armor([s * m for s, m in zip(sharp_c, mult)], [b * m for b, m in zip(blunt_c, mult)]))
skin('CogitoLattice', 'cogito lattice', 'Armour that hardens to triple strength when available RAM is low.', 12, (lo, hi), (0.5, 1.0), (1.0, 2.0), None, {'ramBelow': (2, 10)},
     conditionals(cond('RamBelow', 'RPC_CogitoLatticeBoost', threshold='ramBelow')))
h = active('CountershellActive', 'countershell', 'Under heavy fire: mitigation up.', E().off('RPC_MitigationChance', (0.30, 0.50)))
skin('Countershell', 'countershell', 'When the user loses 35% of their health within 30 seconds, mitigation chance rises sharply for 40 seconds.', 12, ('T1', 'Iconic'),
     (0.85, 2.85), (1.9, 6.4), None, {'healthLost': 0.35, 'window': 30, 'duration': 40, 'cooldown': 12},
     triggers(trig('HealthLostWithin', [eff('ApplyActive', hediff=h)], threshold='healthLost', window='window', cooldown='cooldown')))
h = active('DefenzikovActive', 'Defenzikov', 'After Kerenzikov: mitigation up.', E().off('RPC_MitigationChance', (0.50, 0.90)))
skin('Defenzikov', 'Defenzikov', 'When Kerenzikov ends, mitigation chance rises sharply for a while.', 20, ('T3', 'Iconic'), (0.4, 1.5), (1.0, 4.0), None, {'duration': (30, 40)},
     triggers(trig('ActiveEnded', [eff('ApplyActive', hediff=h)], active='RPC_KerenzikovActive')))
skin('NanoPlating', 'nano-plating', 'Reactive plating that can stop a bullet outright, more often right after a dodge, dash or jump. At most three blocks in a short span.', 20, ('T2', 'Iconic'),
     (1.0, 1.6), (3.4, 4.2), None, {'blockChance': (0.04, 0.07), 'bonusDuration': (14, 17), 'blockWindow': (65, 50)},
     '      <li Class="RimPunkCE.HediffCompProperties_NanoPlating" />\n')
camo = active('OpticalCamoActive', 'optical camo', 'Invisible.', None, '', comps='      <li Class="HediffCompProperties_Invisibility">\n        <visibleToPlayer>true</visibleToPlayer>\n        <fadeDurationTicks>30</fadeDurationTicks>\n      </li>\n', color='(0.6, 0.8, 1)')
ab = buff_ability('OpticalCamo', 'optical camo', 'Turn invisible.', camo, 40, ai_dist=20)
skin('OpticalCamo', 'optical camo', 'Light-bending skin that makes the user invisible on demand.', 20, ('T2', 'Iconic'), (0.2, 0.6), (1.0, 1.6), None,
     {'duration': (40, 70), 'cooldown': (140, 100)}, give(ab))
lo, hi = 'T4', 'Iconic'
skin('PainEditor', 'pain editor', 'Filters pain signals and dampens every hit a little.', 35, (lo, hi), (2.0, 3.6), (4.8, 6.5), E().fac('RPC_IncomingDamageFactor', minus1((0.06, 0.07), lo, hi)))
skin('Painducer', 'painducer', 'Spreads part of every hit out over the next 8 seconds as small hits to the body and limbs. Whatever is still held back when the user goes down is dropped.', 30, ('T4', 'Iconic'), (5.7, 6.9), (8.2, 10.3), None, {'delayedFraction': (0.25, 0.30)})
skin('PeripheralInverse', 'peripheral inverse', 'The closer an attacker is, the less damage they do: up to -34-45% within 3 tiles, fading to nothing at 6.', 30, ('T4', 'Iconic'),
     (0.4, 1.8), (0.9, 3.1), None, {'closeRangeReduction': (0.34, 0.45)})
skin('ProxiShield', 'ProxiShield', 'The closer an attacker is, the less damage they do: up to -8-20% within 3 tiles, fading to nothing at 6.', 10, ('T1', 'Iconic'),
     (0.4, 1.8), (0.9, 3.1), None, {'closeRangeReduction': (0.08, 0.20)})
active('RangeGuardBoost', 'range guard', 'No enemies close: extra armour.', E().armor((1.5, 3.5), (2.4, 7.1)))
skin('RangeGuard', 'RangeGuard', 'Extra armour while no enemy is within 6 tiles.', 14, ('T1', 'Iconic'), (0.5, 1.7), (1.1, 3.8), None, {'clearRadius': 6},
     conditionals(cond('NoEnemiesWithin', 'RPC_RangeGuardBoost', threshold='clearRadius')))
skin('ShockNAwe', 'Shock-n-Awe', 'Taking damage can release a large electroshock that hits nearby enemies.', 25, ('T1', 'Iconic'), (2.2, 3.1), (3.9, 5.4), None,
     {'shockChance': 0.1, 'shockDamage': (13.5, 50)}, triggers(trig('TookDamage', [eff('Electroshock', key='shockDamage', radius=5)], chance='shockChance')))
skin('SubdermalArmor', 'subdermal armor', 'Armour plates under the skin. Simple but effective.', 5, ('T1', 'Iconic'), (0.85, 2.45), (1.9, 5.5))

# ------------------------------------------------------------------------------------------------ write
with open(OUT, 'w', encoding='utf-8', newline='\n') as fh:
    fh.write('<?xml version="1.0" encoding="utf-8"?>\n<!-- GENERATED by Source/Tools/gen_cyberware.py. Do not edit; change the generator and rerun. -->\n<Defs>\n')
    fh.write(''.join(out))
    fh.write('</Defs>\n')
n = sum(1 for s in out if '<ThingDef ParentName="RPC_CyberwareItemBase">' in s)
print(f'{n} cyberware -> {os.path.relpath(OUT, ROOT)}')
