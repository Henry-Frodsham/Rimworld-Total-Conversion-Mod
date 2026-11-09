"""Read-only gameplay log monitor with deduplicated reports and optional Windows desktop notifications.

python monitor_gameplay.py --minutes 240 --notify
Stop by creating Diagnostics/Monitoring/<session>/STOP, or terminating only this monitor's PID.
Never modifies the game, mod configuration, saves, recordings or assemblies.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]
PLAYER_ROOT = Path(os.environ.get('USERPROFILE', str(Path.home()))) / 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios'
STAMP = re.compile(r'^\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d')
BENIGN_CHECKS = re.compile(r'(animations drawing:|smart round fired:|smart trail recording on|smart trail finished:|smart trails drawing:)')


def utc(): return datetime.now(timezone.utc).isoformat(timespec='seconds')


def classify(source, line):
    if not line.strip() or re.match(r'^\s+(at |---|in )', line): return None
    if source == 'diag':
        if ' ERROR ' in line: return 'error'
        if ' CHECK ' in line and not BENIGN_CHECKS.search(line): return 'diagnostic'
        return None
    if re.search(r'\b(ERROR|FATAL)\b', line) and (STAMP.match(line) or line.startswith(('ERROR', 'FATAL', '['))): return 'error'
    if re.search(r'(^|\s)(?:[\w.]*Exception:|Error while |Error in |Could not resolve|Could not load|Failed to load|Grain couldn.t resolve|Crash!!!|Fatal error)', line): return 'error'
    if re.search(r'\bWARN(?:ING)?\b', line) and re.search(r'RimPunk|RPC_|sound|AudioClip|texture|missing|failed', line, re.I): return 'warning'
    return None


def fingerprint(source, line):
    value = re.sub(r'^\d{4}-\d\d-\d\d[ T][\d:.]+\s*', '', line)
    value = re.sub(r'^\d\d:\d\d:\d\d t-?\d+\s*', '', value)
    value = re.sub(r'\[Ref [A-Fa-f0-9]+\]|#\d+|0x[0-9a-fA-F]+|\bt\d+\b', '#', value)
    return hashlib.sha1((source + value).encode()).hexdigest()[:14]


class Tail:
    def __init__(self, path):
        self.path = path
        self.offset = 0
        self.identity = None
        self.header = b''
        self.pending = b''
        if path.exists():
            stat = path.stat()
            self.identity = (stat.st_dev, stat.st_ino)
            self.offset = stat.st_size  # old runs are a baseline, never mistaken for new errors
            with path.open('rb') as stream: self.header = stream.read(128)

    def poll(self):
        try:
            stat = self.path.stat()
            with self.path.open('rb') as stream:
                header = stream.read(128)
                rotated = self.identity != (stat.st_dev, stat.st_ino) or stat.st_size < self.offset or (self.header and header[:len(self.header)] != self.header)
                if rotated: self.offset = 0; self.pending = b''
                self.identity = (stat.st_dev, stat.st_ino)
                self.header = header
                stream.seek(self.offset)
                data = stream.read(4 * 1024 * 1024)
                self.offset = stream.tell()
            chunks = (self.pending + data).split(b'\n')
            self.pending = chunks.pop()
            return rotated, [chunk.decode('utf-8', errors='replace').rstrip('\r') for chunk in chunks]
        except (FileNotFoundError, PermissionError, OSError): return False, []


def toast(message):
    # Literal script + environment argument: log text is never executed as PowerShell code.
    script = r'''
try {
  [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime] > $null
  [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType=WindowsRuntime] > $null
  $message = [Security.SecurityElement]::Escape($env:RPC_MONITOR_MESSAGE)
  $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
  $xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>RimPunk test monitor</text><text>' + $message + '</text></binding></visual></toast>')
  $notification = [Windows.UI.Notifications.ToastNotification]::new($xml)
  [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Microsoft.Windows.PowerShell').Show($notification)
} catch { exit 1 }
'''
    env = dict(os.environ, RPC_MONITOR_MESSAGE=message[:350])
    try:
        result = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden', '-Command', script],
            env=env, capture_output=True, timeout=10, creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        return result.returncode == 0
    except (OSError, subprocess.TimeoutExpired): return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--minutes', type=int, default=240)
    parser.add_argument('--notify', action='store_true')
    parser.add_argument('--session', type=Path)
    args = parser.parse_args()
    session = args.session or ROOT / 'Diagnostics/Monitoring' / datetime.now().strftime('%Y-%m-%d_%H-%M-%S')
    session = session.resolve()
    allowed = (ROOT / 'Diagnostics/Monitoring').resolve()
    if allowed not in session.parents: raise ValueError('Session output must be under Diagnostics/Monitoring')
    session.mkdir(parents=True, exist_ok=True)
    (session / 'monitor.pid').write_text(str(os.getpid()), encoding='utf-8')
    paths = {'diag': ROOT / 'Diagnostics/diag.log', 'player': PLAYER_ROOT / 'Logs/Player.log'}
    tails = {key: Tail(path) for key,path in paths.items()}
    started, deadline = utc(), time.monotonic() + args.minutes * 60
    issues = {}
    pending = {}
    last_activity = None
    notifications_ok = toast('Monitoring armed for your next gameplay session. New issues will also be saved in Diagnostics/Monitoring.') if args.notify else None
    notify_times = []

    def event(message):
        record = {'time':utc(), 'event':message}
        with (session/'events.jsonl').open('a', encoding='utf-8') as out: out.write(json.dumps(record)+'\n')
        print(json.dumps(record), flush=True)

    def flush_issue(source):
        item = pending.pop(source, None)
        if item is None: return
        key = fingerprint(source, item['message'])
        if key in issues:
            issues[key]['count'] += 1
            issues[key]['lastSeen'] = item['time']
            return
        item.update(id=key, count=1, lastSeen=item['time'])
        item['rimpunkRelated'] = any(re.search(r'RimPunk|RPC_', part, re.I) for part in [item['message']] + item['context'])
        issues[key] = item
        with (session/'issues.jsonl').open('a', encoding='utf-8') as out: out.write(json.dumps(item,ensure_ascii=False)+'\n')
        print(json.dumps({'alert':item},ensure_ascii=False), flush=True)
        now = time.monotonic()
        notify_times[:] = [value for value in notify_times if now-value < 60]
        if args.notify and len(notify_times) < 4:
            toast(item['severity'].upper()+': '+item['message'])
            notify_times.append(now)

    def status(state):
        report = dict(state=state, pid=os.getpid(), started=started, heartbeat=utc(), lastLogActivity=last_activity,
            expiresAfterMinutes=args.minutes, uniqueIssues=len(issues), notificationsRequested=args.notify,
            notificationsInitialized=notifications_ok, sources={key:str(path) for key,path in paths.items()}, issues=list(issues.values()))
        temp = session/'status.tmp'
        temp.write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
        temp.replace(session/'status.json')
        lines = ['# Gameplay monitoring', '', f'State: {state}. Started: {started}. Heartbeat: {report["heartbeat"]}.',
            f'Unique issues: {len(issues)}. Desktop notifications initialized: {notifications_ok}.', '',
            'Full new log lines are saved in session.log. Repeated messages are grouped below.', '']
        for item in issues.values():
            lines += [f'## {item["severity"].upper()} — {item["count"]} occurrence(s)', '', item['message'], '',
                '```text', *item['context'][:15], '```', '']
        (session/'report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')

    event('Monitor armed; existing log content is the baseline. Waiting for new game activity.')
    status('running')
    last_status = 0.
    try:
        while time.monotonic() < deadline and not (session/'STOP').exists():
            for source, tail in tails.items():
                rotated, lines = tail.poll()
                if rotated: event(source + ' log opened/restarted; reading from the beginning.')
                if not lines: continue
                last_activity = utc()
                with (session/'session.log').open('a', encoding='utf-8') as out:
                    for line in lines: out.write(f'[{utc()} {source}] {line}\n')
                for line in lines:
                    if source == 'player' and ('Started: ' in line or 'Log closed.' in line): event(line.strip())
                    severity = classify(source, line)
                    if severity:
                        flush_issue(source)
                        pending[source] = dict(time=utc(), source=source, severity=severity, message=line, context=[])
                    elif source in pending:
                        if STAMP.match(line) or re.match(r'^\d\d:\d\d:\d\d t', line): flush_issue(source)
                        elif len(pending[source]['context']) < 20: pending[source]['context'].append(line)
            for source in list(pending): flush_issue(source)
            if time.monotonic() - last_status >= 5:
                status('running'); last_status = time.monotonic()
            time.sleep(1)
    finally:
        for source in list(pending): flush_issue(source)
        status('stopped')
        event('Monitor stopped. Reports and raw session log retained.')


if __name__ == '__main__': main()
