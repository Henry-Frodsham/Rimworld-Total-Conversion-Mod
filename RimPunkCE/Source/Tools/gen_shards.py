"""
Generates the cycle 4 shards and engrams (neural shards: skill, capacity, soulkiller; engrams of cyberpunk lore figures):

    python -I Source/Tools/gen_shards.py        (run from the RimPunkCE folder)

Output: Common/Defs/Shards/Generated_Shards.xml. Do not edit that file; change this one and rerun.
To add a lore figure: append to FIGURES (name, backstories, skills, traits, resistance, will). To add a shard: SHARDS.
Behaviour: Source/RimPunkCE/Shards/*.cs. Install / remove surgeries are generated in C# for every shard def.
"""
import os
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, 'Common', 'Defs', 'Shards', 'Generated_Shards.xml')

SKILLS = ['Shooting', 'Melee', 'Construction', 'Mining', 'Cooking', 'Plants', 'Animals', 'Crafting', 'Artistic', 'Medicine', 'Social', 'Intellectual']

# ----------------------------------------------------------------------------------------------------------------- shards
# every skill in the bio tab: +5 levels and two or three work stats. ('f' = factor, 'o' = offset)
SKILL_SHARDS = [
    ('Shooting',     "marksman's shard",  'steadies the aim and speeds every reload',            [('f', 'AimingAccuracy', 1.25), ('f', 'ReloadSpeed', 1.25)],                                  (230, 90, 70)),
    ('Melee',        "brawler's shard",   'sharper parries and a better eye for the opening',    [('f', 'MeleeParryChance', 1.25), ('f', 'MeleeCritChance', 1.25), ('f', 'MeleeDodgeChance', 1.25)], (235, 130, 60)),
    ('Construction', "builder's shard",   'faster building and smoothing',                       [('f', 'ConstructionSpeed', 1.25), ('f', 'SmoothingSpeed', 1.25)],                            (240, 200, 60)),
    ('Mining',       "miner's shard",     'faster digging and more ore from every rock',         [('f', 'MiningSpeed', 1.25), ('f', 'MiningYield', 1.25)],                                     (150, 150, 160)),
    ('Cooking',      "chef's shard",      'faster cooking and far fewer food poisonings',        [('f', 'CookSpeed', 1.25), ('f', 'FoodPoisonChance', 0.5)],                                   (230, 170, 100)),
    ('Plants',       "farmer's shard",    'faster planting and bigger harvests',                 [('f', 'PlantWorkSpeed', 1.25), ('f', 'PlantHarvestYield', 1.25)],                            (90, 200, 90)),
    ('Animals',      "handler's shard",   'animals take to the wearer: taming, training, gathering', [('f', 'TameAnimalChance', 1.25), ('f', 'TrainAnimalChance', 1.25), ('f', 'AnimalGatherYield', 1.25)], (160, 120, 80)),
    ('Crafting',     "artisan's shard",   'faster crafting, smelting and drug synthesis',        [('f', 'GeneralLaborSpeed', 1.15), ('f', 'SmeltingSpeed', 1.25), ('f', 'DrugSynthesisSpeed', 1.25)], (200, 160, 60)),
    ('Artistic',     "artist's shard",    'faster sculpting and a more striking presence',       [('f', 'GeneralLaborSpeed', 1.25), ('o', 'PawnBeauty', 0.5)],                                 (220, 100, 200)),
    ('Medicine',     "medic's shard",     'faster, steadier tending and surgery',                [('f', 'MedicalTendSpeed', 1.25), ('f', 'MedicalOperationSpeed', 1.25), ('f', 'MedicalSurgerySuccessChance', 1.25)], (230, 70, 90)),
    ('Social',       "talker's shard",    'persuasive: negotiation, trade prices and social impact', [('f', 'NegotiationAbility', 1.25), ('f', 'SocialImpact', 1.25), ('o', 'TradePriceImprovement', 0.05)], (90, 190, 230)),
    ('Intellectual', "scholar's shard",   'faster research and reading',                         [('f', 'ResearchSpeed', 1.25), ('f', 'ReadingSpeed', 1.25)],                                  (110, 130, 240)),
]

