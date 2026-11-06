"""Integration test of the actual Auth HTTP endpoint with isolated temporary files/database."""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import urlopen


REMOTE_AUTH = 'https://remote-dev.example.test/'
REMOTE_HOST = '26.1.2.3'


def check(dll, enabled, remote_enabled=False):
    with tempfile.TemporaryDirectory(prefix='revolution-auth-channel-') as directory:
        root = Path(directory)
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            port = sock.getsockname()[1]
        base = f'http://127.0.0.1:{port}'
        env = dict(os.environ, REVOLUTION_AUTH_URL=base, REVOLUTION_AUTH_DB=str(root / 'auth.db'))
        env.pop('REVOLUTION_DEV_CHANNEL_DIRECTORY', None)
        if enabled:
            env['REVOLUTION_DEV_CHANNEL_DIRECTORY'] = directory
        if remote_enabled:
            (root / 'remote.json').write_text(json.dumps(dict(
                authBaseUrl=REMOTE_AUTH, serverHost=REMOTE_HOST, serverPort=25568)))
        with (root / 'server.log').open('w') as log:
            process = subprocess.Popen(['dotnet', str(Path(dll).resolve())], env=env, stdout=log, stderr=log,
                                       creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
            def get(path):
                try:
                    with urlopen(base + path, timeout=2) as response:
                        return response.status, response.read(), response.headers
                except HTTPError as e:
                    return e.code, e.read(), e.headers
            try:
                for _ in range(100):
                    try:
                        if get('/health')[0] == 200:
                            break
                    except (URLError, TimeoutError):
                        pass
                    time.sleep(.1)
                else:
                    raise RuntimeError('Auth did not become ready')
                endpoint = '/api/v1/dev/channel'
                remote_endpoint = endpoint + '/remote'
                if not enabled:
                    assert get(endpoint)[0] == 404, 'PROD config must not expose a DEV channel'
                    assert get(remote_endpoint)[0] == 404, 'PROD config must not expose Remote DEV'
                    print('PASS PROD configuration has no DEV endpoint')
                    return

                assert get(endpoint)[0] == 503, 'Missing channel must fail closed'
                assert get(remote_endpoint)[0] == (503 if remote_enabled else 404)
                manifest = dict(
                    schemaVersion=1, developmentOnly=True, channel='dev',
                    commitSha='a'*40, serverImage='fixture-exact-digest',
                    auth=dict(baseUrl='http://127.0.0.1:25567/'),
                    server=dict(host='127.0.0.1', port=25568),
                    files=[dict(path='mods/RE-volution.jar', sha256='b'*64, size=123)])
                (root / 'active.json').write_text(json.dumps(manifest))
                status, body, headers = get(endpoint)
                assert status == 200 and json.loads(body) == manifest and headers['Cache-Control'] == 'no-store'

                if remote_enabled:
                    status, body, headers = get(remote_endpoint)
                    remote = json.loads(body)
                    assert status == 200 and headers['Cache-Control'] == 'no-store'
                    assert remote['auth']['baseUrl'] == REMOTE_AUTH
                    assert remote['server'] == dict(host=REMOTE_HOST, port=25568)
                    for field in ('commitSha', 'serverImage', 'files', 'channel', 'developmentOnly'):
                        assert remote[field] == manifest[field], f'Remote projection changed accepted {field}'
                    print('PASS Remote DEV rewrites only transport endpoints on the accepted snapshot')
                else:
                    assert get(remote_endpoint)[0] == 404, 'Remote DEV must be disabled without explicit settings'

                (root / 'updating').write_text('update in progress')
                assert get(endpoint)[0] == 503, 'Transition must block local launcher'
                if remote_enabled:
                    assert get(remote_endpoint)[0] == 503, 'Transition must block remote launcher'
                manifest['commitSha'] = 'b'*40
                (root / 'active.json').write_text(json.dumps(manifest))
                assert get(endpoint)[0] == 503
                (root / 'updating').unlink()
                assert json.loads(get(endpoint)[1]) == manifest
                print('PASS Auth serves accepted snapshot, blocks transitions and exposes new snapshot after acceptance')
            finally:
                process.terminate()
                process.wait(timeout=10)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dll', required=True)
    args = parser.parse_args()
    check(args.dll, True, remote_enabled=True)
    check(args.dll, True, remote_enabled=False)
    check(args.dll, False)
