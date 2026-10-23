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


def check(dll, enabled):
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
                if not enabled:
                    assert get(endpoint)[0] == 404, 'PROD config must not expose a DEV channel'
                    print('PASS PROD configuration has no DEV endpoint')
                    return
                assert get(endpoint)[0] == 503, 'Missing channel must fail closed'
                manifest = dict(commitSha='a'*40, serverImage='fixture-exact-digest')
                (root / 'active.json').write_text(json.dumps(manifest))
                status, body, headers = get(endpoint)
                assert status == 200 and json.loads(body) == manifest and headers['Cache-Control'] == 'no-store'
                (root / 'updating').write_text('update in progress')
                assert get(endpoint)[0] == 503, 'Transition must block launcher'
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
    check(args.dll, True)
    check(args.dll, False)