STAT_LABEL = {
    'AimingAccuracy': 'aiming accuracy', 'ReloadSpeed': 'reload speed', 'MeleeParryChance': 'melee parry chance', 'MeleeCritChance': 'melee crit chance',
    'MeleeDodgeChance': 'melee dodge chance', 'ConstructionSpeed': 'construction speed', 'SmoothingSpeed': 'smoothing speed', 'MiningSpeed': 'mining speed',
    'MiningYield': 'mining yield', 'CookSpeed': 'cooking speed', 'FoodPoisonChance': 'food poison chance', 'PlantWorkSpeed': 'planting speed',
    'PlantHarvestYield': 'harvest yield', 'TameAnimalChance': 'taming chance', 'TrainAnimalChance': 'training chance', 'AnimalGatherYield': 'animal gather yield',
    'GeneralLaborSpeed': 'general labor speed', 'SmeltingSpeed': 'smelting speed', 'DrugSynthesisSpeed': 'drug synthesis speed', 'PawnBeauty': 'beauty',
    'MedicalTendSpeed': 'tend speed', 'MedicalOperationSpeed': 'surgery speed', 'MedicalSurgerySuccessChance': 'surgery success', 'NegotiationAbility': 'negotiation ability',
    'SocialImpact': 'social impact', 'TradePriceImprovement': 'trade price improvement', 'ResearchSpeed': 'research speed', 'ReadingSpeed': 'reading speed',
}


def effect_text(kind, stat, value):
    label = STAT_LABEL[stat]
    if kind == 'f':
        pct = round((value - 1) * 100)
        return f'{pct:+d}% {label}'
    if stat == 'PawnBeauty':
        return f'+{value:g} {label}'
    return f'+{round(value * 100)}% {label}'


def stat_xml(effects):
    offs = ''.join(f'        <{s}>{v:g}</{s}>\n' for k, s, v in effects if k == 'o')
    facs = ''.join(f'        <{s}>{v:g}</{s}>\n' for k, s, v in effects if k == 'f')
    out = ''
    if offs: out += '        <statOffsets>\n' + offs.replace('        <', '          <') + '        </statOffsets>\n'
    if facs: out += '        <statFactors>\n' + facs.replace('        <', '          <') + '        </statFactors>\n'
    return out


def shard_def(defname, label, desc, ext_body, colour, value, tex='Things/RimPunk/Shards/Shard', extra=''):
    r, g, b = colour
    return f'''  <ThingDef ParentName="RPC_ShardBase">
    <defName>{defname}</defName>
    <label>{escape(label)}</label>
    <description>{escape(desc).replace(chr(10), chr(92) + 'n')}</description>
    <graphicData>
      <texPath>{tex}</texPath>
      <graphicClass>Graphic_Single</graphicClass>
      <drawSize>0.6</drawSize>
      <color>({r},{g},{b})</color>
    </graphicData>
    <statBases>
      <MarketValue>{value}</MarketValue>
    </statBases>
{extra}    <modExtensions>
      <li Class="RimPunkCE.ShardExtension">
{ext_body}      </li>
    </modExtensions>
  </ThingDef>

'''


def shards_xml():
    out = ''
    for skill, label, blurb, effects, colour in SKILL_SHARDS:
        bullets = ', '.join([f'+5 {skill.lower()} skill'] + [effect_text(*e) for e in effects])
        desc = (f'A neural shard that makes the wearer a better {label.split(chr(39))[0]}: {blurb}. '
                f'{bullets[0].upper() + bullets[1:]}.\n\nNeeds a comms implant to slot; two shards at once. Slot it from the cyberware tab, no surgery.')
        body = f'        <skill>{skill}</skill>\n        <skillLevels>5</skillLevels>\n' + stat_xml(effects)
        out += shard_def(f'RPC_SkillShard_{skill}', label, desc, body, colour, 650)
    out += shard_def('RPC_CapacityShard', 'capacity shard',
                     'A neural shard that lets the nervous system tolerate more chrome. +25 maximum cyberware capacity (not for a gonk: nothing raises a gonk).\n\nNeeds a comms implant to slot; two shards at once. Slot it from the cyberware tab, no surgery.',
                     '        <capacityBonus>25</capacityBonus>\n', (80, 220, 220), 900)
    out += shard_def('RPC_SoulkillerShard', 'soulkiller shard',
                     'A shard that copies a mind and burns the original out of its body. Slotting it kills the wearer and turns the shard into an engram of them. Anyone can be killed this way: slot it yourself from the cyberware tab, or force it into someone else with an operation.\n\nNeeds a comms implant to slot.',
                     '        <soulkiller>true</soulkiller>\n', (230, 30, 60), 3500, tex='Things/RimPunk/Shards/SoulkillerShard',
                     extra='    <stackLimit>1</stackLimit>\n')
    return out


