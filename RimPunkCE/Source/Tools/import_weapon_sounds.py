"""Extract single-shot attacks from user-generated recordings; originals remain read-only.

python Source/Tools/import_weapon_sounds.py --source D:/RimworldSounds
Requires ffmpeg, Python stdlib only. Outputs mono PCM WAVs, SoundDefs, provenance/QA and an audition reel.
"""
import argparse
from array import array
import hashlib
import json
import math
from pathlib import Path
import subprocess
import wave
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
RATE = 44100
FRAME = 88  # ~2 ms attack envelope
PROFILES = json.loads((ROOT / 'Source/Tools/data/weapon_sounds.json').read_text(encoding='utf-8'))['profiles']


def decode(path):
    result = subprocess.run(['ffmpeg', '-v', 'error', '-i', str(path), '-ac', '1', '-ar', str(RATE),
        '-af', 'highpass=f=55,lowpass=f=16500', '-f', 'f32le', '-'], check=True, capture_output=True)
    samples = array('f')
    samples.frombytes(result.stdout)
    return samples


def envelope(samples):
    return [math.sqrt(sum(v*v for v in samples[i:i+FRAME]) / max(1, len(samples[i:i+FRAME]))) for i in range(0, len(samples), FRAME)]


def cut_shot(samples, limit):
    env = envelope(samples)
    if not env or max(env) < 0.0001: raise ValueError('Silent/unusable recording')
    peak = max(range(len(env)), key=env.__getitem__)
    # Find the leading rise of the strongest attack, not the start of a generated charge-up or bolt rattle.
    rises = [max(0., env[i] - sum(env[max(0, i-15):i]) / max(1, min(i,15))) for i in range(len(env))]
    lo = max(0, peak - 45)
    attack = max(range(lo, peak + 1), key=lambda i: rises[i])
    onset = attack
    while onset > max(0, attack - 15) and env[onset-1] > env[peak] * 0.055:
        onset -= 1
    start = max(0, onset * FRAME - int(RATE * 0.002))
    end = min(len(samples), start + int(limit * RATE))
    # A fresh high-energy rise after a quiet valley is another shot, not the first shot's decay.
    for i in range(attack + max(1, int(0.075 * RATE / FRAME)), min(len(env), math.ceil(end / FRAME))):
        if rises[i] > max(rises[attack] * 0.60, 0.015) and env[i] > env[peak] * 0.5 and min(env[max(0,i-8):i]) < env[i] * 0.35:
            end = min(end, i * FRAME - int(RATE * 0.006))
            break
    if end - start < RATE * 0.055: raise ValueError('No isolated attack of usable length')
    shot = samples[start:end]
    # Fade just the boundaries: preserve the transient, remove end-of-cut clicks and long AI reverberation.
    fade_in, fade_out = int(RATE * 0.0007), int(RATE * 0.020)
    for i in range(len(shot)):
        gain = min(1., i / max(1, fade_in), (len(shot)-1-i) / max(1, fade_out))
        shot[i] *= gain
    peak_value = max(abs(v) for v in shot)
    gain = (10 ** (-3/20)) / max(0.00001, peak_value)
    shot = array('f', (v * gain for v in shot))
    return shot, start / RATE, end / RATE


def write_wav(path, samples):
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = array('h', (round(max(-1.,min(1.,v)) * 32767) for v in samples))
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(1); out.setsampwidth(2); out.setframerate(RATE); out.writeframes(pcm.tobytes())


