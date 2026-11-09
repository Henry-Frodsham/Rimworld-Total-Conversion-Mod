"""Validate the shipped audio, routing and reproducible SoundDefs without the game."""
from array import array
import hashlib
import json
import math
from pathlib import Path
import unittest
import wave
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
DATA = ROOT / 'Source/Tools/data'


class WeaponAudioTests(unittest.TestCase):
    def test_every_shipped_recording_is_short_mono_click_free_and_consistent(self):
        records = json.loads((DATA / 'weapon_sound_import.json').read_text(encoding='utf-8'))
        profiles = json.loads((DATA / 'weapon_sounds.json').read_text(encoding='utf-8'))['profiles']
        self.assertEqual(len(records), 41)
        for record in records:
            with self.subTest(clip=record['output']):
                path = ROOT / record['output']
                self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), record['outputSHA256'])
                with wave.open(str(path), 'rb') as sound:
                    self.assertEqual((sound.getnchannels(), sound.getframerate(), sound.getsampwidth()), (1,44100,2))
                    duration = sound.getnframes() / sound.getframerate()
                    self.assertTrue(0.055 <= duration <= profiles[record['id']]['maxSeconds'] + 1/44100)
                    self.assertLess(duration, record['sourceSeconds'])
                    samples = array('h'); samples.frombytes(sound.readframes(sound.getnframes()))
                peak = max(abs(v) for v in samples) / 32767
                rms = math.sqrt(sum((v/32767)**2 for v in samples) / len(samples))
                self.assertTrue(-3.05 < 20*math.log10(peak) < -2.95)
                self.assertGreater(rms, 0.003)
                self.assertLessEqual(abs(samples[0]), 1)
                self.assertLessEqual(abs(samples[-1]), 1)
                first = next(i for i,v in enumerate(samples) if abs(v) > 32767 * 0.015)
                self.assertLess(first / 44100, 0.010)

    def test_every_bank_resolves_only_its_own_files(self):
        profiles = json.loads((DATA / 'weapon_sounds.json').read_text(encoding='utf-8'))['profiles']
        definitions = {n.findtext('defName'): n for n in ET.parse(ROOT/'Common/Defs/Sounds/Generated_WeaponSounds.xml').getroot()}
        self.assertEqual(len(definitions), 14)
        for profile in profiles.values():
            bank = definitions[profile['defName']]
            folder = bank.findtext('subSounds/li/grains/li/clipFolderPath')
            self.assertEqual(folder, profile['folder'])
            clips = list((ROOT/'Common/Sounds'/folder).glob('*.wav'))
            self.assertGreaterEqual(len(clips), 2)
            self.assertEqual(bank.findtext('context'), 'MapOnly')
            self.assertEqual(bank.findtext('subSounds/li/onCamera'), 'false')
            self.assertEqual(bank.findtext('subSounds/li/tempoAffectedByGameSpeed'), 'false')

    def test_all_52_guns_use_the_right_mechanism_and_have_no_duplicate_tail(self):
        guns = ET.parse(ROOT/'Common/Defs/Weapons/Generated_Guns.xml').getroot().findall('ThingDef')
        patches = {n.findtext('defName'): n for n in ET.parse(ROOT/'Common/Patches/Generated_Guns_CE.xml').getroot()}
        self.assertEqual(len(guns), 52)
        mechanisms = {'power':'Power', 'tech':'Tech', 'smart':'Smart'}
        for gun in guns:
            name = gun.findtext('defName')
            typ = next(n.text.removeprefix('RPC_Type_') for n in gun.findall('weaponTags/li') if n.text.startswith('RPC_Type_'))
            sound = gun.findtext('verbs/li/soundCast')
            self.assertTrue(sound.startswith('RPC_Shot_' + mechanisms[typ] + '_'), name)
            self.assertEqual(sound, patches[name].findtext('Properties/soundCast'))
            self.assertIsNone(patches[name].find('Properties/soundCastTail'))


if __name__ == '__main__': unittest.main()