# ----------------------------------------------------------------------------------------------------------------- lore figures
# resistance: normal prisoners are 10-26, will 1-4. Jackie is easy to bring round, Smasher almost impossible.
FIGURES = [
    dict(id='JackieWelles', first='Jackie', nick='Jackie', last='Welles', value=3500, commonality=0.08, resistance=5, will=1,
         blurb='Mercenary, fixer\'s right hand, everybody\'s friend. Big heart, big laugh, and the best man to have beside you when it goes loud.',
         child=('Heywood kid', 'kid', 'Grew up in Heywood, in the glow of the Valentinos\' altars and the smell of street food. [PAWN_nameDef] learned early that family is whoever shows up.', {'Social': 4, 'Melee': 2}),
         adult=('valentino merc', 'merc', 'A Valentino-trained mercenary who took gigs the way other people take lunch. [PAWN_pronoun] never left a partner behind, which is exactly why the big job went the way it did.', {'Social': 5, 'Shooting': 4, 'Melee': 3}),
         skills=dict(Shooting=(12, 'Minor'), Melee=(10, 'Minor'), Construction=(4, None), Mining=(2, None), Cooking=(3, None), Plants=(1, None), Animals=(5, None),
                     Crafting=(4, None), Artistic=(5, None), Medicine=(3, None), Social=(15, 'Major'), Intellectual=(5, None)),
         traits=[('Kind', 0), ('Tough', 0), ('NaturalMood', 1)]),
    dict(id='JohnnySilverhand', first='Johnny', nick='Johnny', last='Silverhand', value=5200, commonality=0.05, resistance=28, will=6,
         blurb='Rockerboy, anarchist, the man who put a nuke under Arasaka Tower. Loud, angry, and right about more than anyone wants to admit.',
         child=('rebel kid', 'rebel', 'Raised in Portland on speeches against the corps and the first guitar [PAWN_nameDef] could steal. [PAWN_pronoun] never took orders well.', {'Artistic': 4, 'Social': 3}),
         adult=('rockerboy', 'rocker', 'Front man, pamphleteer, terrorist. [PAWN_nameDef] sang the corps down until singing was not enough anymore.', {'Artistic': 6, 'Social': 5, 'Shooting': 3}),
         skills=dict(Shooting=(13, 'Minor'), Melee=(7, None), Construction=(2, None), Mining=(1, None), Cooking=(2, None), Plants=(0, None), Animals=(2, None),
                     Crafting=(6, None), Artistic=(17, 'Major'), Medicine=(2, None), Social=(14, 'Major'), Intellectual=(8, None)),
         traits=[('Abrasive', 0), ('Bloodlust', 0), ('TorturedArtist', 0)]),
    dict(id='AdamSmasher', first='Adam', nick='Smasher', last='Smasher', value=5400, commonality=0.04, resistance=40, will=8,
         blurb='Arasaka\'s full-conversion enforcer, more machine than man and nothing at all like a person. Nobody has ever talked him out of anything.',
         child=('arasaka orphan', 'orphan', 'Taken in by Arasaka as a boy and sharpened ever since. [PAWN_nameDef] was never given anything to lose.', {'Shooting': 2, 'Melee': 2}),
         adult=('arasaka enforcer', 'enforcer', 'Hunter, executioner, the corporation\'s answer to every problem. [PAWN_nameDef] kept replacing parts until there was only the job left.', {'Shooting': 6, 'Melee': 6, 'Mining': 2}),
         skills=dict(Shooting=(20, 'Major'), Melee=(18, 'Major'), Construction=(4, None), Mining=(6, None), Cooking=(1, None), Plants=(0, None), Animals=(2, None),
                     Crafting=(7, None), Artistic=(0, None), Medicine=(3, None), Social=(2, None), Intellectual=(8, 'Minor')),
         traits=[('Psychopath', 0), ('Bloodlust', 0), ('Tough', 0)]),
    dict(id='MorganBlackhand', first='Morgan', nick='Blackhand', last='Blackhand', value=5000, commonality=0.05, resistance=24, will=5,
         blurb='The legend of the Fourth Corporate War, the solo other solos are measured against. Quiet, careful, and hard to impress.',
         child=('military brat', 'brat', 'Raised on bases and in briefing rooms. [PAWN_nameDef] learned discipline before [PAWN_pronoun] learned to read.', {'Shooting': 3, 'Intellectual': 2}),
         adult=('legendary solo', 'solo', 'Soldier, then mercenary, then the name everyone drops in a bar. [PAWN_nameDef] has seen every kind of war and has opinions on all of them.', {'Shooting': 6, 'Melee': 5, 'Medicine': 3}),
         skills=dict(Shooting=(19, 'Major'), Melee=(16, 'Major'), Construction=(6, None), Mining=(4, None), Cooking=(3, None), Plants=(2, None), Animals=(3, None),
                     Crafting=(7, None), Artistic=(2, None), Medicine=(7, 'Minor'), Social=(8, None), Intellectual=(12, 'Minor')),
         traits=[('Nerves', 2), ('Tough', 0)]),
    dict(id='DavidMartinez', first='David', nick='David', last='Martinez', value=4200, commonality=0.07, resistance=12, will=2,
         blurb='A kid from Santo Domingo with a borrowed sandevistan and nothing to lose, who burned brighter than the whole city.',
         child=('santo domingo kid', 'kid', 'Raised by [PAWN_possessive] mother in the Santo Domingo apartments, with a lot of ambition and not much else.', {'Crafting': 2, 'Melee': 2}),
         adult=('edgerunner', 'runner', 'Fresh into the gig economy and faster than anyone expected. [PAWN_nameDef] took a thousand risks for the people who mattered.', {'Melee': 5, 'Shooting': 4, 'Intellectual': 3}),
         skills=dict(Shooting=(11, 'Minor'), Melee=(12, 'Major'), Construction=(5, None), Mining=(3, None), Cooking=(5, None), Plants=(1, None), Animals=(2, None),
                     Crafting=(8, 'Minor'), Artistic=(6, None), Medicine=(3, None), Social=(7, None), Intellectual=(10, 'Minor')),
         traits=[('FastLearner', 0), ('Nerves', 1), ('Industriousness', 1)]),
]


