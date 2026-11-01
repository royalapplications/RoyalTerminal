#!/usr/bin/env python3
"""Acceptance for real API, NDJSON, and Lua consumers of independently scoped activity journals."""
import concurrent.futures
import http.client
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2] / 'samples'
CLI = ROOT / 'RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll'
HOST = ROOT / 'RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll'


def eventually(predicate, seconds=10):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        if predicate():
            return
        time.sleep(.025)
    raise TimeoutError('The expected state was not reached')


def run(directory):
    sock = str(directory / 'private.sock')
    admin = secrets.token_hex(32)
    env = dict(os.environ, ROYALTERMINAL_MUX_SOCKET=sock, ROYALTERMINAL_MUX_TOKEN=admin)

    class Local(http.client.HTTPConnection):
        def connect(self):
            self.sock = socket.socket(socket.AF_UNIX)
            self.sock.settimeout(8)
            self.sock.connect(sock)

    def call(method, args, credential=admin, status=(200,)):
        connection = Local('localhost')
        try:
            connection.request('POST', '/api/call', json.dumps(dict(method=method, arguments=args)),
                               {'Content-Type': 'application/json', 'Authorization': 'Bearer ' + credential})
            response = connection.getresponse()
            body = json.loads(response.read())
            assert response.status in status, (method, response.status, body.get('code'))
            return body
        finally:
            connection.close()

    def cli(*args, key=admin):
        result = subprocess.run(['dotnet', CLI, *args], env=dict(env, ROYALTERMINAL_MUX_TOKEN=key),
                                text=True, capture_output=True, timeout=12)
        assert result.returncode == 0, (args, result.stderr)
        return [json.loads(line) for line in result.stdout.splitlines()]

    def page(method, scope, after, key=admin):
        events = []
        while True:
            p = call(method, dict(scope, after=after, limit=2), key)
            for event in p['events']:
                after += 1
                assert event['sequence'] == after
                events.append(event)
            assert p['nextSequence'] == after
            if after == p['lastSequence']:
                return events

    watcher = None
    with open(directory / 'host.log', 'wb') as log:
        broker = subprocess.Popen(['dotnet', HOST, '--Kestrel:Endpoints:Private:Url=http://unix:' + sock],
                                  env=env, stdout=log, stderr=log)
        try:
            eventually(lambda: os.path.exists(sock))
            a = call('session.create', {'label': 'origin'})['id']
            b = call('session.create', {'label': 'event-target'})['id']
            sa, sb = {'sessionId': a}, {'sessionId': b}
            grant = call('share.create', dict(sb, role='observer')); token = grant['token']
            for method in ('server.events', 'server.all_events'):
                call(method, {}, token, (403,))
            call('session.events', sa, token, (403,))
            before = call('session.events', sb, token)['nextSequence']
            global_fence = call('server.all_events', {})['nextSequence']
            ready = directory / 'ready'
            script = '''
                local saw_window=false
                rex.on('window_created', function(ctx, ev)
                    assert(ctx.session_id == rex.args.watched and ctx.session_id ~= rex.session_id)
                    assert(ev.window_id and ev.label == 'ephemeral')
                    assert(assert(rex.session.view{}).id == ctx.session_id)
                    assert(not pcall(rex.on, 'window_closed', function() end))
                    saw_window=true
                end)
                rex.on('com.superlogical.terminal.child_exited', function(ctx, ev)
                    assert(saw_window and ev.exit_code == 37 and ctx.block_id == ev.block_id)
                    assert(assert(rex.session.view{}).id == rex.args.watched)
                    print('FOREIGN_SESSION_EXIT_OK'); rex.stop(0)
                end)
                assert(io.open(rex.args.ready,'w')):close()
                rex.after(7, function() rex.stop(19) end)
            '''
            watcher = subprocess.Popen(['dotnet', CLI, 'do', '-e', script, '--all', '--args',
                                        json.dumps({'ready': str(ready), 'watched': b})],
                                       env=dict(env, ROYALTERMINAL_MUX_SESSION=a),
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            eventually(lambda: ready.exists() or watcher.poll() is not None)
            assert ready.exists(), watcher.communicate()[1]
            w = call('session.new_window', dict(sb, label='ephemeral'))['id']
            call('session.close_window', dict(sb, windowId=w))
            call('session.set_label', dict(sa, label='not visible to share'))
            block = call('block.create', dict(sb, block={'label': 'child', 'transport': 'pipe', 'command': {
                'fileName': '/usr/bin/python3', 'arguments': ['-c', "import os;os.write(1,b'\\x07');raise SystemExit(37)"]}}))['id']
            _, stderr = watcher.communicate(timeout=12)
            assert watcher.returncode == 0 and 'FOREIGN_SESSION_EXIT_OK' in stderr, stderr
            local = page('session.events', sb, before, token)
            assert all(e['sessionId'] == b for e in local)
            names = [e['type'] for e in local]
            assert names.index('window_created') < names.index('window_closed') < names.index('block_created')
            terminal = [e for e in local if e['type'] == 'block_event']
            assert [(e['blockId'], e['detail']['name']) for e in terminal] == [(block,'terminal.bell'),(block,'terminal.child_exited')]
            assert terminal[-1]['detail']['exitCode'] == 37
            global_events = page('server.all_events', {}, global_fence)
            assert {e['sessionId'] for e in global_events} == {a,b}
            rows = cli('events', '-s', b, '--json', '--after', str(before), '--count', str(len(local)), key=token)
            assert [r['sequence'] for r in rows] == [e['sequence'] for e in local]
            assert rows[-1]['payload']['event']['payload']['payload']['exit_code'] == 37
            assert cli('events', '--all', '--after', str(global_fence), '--count', '1')[0] == global_events[0]
            assert cli('events', '--json', '--after','0','--count','1')[0]['payload']['session']['session_id'] == a
            # Revoking the token either rejects the request at admission, or while it is parked before publication.
            with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
                future = pool.submit(call,'session.events',dict(sb,after=local[-1]['sequence'],timeoutMs=2000), token,(401,403))
                call('share.revoke',dict(sb,shareId=grant['id']))
                call('session.set_label',dict(sb,label='not visible after revoke'))
                assert 'events' not in future.result(timeout=5)
            print('Verified scoped and global CLI journals, one-event pagination, callback session inheritance, a real bell and exit 37, and grant revocation.')
        finally:
            if watcher is not None and watcher.poll() is None:
                watcher.kill(); watcher.wait(timeout=5)
            broker.terminate(); broker.wait(timeout=10)


if __name__ == '__main__':
    with tempfile.TemporaryDirectory() as temp:
        run(Path(temp))
