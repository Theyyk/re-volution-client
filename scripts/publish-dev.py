"""Publish one complete DEV manifest containing both server and client identity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
from dev_channel import REPO, DOCKER, gh, api, latest_candidate, current_channel, can_promote, marker


def one_jar(directory):
    jars = [p for p in Path(directory).rglob('*.jar') if not p.name.endswith(('-sources.jar', '-dev.jar'))]
    if len(jars) != 1:
        raise RuntimeError(f'Expected one payload JAR in {directory}')
    return jars[0]


def managed(path, tag, role):
    return dict(path=f'mods/{path.name}', role=role, size=path.stat().st_size,
                sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                downloadUrl=f'https://github.com/{REPO}/releases/download/{tag}/{path.name}')


def releases():
    pages = json.loads(gh('api', '--paginate', '--slurp', f'repos/{REPO}/releases?per_page=100'))
    return [release for page in pages for release in page]


def delete_tag_if_present(tag):
    try:
        gh('api', '--method', 'DELETE', f'repos/{REPO}/git/refs/tags/{tag}')
    except subprocess.CalledProcessError:
        # Deleted tags leave release objects behind; missing refs are an expected cleanup case.
        pass


def ensure_dev_latest_ref(client_sha):
    try:
        gh('api', f'repos/{REPO}/git/ref/tags/dev-latest')
    except subprocess.CalledProcessError:
        gh('api', '--method', 'POST', f'repos/{REPO}/git/refs',
           '-f', 'ref=refs/tags/dev-latest', '-f', f'sha={client_sha}')
    else:
        gh('api', '--method', 'PATCH', f'repos/{REPO}/git/refs/tags/dev-latest',
           '-f', f'sha={client_sha}', '-F', 'force=true')


def ensure_dev_latest_release(client_sha, known_releases):
    ensure_dev_latest_ref(client_sha)
    release = next((r for r in known_releases if r['tag_name'] == 'dev-latest'), None)
    # Deleting only the Git tag leaves a release object whose URL becomes /untagged-*.
    # Recreate that object once so release-by-tag works again and future uploads are normal.
    if release is not None and '/untagged-' in release.get('html_url', ''):
        gh('api', '--method', 'DELETE', f"repos/{REPO}/releases/{release['id']}")
        release = None
    if release is None:
        gh('release', 'create', 'dev-latest', '--repo', REPO, '--target', client_sha,
           '--prerelease', '--latest=false', '--title', 'DEV channel (testing only)',
           '--notes', 'Unsigned local Docker DEV channel. Stable production distribution is unchanged.')


def build_generation(tag):
    match = re.fullmatch(
        r'dev-build-(\d+)-(\d+)-(\d+)-(\d+)-([a-f0-9]{40})-(\d+)-(\d+)', tag)
    if match is None:
        return None
    # Source run/attempt and image run/attempt identify the gameplay generation;
    # client run/attempt orders rebuilds of that same generation.
    return tuple(int(match.group(index)) for index in (1, 2, 3, 4, 6, 7))


def prune_transient_releases(active_candidate, active_build):
    active_candidate_marker = marker(active_candidate)
    active_build_generation = build_generation(active_build)
    if active_build_generation is None:
        raise RuntimeError('Invalid active DEV build tag')

    for release in releases():
        tag = release['tag_name']
        should_delete = False
        if tag.startswith('dev-candidate-') and tag != active_candidate['candidateId']:
            try:
                release_candidate = json.loads(release['body'])
                should_delete = marker(release_candidate) < active_candidate_marker
            except (KeyError, TypeError, ValueError, json.JSONDecodeError):
                # Unknown/malformed state is preserved rather than risking deletion of a newer payload.
                should_delete = False
        elif tag.startswith('dev-build-') and tag != active_build:
            release_generation = build_generation(tag)
            should_delete = release_generation is not None and release_generation < active_build_generation

        if should_delete:
            gh('api', '--method', 'DELETE', f"repos/{REPO}/releases/{release['id']}")
            delete_tag_if_present(tag)


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
        known_releases = releases()
        existing = next((r for r in known_releases if r['tag_name'] == tag), None)
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
        ensure_dev_latest_release(client_sha, known_releases)
        # Single mutable document is the atomic visibility boundary for BOTH consumers.
        gh('release', 'upload', 'dev-latest', str(manifest), '--repo', REPO, '--clobber')
        # Only generations proven older than this publication may be removed. A late older
        # publisher must never delete a candidate/build that appeared after it started.
        prune_transient_releases(m, tag)
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