def backstory(defname, slot, spec):
    title, short, desc, gains = spec
    gain_xml = ''.join(f'      <{k}>{v}</{k}>\n' for k, v in gains.items())
    return f'''  <BackstoryDef>
    <defName>{defname}</defName>
    <identifier>{defname}</identifier>
    <slot>{slot}</slot>
    <title>{escape(title)}</title>
    <titleShort>{escape(short)}</titleShort>
    <description>{escape(desc)}</description>
    <skillGains>
{gain_xml}    </skillGains>
    <spawnCategories>
      <li>RPC_Lore</li>
    </spawnCategories>
    <shuffleable>false</shuffleable>
  </BackstoryDef>

'''


def figure_xml(f):
    i = f['id']
    out = backstory(f'RPC_Child_{i}', 'Childhood', f['child'])
    out += backstory(f'RPC_Adult_{i}', 'Adulthood', f['adult'])
    skills = ''.join(
        f'      <li><skill>{s}</skill><level>{f["skills"].get(s, (0, None))[0]}</level>' + (f'<passion>{f["skills"][s][1]}</passion>' if f['skills'].get(s, (0, None))[1] else '') + '</li>\n'
        for s in SKILLS)
    traits = ''.join(f'      <li><def>{t}</def><degree>{d}</degree></li>\n' for t, d in f['traits'])
    out += f'''  <RimPunkCE.EngramPersonDef>
    <defName>RPC_Person_{i}</defName>
    <label>{escape(f['first'] + ' ' + f['last'])}</label>
    <description>{escape(f['blurb'])}</description>
    <firstName>{escape(f['first'])}</firstName>
    <nickName>{escape(f['nick'])}</nickName>
    <lastName>{escape(f['last'])}</lastName>
    <childhood>RPC_Child_{i}</childhood>
    <adulthood>RPC_Adult_{i}</adulthood>
    <skills>
{skills}    </skills>
    <traits>
{traits}    </traits>
    <resistance>{f['resistance']}</resistance>
    <will>{f['will']}</will>
  </RimPunkCE.EngramPersonDef>

  <ThingDef ParentName="RPC_EngramBase">
    <defName>RPC_Engram_{i}</defName>
    <label>engram</label>
    <description>{escape(f['blurb'])}\\n\\nAn engram is a person on a shard. Written over a living body in an operation, it replaces their name, backstory, traits and skills (never their looks). The new person wakes up after 12 hours, and has to be arrested and talked into joining.</description>
    <statBases>
      <MarketValue>{f['value']}</MarketValue>
    </statBases>
    <generateCommonality>{f['commonality']}</generateCommonality>
    <thingSetMakerTags>
      <li>RewardStandardLowFreq</li>
    </thingSetMakerTags>
    <comps>
      <li Class="RimPunkCE.CompProperties_Engram">
        <person>RPC_Person_{i}</person>
      </li>
    </comps>
  </ThingDef>

'''
    return out