def generate_sound_defs():
    root = ET.Element('Defs')
    for profile in PROFILES.values():
        sound = ET.SubElement(root, 'SoundDef')
        for key,value in [('defName', profile['defName']), ('context','MapOnly'), ('maxVoices','12'),
            ('maxSimultaneous','4'), ('priorityMode','PrioritizeNewest')]: ET.SubElement(sound,key).text = value
        sub = ET.SubElement(ET.SubElement(sound, 'subSounds'), 'li')
        grain = ET.SubElement(ET.SubElement(sub,'grains'), 'li', {'Class':'AudioGrain_Folder'})
        ET.SubElement(grain,'clipFolderPath').text = profile['folder']
        for key,value in [('volumeRange', profile.get('volume', '28~30')), ('pitchRange','0.98~1.02'), ('distRange','12~65'), ('onCamera','false'),
            ('tempoAffectedByGameSpeed','false'), ('startDelayRange','0~0')]: ET.SubElement(sub,key).text = value
    ET.indent(root, space='  ')
    path = ROOT / 'Common/Defs/Sounds/Generated_WeaponSounds.xml'
    ET.ElementTree(root).write(path, encoding='utf-8', xml_declaration=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, default=Path('D:/RimworldSounds'))
    args = parser.parse_args()
    config = json.loads((args.source / 'sounds.json').read_text(encoding='utf-8'))
    sources = {s['id']: s for s in config['sounds']}
    records, reel, captions = [], array('f'), []
    for key,profile in PROFILES.items():
        group = sources[key]
        if group['folder'] != profile['folder']: raise ValueError('Source/config folder mismatch: ' + key)
        for variant in range(1, group['variants']+1):
            name = f'{key}_{variant:02d}'
            raw = args.source / 'raw' / (name + '.wav')
            source = raw if raw.exists() else args.source / 'output/Sounds' / profile['folder'] / (name + '.ogg')
            samples = decode(source)
            shot, start, end = cut_shot(samples, profile['maxSeconds'])
            target = ROOT / 'Common/Sounds' / profile['folder'] / (name + '.wav')
            write_wav(target, shot)
            env = envelope(shot)
            peak = max(abs(v) for v in shot)
            rms = math.sqrt(sum(v*v for v in shot) / len(shot))
            first_audible = next((i / RATE for i,v in enumerate(shot) if abs(v) > 0.015), None)
            record = dict(id=key, variant=variant, source=str(source), sourceSHA256=hashlib.sha256(source.read_bytes()).hexdigest(),
                sourceSeconds=round(len(samples)/RATE, 6), selectedStart=round(start,6), selectedEnd=round(end,6),
                output=str(target.relative_to(ROOT)).replace('\\','/'), seconds=round(len(shot)/RATE,6),
                peakDb=round(20*math.log10(peak),2), rmsDb=round(20*math.log10(rms),2), onsetMs=round((first_audible or 0)*1000,2),
                outputSHA256=hashlib.sha256(target.read_bytes()).hexdigest())
            records.append(record)
            if variant == 1:
                captions.append(f'{len(reel)/RATE:.2f}s {key}: single shot, then a three-shot sequence')
                reel.extend(shot); reel.extend([0.] * int(RATE * 0.35))
                # Overlap short tails as the game does; never encode an entire burst into a per-shot clip.
                spacing = 0.10 if key in ('gun_smg','gun_rifle','gun_mg','smart_light') else 0.24
                sequence = [0.] * (int(RATE * spacing * 2) + len(shot))
                for offset in (0, int(RATE * spacing), int(RATE * spacing * 2)):
                    for i,v in enumerate(shot): sequence[offset+i] += v * 0.65
                reel.extend(sequence); reel.extend([0.] * int(RATE * 0.65))
            print(f'{name}: {len(samples)/RATE:.3f}s -> {len(shot)/RATE:.3f}s, onset {record["onsetMs"]:.1f}ms, peak {record["peakDb"]:.1f}dBFS')
    generate_sound_defs()
    (ROOT / 'Source/Tools/data/weapon_sound_import.json').write_text(json.dumps(records, indent=2)+'\n', encoding='utf-8')
    qa = ROOT / 'Diagnostics/WeaponAudio'
    qa.mkdir(parents=True, exist_ok=True)
    max_peak = max(abs(v) for v in reel)
    if max_peak > 0.9: reel = array('f', (v*0.9/max_peak for v in reel))
    write_wav(qa / 'weapon-audition.wav', reel)
    (qa / 'weapon-audition.txt').write_text('\n'.join(captions)+'\n', encoding='utf-8')
    print(f'Imported {len(records)} single-shot clips across {len(PROFILES)} sound families.')


if __name__ == '__main__': main()
