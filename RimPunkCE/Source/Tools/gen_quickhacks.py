"""
Quickhack generator (prompts/quickhacks/manifest.md -> mod defs).

Output (overwritten every run, do not hand-edit): Common/Defs/Quickhacks/Generated_Quickhacks.xml
  - one AbilityDef per quickhack (RimPunkCE.QuickhackAbility + QuickhackExtension; the learned tier picks the numbers)
  - the effect hediffs (RimPunkCE.Hediff_Quickhack: tiered debuffs, DoT, stacks)
  - one shard item per tier (learn / upgrade the quickhack, crafted at the fabrication bench)
Framework defs (category, Overclock, mental states, shard base) live in Quickhacks_Core.xml.

Tiered numbers: scalar, (t1, t5) over the hack's own tier range (layout.md), or a list of 6 (T1..T5, Iconic).
Iconic = T5 for every quickhack except Blackwall Gateway. Conversions as the cyberware: damage /10, 1 m = 1 tile, seconds.
"""
import os
from xml.sax.saxutils import escape

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Common', 'Defs', 'Quickhacks', 'Generated_Quickhacks.xml')

TIERS = ['T1', 'T2', 'T3', 'T4', 'T5', 'Iconic']


def tiers6(v, lo='T1', hi='T5'):
    """6 per-tier values; below lo = value at lo, above hi = value at hi."""
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


def per(v, lo='T1', hi='T5'):
    return '<perTier>' + ''.join(f'<li>{fmt(x)}</li>' for x in tiers6(v, lo, hi)) + '</perTier>'


# ------------------------------------------------------------------------------------------------ effect hediffs

HEDIFFS = []


def hediff(defname, label, desc, stat_factors=(), cap_factors=(), q=None):
    """Hediff_Quickhack. stat_factors / cap_factors: (stat or capacity, tiered value)."""
    eff = ''
    if stat_factors:
        eff += '          <statFactors>\n' + ''.join(
            f'            <li><stat>{st}</stat><value>{per(v)}</value></li>\n' for st, v in stat_factors) + '          </statFactors>\n'
    if cap_factors:
        eff += '          <capMods>\n' + ''.join(
            f'            <li><capacity>{c}</capacity><postFactor>{per(v)}</postFactor></li>\n' for c, v in cap_factors) + '          </capMods>\n'
    active = f'''      <li Class="RimPunkCE.ActiveEffectExtension">
        <effects>
{eff}        </effects>
      </li>
''' if eff else ''
    qx = ''.join(f'        <{k}>{str(v).lower() if isinstance(v, bool) else v}</{k}>\n' for k, v in (q or {}).items())
    HEDIFFS.append(f'''  <HediffDef>
    <defName>{defname}</defName>
    <label>{escape(label)}</label>
    <description>{escape(desc)}</description>
    <hediffClass>RimPunkCE.Hediff_Quickhack</hediffClass>
    <defaultLabelColor>(0.35, 0.65, 1)</defaultLabelColor>
    <isBad>true</isBad>
    <everCurableByItem>false</everCurableByItem>
    <stages><li /></stages>
    <modExtensions>
{active}      <li Class="RimPunkCE.QuickhackHediffExtension">
{qx}      </li>
    </modExtensions>
  </HediffDef>
''')


hediff('RPC_QH_Overheat', 'overheat', 'Quickhacked: burning from the inside.', q={'dotDamage': 'Burn', 'refreshOnReapply': True})
hediff('RPC_QH_Contagion', 'contagion', 'Quickhacked: a virus leaks toxic cyberware fluids.', q={'dotDamage': 'RPC_Toxic', 'maxStacksT5': 2})
hediff('RPC_QH_CyberwareMalfunction', 'cyberware malfunction', 'Quickhacked: cyberware abilities are disabled and every stack makes the pawn take more damage.',
       q={'dotDamage': 'RPC_Electrical', 'maxStacks': 5, 'incomingDamagePerStack': 0.05, 'blocksCyberwareAbilities': True})
hediff('RPC_QH_CrippleMovement', 'cripple movement', 'Quickhacked: the legs barely respond, movement cyberware is offline.',
       stat_factors=[('MoveSpeed', 0.05)], q={'blocksMovementAbilities': True})
hediff('RPC_QH_RebootOptics', 'reboot optics', 'Quickhacked: the optics are rebooting.', cap_factors=[('Sight', (0.5, 0.1))])
hediff('RPC_QH_WeaponGlitch', 'weapon glitch', 'Quickhacked: the weapon is malfunctioning and smart targeting is offline.',
       stat_factors=[('ShootingAccuracyPawn', (0.7, 0.4)), ('AimingAccuracy', (0.7, 0.4))], q={'disablesSmartWeapons': True})