HEAD = '''<?xml version="1.0" encoding="utf-8"?>
<!-- GENERATED by Source/Tools/gen_shards.py. Do not edit; change the generator and rerun. -->
<Defs>

  <ThingCategoryDef>
    <defName>RPC_NeuralShards</defName>
    <label>neural shards</label>
    <parent>Items</parent>
  </ThingCategoryDef>

  <ThingCategoryDef>
    <defName>RPC_Engrams</defName>
    <label>engrams</label>
    <parent>Items</parent>
  </ThingCategoryDef>

  <!-- ===================== bases ===================== -->

  <ThingDef Name="RPC_ShardBase" ParentName="ResourceBase" Abstract="True">
    <techLevel>Spacer</techLevel>
    <thingCategories>
      <li>RPC_NeuralShards</li>
    </thingCategories>
    <graphicData>
      <texPath>Things/RimPunk/Shards/Shard</texPath>
      <graphicClass>Graphic_Single</graphicClass>
    </graphicData>
    <stackLimit>5</stackLimit>
    <tradeTags>
      <li>RPC_NeuralShard</li>
    </tradeTags>
    <statBases>
      <Mass>0.05</Mass>
      <DeteriorationRate>0</DeteriorationRate>
      <Flammability>0.2</Flammability>
    </statBases>
    <tradeability>All</tradeability>
  </ThingDef>

  <ThingDef Name="RPC_EngramBase" ParentName="ResourceBase" Abstract="True">
    <techLevel>Spacer</techLevel>
    <thingCategories>
      <li>RPC_Engrams</li>
    </thingCategories>
    <graphicData>
      <texPath>Things/RimPunk/Shards/Engram</texPath>
      <graphicClass>Graphic_Single</graphicClass>
      <drawSize>0.7</drawSize>
    </graphicData>
    <stackLimit>1</stackLimit>
    <tradeTags>
      <li>RPC_Engram</li>
    </tradeTags>
    <statBases>
      <Mass>0.05</Mass>
      <DeteriorationRate>0</DeteriorationRate>
      <Flammability>0.2</Flammability>
    </statBases>
    <tradeability>All</tradeability>
  </ThingDef>

  <!-- the soulkiller's engram: carries whoever was killed, nobody rolls it as a reward -->
  <ThingDef ParentName="RPC_EngramBase">
    <defName>RPC_Engram</defName>
    <label>engram</label>
    <description>A person on a shard, copied out by a soulkiller. Written over a living body in an operation, it replaces their name, backstory, traits and skills (never their looks). The new person wakes up after 12 hours, and has to be arrested and talked into joining, unless they came from your own colony: then they join at once.</description>
    <statBases>
      <MarketValue>2500</MarketValue>
    </statBases>
    <generateCommonality>0</generateCommonality>
    <comps>
      <li Class="RimPunkCE.CompProperties_Engram" />
    </comps>
  </ThingDef>

  <JobDef>
    <defName>RPC_SlotShard</defName>
    <driverClass>RimPunkCE.JobDriver_SlotShard</driverClass>
    <reportString>slotting TargetA.</reportString>
    <allowOpportunisticPrefix>false</allowOpportunisticPrefix>
  </JobDef>

  <!-- the new person: 12 hours down, whatever the body is doing -->
  <HediffDef>
    <defName>RPC_EngramShock</defName>
    <label>engram shock</label>
    <description>A new mind in an old body. Unconscious until it settles, then the new person has to decide what they make of the place.</description>
    <hediffClass>HediffWithComps</hediffClass>
    <isBad>false</isBad>
    <tendable>false</tendable>
    <initialSeverity>1</initialSeverity>
    <maxSeverity>1</maxSeverity>
    <defaultLabelColor>(0.9, 0.3, 0.45)</defaultLabelColor>
    <comps>
      <li Class="HediffCompProperties_Disappears">
        <disappearsAfterTicks>30000~30000</disappearsAfterTicks>
        <showRemainingTime>true</showRemainingTime>
      </li>
    </comps>
    <stages>
      <li>
        <becomeVisible>true</becomeVisible>
        <capMods>
          <li>
            <capacity>Consciousness</capacity>
            <setMax>0.1</setMax>
          </li>
        </capMods>
      </li>
    </stages>
  </HediffDef>

  <!-- ===================== shards ===================== -->

'''


def main():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(HEAD)
        fh.write(shards_xml())
        fh.write('  <!-- ===================== lore engrams ===================== -->\n\n')
        for f in FIGURES:
            fh.write(figure_xml(f))
        fh.write('</Defs>\n')
    print('wrote', OUT)


if __name__ == '__main__':
    main()
