"""
Generates the cycle 4 health items, the two types from cyberpunk 2077, five tiers each (Mk.1 common ... Mk.5 legendary):

  bounce back  injectable. Go-juice sound. Brief regeneration on every body part, seals every bleeding wound,
               a rush of adrenaline and a move speed boost. Quick to use.
  maxdoc       inhaler. Yayo sound, slower to use. Instantly restores a share of every body part's health and seals
               every bleeding wound. No adrenaline, no speed.

    python -I Source/Tools/gen_health_items.py        (run from the RimPunkCE folder)

Output: Common/Defs/Drugs/Generated_HealthItems.xml. Not infinite: each dose is made at a drug lab from neutroamine and steel,
behind a research project in the cyberware tab. Behaviour: Source/RimPunkCE/Shards/HealthItems.cs.
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, 'Common', 'Defs', 'Drugs', 'Generated_HealthItems.xml')

RARITY = ['common', 'uncommon', 'rare', 'epic', 'legendary']

# index 0..4 = Mk.1..Mk.5
BOUNCE = dict(
    id='BounceBack', label='bounce back', row=3,
    regen=[0.05, 0.10, 0.15, 0.20, 0.25],            # share of every part's max health, over the duration (max 25%)
    seconds=[12, 14, 16, 18, 20],                    # brief
    speed=[1.08, 1.12, 1.16, 1.20, 1.25],            # move speed factor
    adrenaline=[0.10, 0.15, 0.20, 0.30, 0.40],       # share of max adrenaline
    seal=[0.30, 0.40, 0.50, 0.60, 0.70],             # tend quality of the bleeding wounds it seals
    neutroamine=[2, 3, 4, 5, 6], steel=[10, 15, 20, 25, 30],
    value=[45, 70, 110, 170, 260], work=[600, 900, 1200, 1600, 2000],
    research=[500, 800, 1300, 2000, 3000],
)
MAXDOC = dict(
    id='MaxDoc', label='maxdoc', row=4,
    instant=[0.07, 0.14, 0.21, 0.28, 0.35],          # share of every part's max health, at once (max 35%)
    seal=[0.30, 0.45, 0.60, 0.75, 0.90],
    ticks=[360, 360, 360, 360, 360],                 # longer than an injector's 80
    neutroamine=[2, 3, 4, 5, 7], steel=[8, 12, 16, 20, 26],
    value=[40, 65, 100, 160, 250], work=[500, 800, 1100, 1500, 1900],
    research=[500, 800, 1300, 2000, 3000],
)


def pct(x):
    return f'{round(x * 100)}%'


def research(t, n):
    i = n - 1
    pre = ['MedicineProduction', 'MicroelectronicsBasics'] if n == 1 else [f'RPC_Research_{t["id"]}{n - 1}']
    pre_xml = ''.join(f'      <li>{p}</li>\n' for p in pre)
    kind = 'injector' if t is BOUNCE else 'inhaler'
    return f'''  <ResearchProjectDef ParentName="RPC_CyberwareResearchBase">
    <defName>RPC_Research_{t['id']}{n}</defName>
    <label>{t['label']} Mk.{n}</label>
    <description>Craft {t['label']} Mk.{n} ({RARITY[i]}) at a drug lab. {'Bounce back is an injector: brief regeneration, speed and adrenaline.' if t is BOUNCE else 'Maxdoc is an inhaler: an instant top-up for every body part.'}</description>
    <baseCost>{t['research'][i]}</baseCost>
    <prerequisites>
{pre_xml}    </prerequisites>
    <researchViewX>{i}</researchViewX>
    <researchViewY>{t['row']}</researchViewY>
  </ResearchProjectDef>

'''


def bounce_back(n):
    t = BOUNCE
    i = n - 1
    secs = t['seconds'][i]
    ticks = secs * 60
    return f'''  <ThingDef ParentName="RPC_HealthItemBase">
    <defName>RPC_BounceBack_{n}</defName>
    <label>bounce back Mk.{n}</label>
    <description>A {RARITY[i]} autoinjector of regenerative nanites and stimulants. Regrows {pct(t['regen'][i])} of every body part's health over {secs} seconds, seals every bleeding wound, floods the user with adrenaline and speeds them up by {round((t['speed'][i] - 1) * 100)}% while it lasts. Injected in a moment. Not infinite: it is made at a drug lab from neutroamine and steel.</description>
    <descriptionHyperlinks><HediffDef>RPC_BounceBackHigh_{n}</HediffDef></descriptionHyperlinks>
    <graphicData>
      <texPath>Things/RimPunk/Health/BounceBack_{n}</texPath>
    </graphicData>
    <statBases>
      <WorkToMake>{t['work'][i]}</WorkToMake>
      <MarketValue>{t['value'][i]}</MarketValue>
    </statBases>
    <ingestible>
      <foodType>Processed, Fluid</foodType>
      <baseIngestTicks>80</baseIngestTicks>
      <ingestSound>Ingest_Inject</ingestSound>
      <ingestCommandString>Inject {{0}}</ingestCommandString>
      <ingestReportString>Injecting {{0}}.</ingestReportString>
      <outcomeDoers>
        <li Class="RimPunkCE.IngestionOutcomeDoer_SealWounds">
          <quality>{t['seal'][i]}</quality>
        </li>
        <li Class="IngestionOutcomeDoer_GiveHediff">
          <hediffDef>RPC_BounceBackHigh_{n}</hediffDef>
          <severity>1</severity>
        </li>
        <li Class="RimPunkCE.IngestionOutcomeDoer_Adrenaline">
          <fraction>{t['adrenaline'][i]}</fraction>
        </li>
      </outcomeDoers>
    </ingestible>
    <recipeMaker>
      <researchPrerequisite>RPC_Research_BounceBack{n}</researchPrerequisite>
      <label>make bounce back Mk.{n}</label>
      <productCount>1</productCount>
      <displayPriority>{2300 + i}</displayPriority>
    </recipeMaker>
    <costList>
      <Neutroamine>{t['neutroamine'][i]}</Neutroamine>
      <Steel>{t['steel'][i]}</Steel>
    </costList>
    <comps>
      <li Class="CompProperties_Drug">
        <listOrder>{1200 + i}</listOrder>
      </li>
    </comps>
  </ThingDef>

  <HediffDef>
    <defName>RPC_BounceBackHigh_{n}</defName>
    <label>bounce back Mk.{n}</label>
    <description>Regenerative nanites at work: every wounded body part is regrowing, and the legs are {round((t['speed'][i] - 1) * 100)}% faster.</description>
    <hediffClass>HediffWithComps</hediffClass>
    <isBad>false</isBad>
    <defaultLabelColor>(0.4, 1, 0.6)</defaultLabelColor>
    <initialSeverity>1</initialSeverity>
    <maxSeverity>1</maxSeverity>
    <comps>
      <li Class="HediffCompProperties_Disappears">
        <disappearsAfterTicks>{ticks}~{ticks}</disappearsAfterTicks>
        <showRemainingTime>true</showRemainingTime>
      </li>
      <li Class="RimPunkCE.HediffCompProperties_PartRegen">
        <totalFraction>{t['regen'][i]}</totalFraction>
        <durationTicks>{ticks}</durationTicks>
      </li>
    </comps>
    <stages>
      <li>
        <statFactors>
          <MoveSpeed>{t['speed'][i]}</MoveSpeed>
        </statFactors>
      </li>
    </stages>
  </HediffDef>

'''


def maxdoc(n):
    t = MAXDOC
    i = n - 1
    return f'''  <ThingDef ParentName="RPC_HealthItemBase">
    <defName>RPC_MaxDoc_{n}</defName>
    <label>maxdoc Mk.{n}</label>
    <description>A {RARITY[i]} inhaler of medical aerosol. Instantly restores {pct(t['instant'][i])} of every body part's health and seals every bleeding wound. Slow to take, and no adrenaline or speed in it. Not infinite: it is made at a drug lab from neutroamine and steel.</description>
    <graphicData>
      <texPath>Things/RimPunk/Health/MaxDoc_{n}</texPath>
    </graphicData>
    <statBases>
      <WorkToMake>{t['work'][i]}</WorkToMake>
      <MarketValue>{t['value'][i]}</MarketValue>
    </statBases>
    <ingestible>
      <foodType>Processed</foodType>
      <baseIngestTicks>{t['ticks'][i]}</baseIngestTicks>
      <ingestSound>Ingest_Snort</ingestSound>
      <ingestCommandString>Inhale {{0}}</ingestCommandString>
      <ingestReportString>Inhaling {{0}}.</ingestReportString>
      <outcomeDoers>
        <li Class="RimPunkCE.IngestionOutcomeDoer_SealWounds">
          <quality>{t['seal'][i]}</quality>
        </li>
        <li Class="RimPunkCE.IngestionOutcomeDoer_HealParts">
          <fraction>{t['instant'][i]}</fraction>
        </li>
      </outcomeDoers>
    </ingestible>
    <recipeMaker>
      <researchPrerequisite>RPC_Research_MaxDoc{n}</researchPrerequisite>
      <label>make maxdoc Mk.{n}</label>
      <productCount>1</productCount>
      <displayPriority>{2310 + i}</displayPriority>
    </recipeMaker>
    <costList>
      <Neutroamine>{t['neutroamine'][i]}</Neutroamine>
      <Steel>{t['steel'][i]}</Steel>
    </costList>
    <comps>
      <li Class="CompProperties_Drug">
        <listOrder>{1210 + i}</listOrder>
      </li>
    </comps>
  </ThingDef>

'''


HEAD = '''<?xml version="1.0" encoding="utf-8"?>
<!-- GENERATED by Source/Tools/gen_health_items.py. Do not edit; change the generator and rerun. -->
<Defs>

  <!-- Bounce back (injector, go-juice sound): brief regeneration on every body part (up to 25% at Mk.5), seals bleeding, adrenaline, move speed.
       Maxdoc (inhaler, yayo sound, slower to take): instantly restores up to 35% of every body part's health at Mk.5, seals bleeding.
       Five tiers each. Made at a drug lab from neutroamine and steel; one research project per tier in the cyberware tab (rows 4 and 5). -->

  <ThingDef Name="RPC_HealthItemBase" ParentName="MakeableDrugBase" Abstract="True">
    <graphicData>
      <graphicClass>Graphic_Single</graphicClass>
    </graphicData>
    <rotatable>false</rotatable>
    <socialPropernessMatters>false</socialPropernessMatters>
    <techLevel>Spacer</techLevel>
    <stackLimit>10</stackLimit>
    <statBases>
      <Mass>0.05</Mass>
      <Flammability>0.4</Flammability>
    </statBases>
    <ingestible>
      <drugCategory>Medical</drugCategory>
      <chairSearchRadius>0</chairSearchRadius>
      <useEatingSpeedStat>false</useEatingSpeedStat>
    </ingestible>
    <recipeMaker>
      <!-- MakeableDrugBase adds a x4 bulk recipe; a dose is made one at a time -->
      <bulkRecipeCount>-1</bulkRecipeCount>
      <recipeUsers>
        <li>DrugLab</li>
      </recipeUsers>
      <soundWorking>Recipe_Drug</soundWorking>
    </recipeMaker>
  </ThingDef>

'''


def main():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(HEAD)
        fh.write('  <!-- ===================== research ===================== -->\n\n')
        for t in (BOUNCE, MAXDOC):
            for n in range(1, 6):
                fh.write(research(t, n))
        fh.write('  <!-- ===================== bounce back (injectors) ===================== -->\n\n')
        for n in range(1, 6):
            fh.write(bounce_back(n))
        fh.write('  <!-- ===================== maxdoc (inhalers) ===================== -->\n\n')
        for n in range(1, 6):
            fh.write(maxdoc(n))
        fh.write('</Defs>\n')
    print('wrote', OUT)


if __name__ == '__main__':
    main()
