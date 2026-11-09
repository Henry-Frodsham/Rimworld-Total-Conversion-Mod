"""
Runs the RimPunk CE live test suite in the real game (Source/RimPunkCE/LiveTests) and reports the result.

    python run_live_tests.py                     everything
    python run_live_tests.py --filter gigs       scenarios whose name or group contains "gigs"
    python run_live_tests.py --bubbles           also load Interaction Bubbles
    python run_live_tests.py --no-build          skip dotnet build

Nothing of yours is touched. The game gets its own data folder under %TEMP% (config, saves, logs), so your mod list,
settings and saves stay as they are. Steam's API doesn't start for a game launched like this, so workshop mods can't load
through Steam: Harmony and CE are linked into the game's Mods folder for the run (directory junctions) and the links are
removed afterwards, even on a crash. Exit code: 0 all passed, 1 failures, 2 the run itself didn't finish.

The one shared file: Diagnostics/diag.log in the mod folder is rewritten, as it is on any game start.
"""
import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import time

GAME = r'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
WORKSHOP = r'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100'
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
PROJECT = os.path.join(ROOT, 'Source', 'RimPunkCE')
RESULTS = os.path.join(ROOT, 'Diagnostics', 'livetest.log')
DIAG = os.path.join(ROOT, 'Diagnostics', 'diag.log')
DATA = os.path.join(tempfile.gettempdir(), 'rimpunk_livetest_data')

LINKS = {'_livetest_harmony': os.path.join(WORKSHOP, '2009463077'), '_livetest_ce': os.path.join(WORKSHOP, '2890901044')}
BUBBLES = {'_livetest_bubbles': os.path.join(WORKSHOP, '1516158345')}
MODS = ['brrainz.harmony', 'ludeon.rimworld', 'ludeon.rimworld.royalty', 'ludeon.rimworld.ideology', 'ludeon.rimworld.biotech',
        'ludeon.rimworld.anomaly', 'ludeon.rimworld.odyssey', 'ceteam.combatextended', 'mackos.bakos']


def game_version():
    with open(os.path.join(GAME, 'Version.txt'), encoding='utf-8') as f:
        return f.read().strip()


def write_config(mods):
    """A fresh data folder each run: nothing carries over between runs."""
    shutil.rmtree(DATA, ignore_errors=True)
    config = os.path.join(DATA, 'Config')
    os.makedirs(config)
    active = ''.join(f'    <li>{m}</li>\n' for m in mods)
    known = ''.join(f'    <li>{m}</li>\n' for m in mods if m.startswith('ludeon.') and m != 'ludeon.rimworld')
    with open(os.path.join(config, 'ModsConfig.xml'), 'w', encoding='utf-8') as f:
        f.write(f'<?xml version="1.0" encoding="utf-8"?>\n<ModsConfigData>\n  <version>{game_version()}</version>\n  <activeMods>\n{active}  </activeMods>\n'
                f'  <knownExpansions>\n{known}  </knownExpansions>\n</ModsConfigData>\n')
    # keep running without focus (it pauses otherwise), small window, silent, no autosaves in the way
    with open(os.path.join(config, 'Prefs.xml'), 'w', encoding='utf-8') as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\n<PrefsData>\n  <runInBackground>True</runInBackground>\n  <fullscreen>False</fullscreen>\n'
                '  <screenWidth>1280</screenWidth>\n  <screenHeight>720</screenHeight>\n  <volumeMaster>0</volumeMaster>\n'
                '  <autosaveIntervalDays>100</autosaveIntervalDays>\n  <devMode>True</devMode>\n  <resetModsConfigOnCrash>False</resetModsConfigOnCrash>\n</PrefsData>\n')


def make_links(links):
    made = []
    for name, target in links.items():
        link = os.path.join(GAME, 'Mods', name)
        if os.path.exists(link):
            os.rmdir(link)  # left by a killed run; rmdir only removes the junction, never what it points to
        subprocess.run(['cmd', '/c', 'mklink', '/J', link, target], check=True, capture_output=True)
        made.append(link)
    return made


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(errors='replace')  # weapon/faction names may not fit the Windows console code page
    ap = argparse.ArgumentParser()
    ap.add_argument('--filter', default='')
    ap.add_argument('--timeout', type=int, default=1500, help='seconds before the run is abandoned')
    ap.add_argument('--bubbles', action='store_true')
    ap.add_argument('--no-build', action='store_true')
    args = ap.parse_args()

    if not args.no_build:
        b = subprocess.run(['dotnet', 'build', '-nologo', '-v', 'q'], cwd=PROJECT, capture_output=True, text=True)
        if b.returncode != 0:
            print(b.stdout[-3000:])
            return 2

    mods, links = list(MODS), dict(LINKS)
    if args.bubbles:
        links.update(BUBBLES)
        mods.append('jaxe.bubbles')
    write_config(mods)
    if os.path.exists(RESULTS):
        os.remove(RESULTS)

    made = make_links(links)
    flag = '-rimpunktests' + (f'={args.filter}' if args.filter else '')
    finished = False
    try:
        t0 = time.time()
        proc = subprocess.Popen([os.path.join(GAME, 'RimWorld.exe'), '-quicktest', flag, f'-savedatafolder={DATA}'], cwd=GAME,
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            proc.wait(timeout=args.timeout)
            finished = True
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()
            print(f'abandoned after {args.timeout}s')
        print(f'game ran {time.time() - t0:.0f}s, exit code {proc.returncode}')
    finally:
        for link in made:
            os.rmdir(link)

    lines = open(RESULTS, encoding='utf-8').read().splitlines() if os.path.exists(RESULTS) else []
    summary = next((l for l in lines if ' SUMMARY ' in l), None)
    for line in lines:
        if ' FAIL ' in line or ' WARN ' in line:
            print(line[:500])
    if not summary:
        print('no SUMMARY line: the run did not finish.')
        print('last lines of the results:', *lines[-5:], sep='\n  ')
        log = os.path.join(DATA, 'Logs', 'Player.log')
        print('game log:', log)
        return 2
    print(summary)
    print('results:', RESULTS)
    print('diagnostics:', DIAG)
    return 0 if finished and ' 0 failed' in summary else 1


if __name__ == '__main__':
    sys.exit(main())