hediff('RPC_QH_SonicShock', 'sonic shock', 'Quickhacked: deafened, comms scrambled, can\'t upload quickhacks.',
       cap_factors=[('Hearing', 0)], q={'blocksQuickhacks': True})
hediff('RPC_QH_Baited', 'baited', 'Quickhacked: lured by a fake signal.', q={'refreshOnReapply': True})
hediff('RPC_QH_Pinged', 'pinged', 'Quickhacked: position revealed to the netrunner.', q={'refreshOnReapply': True})
hediff('RPC_QH_MemoryWiped', 'memory wiped', 'Quickhacked: short-term memory erased.')
hediff('RPC_QH_SystemCollapse', 'system collapse', 'Quickhacked: the nervous system has crashed.', cap_factors=[('Consciousness', 0.05)])


# ------------------------------------------------------------------------------------------------ quickhacks

QUICKHACKS = []

# SoundDef played at the target when the upload lands (gen_sounds.py); the others get the generic "uploaded" chirp
LAND_SOUND = {
    'ShortCircuit': 'RPC_Snd_QhShortCircuit', 'Overheat': 'RPC_Snd_QhOverheat', 'Contagion': 'RPC_Snd_QhContagion',
    'SynapseBurnout': 'RPC_Snd_QhSynapseBurnout', 'CyberwareMalfunction': 'RPC_Snd_QhCyberwareMalfunction',
    'CrippleMovement': 'RPC_Snd_QhCrippleMovement', 'RebootOptics': 'RPC_Snd_QhRebootOptics', 'WeaponGlitch': 'RPC_Snd_QhWeaponGlitch',
    'Ping': 'RPC_Snd_QhPing', 'SonicShock': 'RPC_Snd_QhSonicShock', 'MemoryWipe': 'RPC_Snd_QhMemoryWipe', 'Bait': 'RPC_Snd_QhBait',
    'RequestBackup': 'RPC_Snd_QhRequestBackup', 'Suicide': 'RPC_Snd_QhSuicide', 'SystemCollapse': 'RPC_Snd_QhSystemCollapse',
    'DetonateGrenade': 'RPC_Snd_QhDetonate', 'Cyberpsychosis': 'RPC_Snd_QhCyberpsychosis', 'BlackwallGateway': 'RPC_Snd_QhBlackwall',
}


def comp(cls, **kw):
    body = ''.join(f'        <{k}>{str(v).lower() if isinstance(v, bool) else v}</{k}>\n' for k, v in kw.items())
    return f'      <li Class="RimPunkCE.CompProperties_Quickhack{cls}">\n{body}      </li>\n' if body else f'      <li Class="RimPunkCE.CompProperties_Quickhack{cls}" />\n'


def quickhack(id, label, desc, category, tiers, ram, upload, cooldown, comps, duration=None, magnitude=None, spread=None,
              spread_radius=8, untraceable=False, untraceable_from=None, mechs=True, humanlike=False, blocked=None,
              ai_never=False, shard_cost=1.0):
    lo, hi = tiers
    ext = f'''        <category>{category}</category>
        <minTier>{lo}</minTier>
        <maxTier>{hi}</maxTier>
        <ram>{per([round(x) for x in tiers6(ram, lo, hi)])}</ram>
        <upload>{per(upload, lo, hi)}</upload>
'''
    if duration is not None: ext += f'        <duration>{per(duration, lo, hi)}</duration>\n'
    if magnitude is not None: ext += f'        <magnitude>{per(magnitude, lo, hi)}</magnitude>\n'
    if spread is not None: ext += f'        <spread>{per(spread, lo, hi)}</spread>\n        <spreadRadius>{spread_radius}</spreadRadius>\n'
    if untraceable: ext += '        <untraceable>true</untraceable>\n'
    if untraceable_from: ext += f'        <untraceableFromTier>{untraceable_from}</untraceableFromTier>\n'
    if not mechs: ext += '        <affectsMechanoids>false</affectsMechanoids>\n'
    if humanlike: ext += '        <humanlikeOnly>true</humanlikeOnly>\n'
    if blocked: ext += f'        <blockedIfTargetHas>{blocked}</blockedIfTargetHas>\n'
    if ai_never: ext += '        <aiNever>true</aiNever>\n'
    if id in LAND_SOUND: ext += f'        <landSound>{LAND_SOUND[id]}</landSound>' + chr(10)
    QUICKHACKS.append(dict(id=id, label=label, desc=desc, category=category, lo=lo, hi=hi, shard_cost=shard_cost, xml=f'''  <AbilityDef>
    <defName>RPC_Quickhack_{id}</defName>
    <label>{escape(label)}</label>
    <description>{escape(desc)}</description>
    <abilityClass>RimPunkCE.QuickhackAbility</abilityClass>
    <category>RPC_Quickhacks</category>
    <iconPath>UI/RimPunk/Placeholder_Ability</iconPath>
    <showPsycastEffects>false</showPsycastEffects>
    <sendMessageOnCooldownComplete>false</sendMessageOnCooldownComplete>
    <cooldownTicksRange>{int(cooldown * 60)}</cooldownTicksRange>
    <aiCanUse>{str(not ai_never).lower()}</aiCanUse>
    <ai_IsOffensive>true</ai_IsOffensive>
    <displayGizmoWhileUndrafted>false</displayGizmoWhileUndrafted>
    <verbProperties>
      <verbClass>RimPunkCE.Verb_Quickhack</verbClass>
      <label>upload</label>
      <range>24.9</range>
      <warmupTime>{fmt(tiers6(upload, lo, hi)[TIERS.index(lo)])}</warmupTime>
      <requireLineOfSight>true</requireLineOfSight>
      <targetParams>
        <canTargetPawns>true</canTargetPawns>
        <canTargetSelf>false</canTargetSelf>
        <canTargetBuildings>false</canTargetBuildings>
        <canTargetLocations>false</canTargetLocations>
      </targetParams>
    </verbProperties>
    <comps>
      <li Class="RimPunkCE.CompProperties_QuickhackCore" />
{''.join(comps)}    </comps>
    <modExtensions>
      <li Class="RimPunkCE.QuickhackExtension">
{ext}      </li>
    </modExtensions>
  </AbilityDef>
'''))


