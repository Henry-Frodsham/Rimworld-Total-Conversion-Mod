"""Offline integration checks for generated content, approved balance data, and generator reproducibility.

Run: python -m unittest discover -s RimPunkCE/Source/Tools/tests -v
Live C# scenarios exercise simulation behaviour; these checks verify the shipped data pipeline.
"""
from pathlib import Path
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
TOOLS = ROOT / 'Source/Tools'
PLATING = {'Carapace', 'CellularAdapter', 'Chitin', 'CogitoLattice', 'NanoPlating', 'RangeGuard', 'SubdermalArmor', 'TitaniumBones'}
MULTIPLIERS = {'ParaBellum', 'RaraAvis', 'ScarCoalescerBoost'}


def defs(path):
    return {n.findtext('defName'): n for n in ET.parse(ROOT / path).getroot() if n.findtext('defName')}


class Cycle2ContentTests(unittest.TestCase):
    def test_all_shipped_xml_is_well_formed(self):
        paths = list((ROOT / 'Common').rglob('*.xml'))
        self.assertGreater(len(paths), 20)
        for path in paths:
            with self.subTest(path=path.relative_to(ROOT)):
                ET.parse(path)

    def test_no_duplicate_generated_definitions(self):
        seen = set()
        for path in (ROOT / 'Common/Defs').rglob('*.xml'):
            for node in ET.parse(path).getroot():
                name = node.findtext('defName')
                if name:
                    identity = (node.tag, name)
                    self.assertNotIn(identity, seen, identity)
                    seen.add(identity)

    def test_armour_is_explicitly_opt_in(self):
        generated = defs('Common/Defs/Cyberware/Generated_Cyberware.xml')
        flat = set()
        factors = set()
        for name, node in generated.items():
            for ext in node.findall('modExtensions/li'):
                for offset in ext.findall('effects/statOffsets/li'):
                    if offset.findtext('stat') in ('ArmorRating_Sharp', 'ArmorRating_Blunt'):
                        flat.add(name.removeprefix('RPC_'))
                for factor in ext.findall('effects/statFactors/li'):
                    if factor.findtext('stat') in ('ArmorRating_Sharp', 'ArmorRating_Blunt'):
                        factors.add(name.removeprefix('RPC_'))
        self.assertEqual(flat, PLATING | {'CogitoLatticeBoost', 'RangeGuardBoost'})
        self.assertEqual(factors, MULTIPLIERS)

    def test_ce_caps_and_general_part_armour(self):
        tuning = defs('Common/Defs/Cyberware/Bases.xml')['RPC_ChromePlatingTuning']
        self.assertEqual(float(tuning.findtext('maxPawnSharp')), 10)
        self.assertEqual(float(tuning.findtext('maxPawnBlunt')), 25)
        for tag in ('partSharp', 'partBlunt'):
            self.assertEqual([float(n.text) for n in tuning.findall(f'{tag}/perTier/li')], [0] * 6)
        self.assertEqual(int(tuning.findtext('aiMinChrome')), 3)
        self.assertEqual(float(tuning.findtext('aiFirstArmorChance')), 0.6)
        self.assertEqual(float(tuning.findtext('aiSecondArmorChance')), 0.25)
        self.assertEqual(float(tuning.findtext('aiMaxUsage')), 0.75)

    def test_approved_weapon_manifest_is_applied_exactly(self):
        approved = json.loads((TOOLS / 'data/cycle2_weapon_changes.json').read_text(encoding='utf-8'))
        rows = json.loads((TOOLS / 'data/weapons.json').read_text(encoding='utf-8'))
        applied = {r['name']: r['damageMultiplier'] for r in rows if 'damageMultiplier' in r}
        self.assertEqual(len(approved), 26)
        self.assertEqual(applied, approved)

    def test_gun_multipliers_survive_ce_patch(self):
        guns = defs('Common/Defs/Weapons/Generated_Guns.xml')
        patches = defs('Common/Patches/Generated_Guns_CE.xml')
        approved = json.loads((TOOLS / 'data/cycle2_weapon_changes.json').read_text(encoding='utf-8'))
        count = 0
        for name, gun in guns.items():
            label = gun.findtext('label')
            expected = approved.get(label, 1)
            self.assertEqual(float(gun.findtext('statBases/RangedWeapon_DamageMultiplier')), expected, label)
            self.assertEqual(float(patches[name].findtext('statBases/RangedWeapon_DamageMultiplier')), expected, label)
            if label in approved:
                count += 1
        self.assertEqual(count, 18)

    def test_melee_primary_damage_matches_approved_values(self):
        # Independent, reviewed expected powers, including secondary handles. Riders keep their own damage.
        expected = {'Baseball Bat': (15, 3), 'Kanabo': (15, 3)}
        for label in ('Crowbar', 'Electric Baton Alpha', 'Electric Baton Beta', 'Electric Baton Gamma', 'Steel Pipe', 'Tire Iron'):
            expected[label] = (12.35, 2.85)
        melee = defs('Common/Defs/Weapons/Generated_Melee.xml')
        found = 0
        for node in melee.values():
            label = node.findtext('label')
            if label in expected:
                self.assertEqual(tuple(float(n.findtext('power')) for n in node.findall('tools/li')), expected[label], label)
                found += 1
        self.assertEqual(found, 8)

    def test_all_default_projectiles_and_ammo_sets_exist(self):
        ammo = defs('Common/Defs/Weapons/Generated_Ammo.xml')
        for patch in defs('Common/Patches/Generated_Guns_CE.xml').values():
            self.assertIn(patch.findtext('Properties/defaultProjectile'), ammo)
            ammo_set = ammo[patch.findtext('AmmoUser/ammoSet')]
            for pair in ammo_set.find('ammoTypes'):
                self.assertIn(pair.tag, ammo)
                self.assertIn(pair.text, ammo)

    def test_psychosis_tuning_is_complete_and_bounded(self):
        t = defs('Common/Defs/Core.xml')['RPC_CyberpsychosisTuning']
        for field in ('baselinePerLoadSecond', 'damageSpike', 'killSpike', 'abilitySpike', 'sleepPerSecond', 'recreationPerLevel', 'drugRecovery', 'medicalRecovery'):
            self.assertGreater(float(t.findtext(field)), 0, field)
        self.assertTrue(0 < float(t.findtext('permanentChance')) < 0.1)
        self.assertLess(int(t.findtext('temporaryMinTicks')), int(t.findtext('temporaryMaxTicks')))

    def test_language_keys_are_unique(self):
        keys = set()
        for path in (ROOT / 'Common/Languages/English/Keyed').glob('*.xml'):
            for key in ET.parse(path).getroot():
                self.assertNotIn(key.tag, keys)
                keys.add(key.tag)
        self.assertIn('RPC_Features_WorldExplanation', keys)
        self.assertIn('RPC_Psychosis_Status', keys)

    def test_generators_reproduce_shipped_files_in_isolation(self):
        scratch = ROOT / 'Diagnostics/Cycle2'
        scratch.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='content-', dir=scratch) as tmp:
            sandbox = Path(tmp)
            (sandbox / 'Source/Tools/data').mkdir(parents=True)
            (sandbox / 'Common/Defs/Cyberware').mkdir(parents=True)
            # Weapon texture selection checks files under Textures. Copy filenames via empty placeholders.
            for path in (ROOT / 'Common/Textures').rglob('*'):
                if path.is_file():
                    dest = sandbox / path.relative_to(ROOT)
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.touch()
            shutil.copy2(TOOLS / 'data/weapons.json', sandbox / 'Source/Tools/data/weapons.json')
            shutil.copy2(TOOLS / 'data/weapon_sounds.json', sandbox / 'Source/Tools/data/weapon_sounds.json')
            for script in ('gen_cyberware.py', 'gen_weapons.py'):
                shutil.copy2(TOOLS / script, sandbox / 'Source/Tools' / script)
                result = subprocess.run([sys.executable, '-I', str(sandbox / 'Source/Tools' / script)], capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, result.stderr)
            outputs = ['Common/Defs/Cyberware/Generated_Cyberware.xml', 'Common/Defs/Weapons/Generated_Ammo.xml',
                'Common/Defs/Weapons/Generated_Guns.xml', 'Common/Defs/Weapons/Generated_Melee.xml',
                'Common/Defs/Weapons/Generated_Research.xml', 'Common/Patches/Generated_Guns_CE.xml']
            for rel in outputs:
                self.assertEqual((sandbox / rel).read_bytes(), (ROOT / rel).read_bytes(), rel)


if __name__ == '__main__':
    unittest.main()
