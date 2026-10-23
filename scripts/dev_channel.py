"""Shared selection rules. Only the serialized dev-client workflow moves dev-latest."""
import json
import re
import subprocess

REPO = 'Theyyk/re-volution-client'
DOCKER = 'Theyyk/re-volution-docker'
MOD = 'Theyyk/re-volution-mod'


def gh(*args):
    return subprocess.check_output(['gh', *map(str, args)], text=True).strip()


def api(path):
    return json.loads(gh('api', path))


def eligible(branch):
    b = branch.lower()
    return bool(b) and b not in ('main', 'master', 'stable', 'release') and not b.startswith(('main/', 'master/', 'stable/', 'release/'))


def marker(m):
    return (m['sourceRun']['createdAt'], int(m['sourceRun']['id']), int(m['sourceRun']['attempt']),
            int(m['imageBuild']['id']), int(m['imageBuild']['attempt']))


def validate(m):
    if m['schemaVersion'] != 1 or not eligible(m['branch']):
        raise ValueError('Ineligible DEV source')
    if not re.fullmatch('[a-f0-9]{40}', m['commitSha']):
        raise ValueError('Invalid source SHA')
    if not re.fullmatch('ghcr.io/theyyk/re-volution-server@sha256:[a-f0-9]{64}', m['serverImage']):
        raise ValueError('DEV requires exact image digest')
    if not re.fullmatch('[a-f0-9]{64}', m['gameplaySha256']) or m['gameplaySize'] <= 0:
        raise ValueError('Invalid gameplay integrity metadata')
    marker(m)


def latest_candidate():
    pages = json.loads(gh('api', '--paginate', '--slurp', f'repos/{DOCKER}/releases?per_page=100'))
    candidates = []
    for release in (r for page in pages for r in page):
        if release['draft'] or not release['tag_name'].startswith('dev-candidate-'):
            continue
        m = json.loads(release['body'])
        validate(m)
        if m['candidateId'] != release['tag_name']:
            raise ValueError('Candidate identity mismatch')
        if not {'candidate.json', 'RE-volution.jar'} <= {a['name'] for a in release['assets']}:
            continue
        candidates.append(m)
    for m in sorted(candidates, key=marker, reverse=True):
        source = api(f"repos/{MOD}/actions/runs/{m['sourceRun']['id']}")
        build = api(f"repos/{DOCKER}/actions/runs/{m['imageBuild']['id']}")
        if (source['status'] == build['status'] == 'completed'
                and source['conclusion'] == build['conclusion'] == 'success'
                and source['event'] == 'push' and source['path'] == '.github/workflows/release.yml'
                and source['head_repository']['full_name'].lower() == MOD.lower()
                and build['head_repository']['full_name'].lower() == DOCKER.lower()
                and build['path'] == '.github/workflows/dev-server.yml'
                and source['head_branch'] == m['branch'] and source['head_sha'] == m['commitSha']
                and source['created_at'] == m['sourceRun']['createdAt']
                and source['run_attempt'] == m['sourceRun']['attempt']
                and build['run_attempt'] == m['imageBuild']['attempt']):
            return m
    raise RuntimeError('No complete successful DEV server candidate is available')


def current_channel():
    # 404 is the only valid bootstrap case. Do not turn network/auth errors into an empty watermark.
    pages = json.loads(gh('api', '--paginate', '--slurp', f'repos/{REPO}/releases?per_page=100'))
    release = next((r for page in pages for r in page if r['tag_name'] == 'dev-latest'), None)
    if release is None:
        return None
    asset = next((a for a in release['assets'] if a['name'] == 'manifest.dev.json'), None)
    if asset is None:
        return None
    return json.loads(gh('api', '-H', 'Accept: application/octet-stream', f"repos/{REPO}/releases/assets/{asset['id']}"))


def can_promote(candidate, old):
    # Legacy develop-only manifest has no server metadata; allow one migration.
    if old is None or 'sourceRun' not in old:
        return True
    validate(old)
    return marker(candidate) >= marker(old)