# == combat ==
quickhack('ShortCircuit', 'short circuit', 'Overload the target\'s systems: electrical damage, double against mechanoids. Tier 5: +3s to control quickhacks already on the target, +10% damage per Cyberware Malfunction stack, and an EMP burst that keeps shocking.',
          'Combat', ('T1', 'T5'), ram=(3, 10), upload=(1.5, 0.5), cooldown=3, magnitude=(7.8, 26),
          comps=[comp('Damage', damageDef='RPC_Electrical', mechanoidFactor=2, t5MalfunctionBonus=True, t5Emp=True, t5ExtendControlSeconds=3)])
quickhack('Overheat', 'overheat', 'Set the target on fire from the inside: burn damage every second. Uploading again refreshes it.',
          'Combat', ('T1', 'T5'), ram=(3, 9), upload=2, cooldown=4, duration=8, magnitude=(2.9, 12), blocked='RPC_QH_Overheat',
          comps=[comp('Hediff', hediff='RPC_QH_Overheat', igniteSize=0.4)])
quickhack('Contagion', 'contagion', 'A virus that leaks toxic cyberware fluids: toxic damage every second, spreading to nearby enemies. No effect on mechanoids. Tier 5: +15% damage per spread, stacks twice.',
          'Combat', ('T2', 'T5'), ram=(5, 12), upload=2, cooldown=5, duration=8, magnitude=(1.5, 3.8), spread=(2, 4), spread_radius=8,
          mechs=False, blocked='RPC_QH_Contagion',
          comps=[comp('Hediff', hediff='RPC_QH_Contagion', t5SpreadBonus=0.15)])
quickhack('SynapseBurnout', 'synapse burnout', 'Fry the target\'s synapses: heavy damage, +3% for every 1% of health the target is missing. Tier 5: +10% damage per RAM spent (max +400%, doubled while overclocked).',
          'Combat', ('T3', 'T5'), ram=(10, 16), upload=3, cooldown=6, magnitude=(8, 12),
          comps=[comp('Damage', damageDef='Blunt', perMissingHealth=0.03, t5PerRamBonus=0.1, t5PerRamBonusMax=4)])

# == control ==
quickhack('CyberwareMalfunction', 'cyberware malfunction', 'Disable the target\'s cyberware abilities. Stacks up to 5 times, each stack +5% damage taken. Tier 5: 2 stacks make it permanent, 3+ deal electrical damage over time, 5 overload the cyberware for heavy damage.',
          'Control', ('T1', 'T5'), ram=(2, 4), upload=0.3, cooldown=2, duration=10, blocked='RPC_QH_CyberwareMalfunction',
          comps=[comp('Hediff', hediff='RPC_QH_CyberwareMalfunction', malfunctionStacks=True, aiNeedsChrome=True)])
