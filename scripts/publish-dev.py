"""Publish one complete DEV manifest containing both server and client identity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
from dev_channel import REPO, DOCKER, gh, api, latest_candidate, current_channel, can_promote


def one_jar(directory):
    jars = [p for p in Path(directory).rglob('*.jar') if not p.name.endswith(('-sources.jar', '-dev.jar'))]
    if len(jars) != 1:
        raise RuntimeError(f'Expected one payload JAR in {directory}')
    return jars[0]


def managed(path, tag, role):
    return dict(path=f'mods/{path.name}', role=role, size=path.stat().st_size,
                sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                downloadUrl=f'https://github.com/{REPO}/releases/download/{tag}/{path.name}')


def publish(client_directory, output):
    m = latest_candidate()
    old = current_channel()
    if not can_promote(m, old):
        raise RuntimeError('Candidate is older than published DEV watermark')
    client_sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    head = gh('api', f'repos/{REPO}/commits/develop', '--jq', '.sha')
    if head != client_sha:
        raise RuntimeError('Client develop advanced; retry with the current client checkout')
    # Private GHCR identity was verified by Docker's trusted completion publisher.
    # The public receipt allows this workflow and end-user launcher to remain token-free.
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        gh('release', 'download', m['candidateId'], '--repo', REPO,
           '--pattern', 'RE-volution.jar', '--dir', str(root))
        gameplay = root / m['gameplayFile']
        (root / 'RE-volution.jar').rename(gameplay)
        if (hashlib.sha256(gameplay.read_bytes()).hexdigest() != m['gameplaySha256']
                or gameplay.stat().st_size != m['gameplaySize']):
            raise RuntimeError('Gameplay artifact integrity failure')
        client = one_jar(client_directory)
        tag = f"dev-build-{m['candidateId'].removeprefix('dev-candidate-')}-{client_sha}-{os.environ['GITHUB_RUN_ID']}-{os.environ['GITHUB_RUN_ATTEMPT']}"
        m.update(developmentOnly=True, channel='dev', productVersion=f"dev-{m['commitSha'][:12]}-{client_sha[:12]}",
                 manifestUrl=f'https://github.com/{REPO}/releases/download/{tag}/manifest.dev.json',
                 auth=dict(baseUrl='http://127.0.0.1:25567/'),
                 java=dict(majorVersion=8, provider='adoptium'),
                 minecraft=dict(version='1.12.2', forgeVersion='14.23.5.2859', memoryMb=4096),
                 server=dict(host='127.0.0.1', port=25568),
                 files=[managed(gameplay, tag, 'gameplay-mod'), managed(client, tag, 'client-mod')],
                 source=dict(gameplaySha=m['commitSha'], clientSha=client_sha,
                             gameplayBuild=m['sourceRun']['url'], channel='dev', branch=m['branch']))
        manifest = Path(output) / 'manifest.dev.json'
        manifest.parent.mkdir(parents=True, exist_ok=True)
        manifest.write_text(json.dumps(m, indent=2) + '\n', encoding='utf-8')
        releases = json.loads(gh('api', '--paginate', '--slurp', f'repos/{REPO}/releases?per_page=100'))
        existing = next((r for page in releases for r in page if r['tag_name'] == tag), None)
        if existing is None:
            gh('release', 'create', tag, str(gameplay), str(client), str(manifest), '--repo', REPO,
               '--target', client_sha, '--prerelease', '--latest=false', '--draft', '--title', tag,
               '--notes', f"DEV {m['branch']} @ {m['commitSha']}; server {m['serverImage']}")
            gh('release', 'edit', tag, '--repo', REPO, '--draft=false')
        else:
            # Never clobber an immutable release on rerun. Check the published bytes.
            gh('release', 'download', tag, '--repo', REPO, '--dir', str(root / 'existing'))
            for path in (gameplay, client, manifest):
                if (root / 'existing' / path.name).read_bytes() != path.read_bytes():
                    raise RuntimeError('Immutable DEV release differs on rerun')
        # The workflow concurrency lock serializes this read/check/write. Recheck both sources.
        if latest_candidate()['candidateId'] != m['candidateId']:
            raise RuntimeError('Newer complete candidate arrived; retry')
        if not can_promote(m, current_channel()):
            raise RuntimeError('Refusing a channel rollback')
        if gh('api', f'repos/{REPO}/commits/develop', '--jq', '.sha') != client_sha:
            raise RuntimeError('Client source changed; retry')
        if old is None and not any(r['tag_name'] == 'dev-latest' for page in releases for r in page):
            gh('release', 'create', 'dev-latest', '--repo', REPO, '--target', client_sha,
               '--prerelease', '--latest=false', '--title', 'DEV channel', '--notes', 'Unsigned testing channel')
        # Single mutable document is the atomic visibility boundary for BOTH consumers.
        gh('release', 'upload', 'dev-latest', str(manifest), '--repo', REPO, '--clobber')
        print(json.dumps(m, indent=2))
        if 'GITHUB_STEP_SUMMARY' in os.environ:
            with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as summary:
                summary.write(f"DEV `{m['branch']}` / `{m['commitSha']}` / `{m['serverImage']}`\n")


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--client-directory', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    publish(args.client_directory, args.output)
