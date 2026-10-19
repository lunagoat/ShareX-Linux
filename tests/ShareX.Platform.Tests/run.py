#!/usr/bin/python
# GPL-3.0-or-later. Offline integration tests; all state stays under /tmp/sx-wp1-platform.
import json, os, shutil, subprocess, sys, time
from pathlib import Path
here = Path(__file__).resolve().parent
repo = here.parent.parent
root = Path('/tmp/sx-wp1-platform/tests')
if '--private' not in sys.argv:
    environment = dict(os.environ, SX_LIVE_DBUS=os.environ.get('DBUS_SESSION_BUS_ADDRESS', ''))
    sys.exit(subprocess.call(['dbus-run-session', '--', sys.executable, str(__file__), '--private'], env=environment))
root.mkdir(parents=True, exist_ok=True)
os.environ.update(SX_SANDBOX=str(root), SX_TEST_ROOT=str(root))
for variable, suffix in [('HOME','home'), ('XDG_CONFIG_HOME','config'), ('XDG_DATA_HOME','data'), ('XDG_CACHE_HOME','cache'), ('XDG_STATE_HOME','state')]:
    directory = root / suffix; directory.mkdir(exist_ok=True); os.environ[variable] = str(directory)
commands = root / 'bin'; commands.mkdir(exist_ok=True)
mock = '''#!/usr/bin/python
import json, os, sys
from pathlib import Path
root = Path(os.environ['SX_TEST_ROOT'])
with (root / 'commands.jsonl').open('a') as stream: stream.write(json.dumps([Path(sys.argv[0]).name, *sys.argv[1:]]) + '\\n')
if Path(sys.argv[0]).name == 'lp':
    path = Path(sys.argv[-1]); assert path.read_bytes().startswith(b'%PDF'); (root / 'spooled.pdf').write_bytes(path.read_bytes())
if Path(sys.argv[0]).name == 'secret-tool':
    key = root / 'fake-keyring'
    if sys.argv[1] == 'store': key.write_text(sys.stdin.read())
    elif key.exists(): print(key.read_text())
    else: sys.exit(1)
'''
for command in ['xdg-open', 'lp', 'secret-tool']:
    path = commands / command; path.write_text(mock); path.chmod(0o700)
os.environ['PATH'] = str(commands) + ':' + os.environ['PATH']
exe = here / 'bin/Debug/net10.0/ShareX.Platform.Tests'
def run(arguments, **kwargs):
    result = subprocess.run([str(exe), *arguments], timeout=30, check=True, **kwargs)
    return result
for marker in ['dbus-ready', 'ipc-ready', 'commands.jsonl']:
    (root / marker).unlink(missing_ok=True)
def ready(marker, process):
    for _ in range(150):
        if (root / marker).exists(): return
        if process.poll() is not None: raise RuntimeError('process exited before ' + marker)
        time.sleep(.05)
    raise TimeoutError(marker)
server = subprocess.Popen([sys.executable, str(here/'fake-desktop.py')])
try:
    ready('dbus-ready', server)
    run([])
    run(['--security-read'])
    run(['--notify'])
    notification = json.loads((root/'notification.json').read_text())
    assert notification[0] == 'ShareX' and 'folder' in notification[5] and notification[4] == '&lt;test&gt; &amp; body', notification
    assert json.loads((root/'showitems.json').read_text())[0][0].startswith('file:///tmp/sx-wp1-platform/')
    recorded = [json.loads(line) for line in (root/'commands.jsonl').read_text().splitlines()]
    assert all(len(entry) == 2 for entry in recorded if entry[0] == 'xdg-open')
    assert any(entry[0] == 'lp' and 'fake-printer' in entry for entry in recorded)
    for desktop in (root/'data/applications').glob('*.desktop'):
        subprocess.run(['desktop-file-validate', str(desktop)], check=True, timeout=5)
    for filename in ['image-print.pdf', 'text-print.pdf']:
        output = subprocess.check_output(['pdfinfo', str(root/filename)], text=True, timeout=5)
        assert 'Pages:' in output
        if filename.startswith('text'): assert int(next(line.split(':')[1] for line in output.splitlines() if line.startswith('Pages:'))) > 1
    ipc = subprocess.Popen([str(exe), '--ipc-server'])
    try:
        ready('ipc-ready', ipc)
        payload = ['--PrintScreen', 'spaces 雪 "quotes"', 'literal\\path', '\nline']
        run(['--ipc-client', *payload])
        assert ipc.wait(timeout=10) == 0
        assert json.loads((root/'ipc-received.json').read_text()) == payload
    finally:
        if ipc.poll() is None: ipc.terminate(); ipc.wait(timeout=5)
    run(['--ui'])
    print('PASS private D-Bus, desktop commands, PDF parsing, IPC, and headless UI')
finally:
    server.terminate(); server.wait(timeout=5)
# Test Secret Service selection with an isolated fake secret-tool, never the live keyring.
secret_env = os.environ.copy(); secret_env.pop('SX_SANDBOX', None)
secret_env['XDG_CONFIG_HOME'] = str(root/'secret-config')
run([], env=secret_env)
run(['--security-read'], env=secret_env)
assert (root/'fake-keyring').exists()
print('PASS Secret Service key backend with fake secret-tool')
# Run the actual app on the live desktop only in a sandbox, then close through IPC.
profile = root/'app'
config = profile/'config/ShareX'; config.mkdir(parents=True, exist_ok=True)
(config/'ApplicationConfig.json').write_text(json.dumps({'ShowStartScreen': False, 'ShowTray': True, 'AutoCheckUpdate': False, 'FirstTimeMinimizeToTray': False}))
appenv = os.environ.copy(); appenv['SX_SANDBOX'] = str(profile)
appenv['DBUS_SESSION_BUS_ADDRESS'] = os.environ['SX_LIVE_DBUS']
app = repo/'ShareX/bin/Debug/net10.0/ShareX'
log = profile/'data/ShareX/Logs'/time.strftime('ShareX-Log-%Y-%m.txt')
with (root/'app-output.log').open('w') as output:
    first = subprocess.Popen(['timeout','-k','5','20',str(app),'-silent','-NoHotkeys'],env=appenv,stdout=output,stderr=output)
    try:
        for _ in range(150):
            if log.exists() and 'Hotkey host init finished.' in log.read_text(encoding='utf-8-sig'): break
            if first.poll() is not None: raise RuntimeError('app startup failed')
            time.sleep(.05)
        else: raise TimeoutError('app startup')
        subprocess.run(['timeout','-k','5','10',str(app),'-exit'],env=appenv,check=True)
        assert first.wait(timeout=10) == 0
    finally:
        if first.poll() is None: first.terminate(); first.wait(timeout=6)
for name in ['ApplicationConfig.json','UploadersConfig.json','HotkeysConfig.json']:
    assert json.loads((config/name).read_text(encoding='utf-8-sig'))
assert (profile/'data/ShareX/History.db').exists()
text = log.read_text(encoding='utf-8-sig')
assert 'Arguments received: -exit' in text and 'ShareX closed.' in text and 'Unhandled exception' not in text, text
subprocess.run(['timeout','-k','5','10',str(app),'-silent','-NoHotkeys','-exit'],env=appenv,check=True)
print('PASS real Avalonia startup, settings persistence/reload, second-instance forwarding, graceful exit')