quickhack('CrippleMovement', 'cripple movement', 'The target can barely move, and movement cyberware (dashes, jumps, sandevistans) goes offline.',
          'Control', ('T1', 'T5'), ram=(2, 6), upload=0.3, cooldown=3, duration=(6, 12), blocked='RPC_QH_CrippleMovement',
          comps=[comp('Hediff', hediff='RPC_QH_CrippleMovement')])
quickhack('RebootOptics', 'reboot optics', 'Blind the target: sight drops by 50% (tier 1) to 90% (tier 5), wrecking its aim.',
          'Control', ('T1', 'T5'), ram=(2, 7), upload=0.3, cooldown=3, duration=(8, 15), blocked='RPC_QH_RebootOptics',
          comps=[comp('Hediff', hediff='RPC_QH_RebootOptics', aiNeedsRangedWeapon=True)])
quickhack('WeaponGlitch', 'weapon glitch', 'The target\'s weapon malfunctions: -30% (tier 1) to -60% (tier 5) accuracy and no smart targeting. Tier 5: the weapon blows up in its hands.',
          'Control', ('T1', 'T5'), ram=(2, 6), upload=0.3, cooldown=3, duration=(8, 14), blocked='RPC_QH_WeaponGlitch',
          comps=[comp('Hediff', hediff='RPC_QH_WeaponGlitch', aiNeedsRangedWeapon=True), comp('DestroyWeapon')])

# == covert (reworked for RimWorld) ==
quickhack('Ping', 'ping', 'Reveal every enemy within 20 tiles of the target, through fog and walls, and mark them for the duration.',
          'Covert', ('T1', 'T5'), ram=(4, 7), upload=1, cooldown=5, duration=(8, 16), untraceable=True, ai_never=True,
          comps=[comp('Reveal', radius=20, markerHediff='RPC_QH_Pinged')])
quickhack('SonicShock', 'sonic shock', 'Deafen the target and scramble its comms: it can\'t upload quickhacks. Tier 5: knocks out a target already hit by Memory Wipe and Reboot Optics.',
          'Covert', ('T1', 'T5'), ram=(2, 6), upload=2, cooldown=4, duration=(15, 30), untraceable=True, blocked='RPC_QH_SonicShock',
          comps=[comp('Hediff', hediff='RPC_QH_SonicShock', t5SonicKnockout=True, aiNeedsCyberdeck=True)])
quickhack('MemoryWipe', 'memory wipe', 'The target forgets the fight and wanders off for the duration (not mechanoids or cyberpsychos). Tier 5: every enemy within 15 tiles forgets too.',
          'Covert', ('T1', 'T5'), ram=(8, 32), upload=(4, 10), cooldown=10, duration=10, untraceable=True, mechs=False, ai_never=True,
          blocked='RPC_QH_MemoryWiped',
          comps=[comp('MentalState', state='RPC_MemoryWiped', markerHediff='RPC_QH_MemoryWiped', t5Radius=15)])
quickhack('Bait', 'bait', 'Lure the target to the netrunner: it walks over instead of fighting.',
          'Covert', ('T1', 'T5'), ram=(2, 4), upload=0.3, cooldown=3, duration=15, untraceable=True, ai_never=True,
          comps=[comp('Hediff', hediff='RPC_QH_Baited'), comp('Lure')])
quickhack('RequestBackup', 'request backup', 'Call one enemy within 20 tiles to the target\'s position (up to 4 if the target is baited).',
          'Covert', ('T2', 'T5'), ram=(3, 6), upload=2, cooldown=5, duration=15, ai_never=True,
          comps=[comp('Lure', backup=True, backupRadius=20, backupCount=1, backupCountIfBaited=4)])

# == ultimate ==
quickhack('Suicide', 'suicide', 'The target puts its gun to its head and pulls the trigger. Without a gun it beats itself down. Humanlikes only. Tier 5: killing with it heals the netrunner 10%.',
          'Ultimate', ('T4', 'T5'), ram=(20, 24), upload=1, cooldown=30, mechs=False, humanlike=True, shard_cost=2,
          comps=[comp('Execute', t5HealOnKill=0.1)])
quickhack('SystemCollapse', 'system collapse', 'Crash the target\'s nervous system: knocked out without a scratch for the duration. Mechanoids shut down instead. Tier 5: untraceable, and knocking out a netrunner wipes NetWatch trace.',
          'Ultimate', ('T4', 'T5'), ram=(24, 28), upload=1, cooldown=30, duration=8.8, untraceable_from='T5', blocked='RPC_QH_SystemCollapse',
          shard_cost=2, comps=[comp('Knockout', hediff='RPC_QH_SystemCollapse')])
quickhack('DetonateGrenade', 'detonate grenade', 'A grenade goes off in the target\'s hands (its own if it carries one). On a mechanoid, a weak spot blows instead.',
          'Ultimate', ('T4', 'T5'), ram=(20, 24), upload=1, cooldown=30, shard_cost=2, comps=[comp('Detonate', radius=2.4, damage=30, mechanoidDamage=40)])
quickhack('Cyberpsychosis', 'cyberpsychosis', 'Push a chromed target into cyberpsychosis for the duration: it attacks anyone nearby, friend or foe. Needs cyberware; doesn\'t work on MaxTac. Tier 5: +25% duration per other quickhack on the target.',
          'Ultimate', ('T4', 'T5'), ram=(18, 22), upload=1.5, cooldown=30, duration=60, mechs=False, humanlike=True, shard_cost=2,
          comps=[comp('MentalState', state='RPC_HackedCyberpsychosis', needsChrome=True)])

# == iconic (Canto Mk.6 only, no shard) ==
quickhack('BlackwallGateway', 'Blackwall Gateway', 'Open a door to the Blackwall: lethal damage to the target\'s nervous system and chrome, then it jumps to up to 5 enemies within 20 tiles (+4 RAM per jump). Mechanoids shut down. Only from an iconic Canto Mk.6.',
          'Ultimate', ('Iconic', 'Iconic'), ram=12, upload=3, cooldown=30,
          comps=[comp('Blackwall', damage=60, jumps=5, jumpRadius=20, ramPerJump=4, mechStunSeconds=10)])


# ------------------------------------------------------------------------------------------------ shards

SHARD_COST = {
    'T1': {'Steel': 10, 'ComponentIndustrial': 1},
    'T2': {'Steel': 10, 'ComponentIndustrial': 2},
    'T3': {'Plasteel': 5, 'ComponentIndustrial': 2},
    'T4': {'Plasteel': 5, 'ComponentSpacer': 1},
    'T5': {'Plasteel': 10, 'ComponentSpacer': 2},
}
TIER_LABEL = {'T1': 'tier 1', 'T2': 'tier 2', 'T3': 'tier 3', 'T4': 'tier 4', 'T5': 'tier 5'}


def shards(q):
    if q['lo'] == 'Iconic':
        return ''
    out = ''
    for t in TIERS[TIERS.index(q['lo']):TIERS.index(q['hi']) + 1]:
        cost = ''.join(f'      <{k}>{int(round(v * q["shard_cost"]))}</{k}>\n' for k, v in SHARD_COST[t].items())
        out += f'''  <ThingDef ParentName="RPC_QuickhackShardBase">
    <defName>RPC_Shard_{t}_{q["id"]}</defName>
    <label>quickhack shard: {escape(q["label"])} ({TIER_LABEL[t]})</label>
    <description>A {q["category"].lower()} quickhack on a data shard. A pawn with a cyberdeck can use it to learn {escape(q["label"])} at {TIER_LABEL[t]} (or upgrade it to that tier). The shard is wiped afterwards.\\n\\n{escape(q["desc"])}</description>
    <costList>
{cost}    </costList>
    <recipeMaker>
      <researchPrerequisite>RPC_Cyberware_{t}</researchPrerequisite>
    </recipeMaker>
    <comps>
      <li Class="CompProperties_Usable">
        <useJob>UseItem</useJob>
        <useLabel>Learn {{0_label}}</useLabel>
        <useDuration>300</useDuration>
      </li>
      <li Class="RimPunkCE.CompProperties_UseEffectLearnQuickhack">
        <quickhack>RPC_Quickhack_{q["id"]}</quickhack>
        <tier>{t}</tier>
      </li>
      <li Class="CompProperties_UseEffectDestroySelf" />
    </comps>
  </ThingDef>
'''
    return out


with open(OUT, 'w', encoding='utf-8', newline='\n') as fh:
    fh.write('<?xml version="1.0" encoding="utf-8"?>\n<!-- GENERATED by Source/Tools/gen_quickhacks.py. Do not edit; change the generator and rerun. -->\n<Defs>\n\n')
    fh.write('  <!-- ===================== effect hediffs ===================== -->\n\n')
    fh.write('\n'.join(HEDIFFS))
    for q in QUICKHACKS:
        fh.write(f'\n  <!-- ===================== {q["label"]} ({q["category"].lower()}, {q["lo"]}-{q["hi"]}) ===================== -->\n\n')
        fh.write(q['xml'])
        fh.write(shards(q))
    fh.write('\n</Defs>\n')
print(f'{len(QUICKHACKS)} quickhacks, {len(HEDIFFS)} effect hediffs')
