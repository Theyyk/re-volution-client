"""Offline tests of real candidate selection and publication watermark logic."""
import copy
import json
import hashlib
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import dev_channel as channel

spec = importlib.util.spec_from_file_location('publisher', Path(__file__).with_name('publish-dev.py'))
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


def candidate(run_id, branch='feature/new', created='2026-10-23T01:00:00Z', image_id=None):
    return dict(schemaVersion=1, branch=branch, commitSha=f'{run_id:040x}', gameplayVersion='1.3.8',
                gameplaySha256='a'*64, gameplaySize=123,
                serverImage='ghcr.io/theyyk/re-volution-server@sha256:'+'b'*64,
                candidateId=f'dev-candidate-{run_id}',
                sourceRun=dict(id=run_id, createdAt=created, attempt=1),
                imageBuild=dict(id=image_id or run_id+100, attempt=1, conclusion='success'))


class ChannelTests(unittest.TestCase):
    def select(self, candidates, failed=(), incomplete=(), tampered=False):
        candidates = copy.deepcopy(candidates)
        for m in candidates:
            if m['imageBuild']['id'] in failed + incomplete:
                m['imageBuild']['conclusion'] = 'failure' if m['imageBuild']['id'] in failed else None
        releases = [dict(draft=False, tag_name=m['candidateId'], body=json.dumps(m),
                         assets=[dict(name='candidate.json'), dict(name='RE-volution.jar')]) for m in candidates]
        def api(path):
            run_id = int(path.rsplit('/', 1)[1])
            source = '/re-volution-mod/' in path
            m = next(m for m in candidates if m['sourceRun' if source else 'imageBuild']['id'] == run_id)
            return dict(status='in_progress' if run_id in incomplete else 'completed',
                        conclusion='failure' if run_id in failed else 'success', event='push',
                        path='.github/workflows/release.yml' if source else '.github/workflows/dev-server.yml',
                        head_repository=dict(full_name=channel.MOD if source else channel.DOCKER),
                        head_branch='tampered' if tampered else m['branch'], head_sha=m['commitSha'],
                        created_at=m['sourceRun']['createdAt'], run_attempt=1)
        with patch.object(channel, 'gh', return_value=json.dumps([releases])), patch.object(channel, 'api', side_effect=api):
            return channel.latest_candidate()

    def test_any_branch(self):
        for branch in ('develop', 'feature/new', 'fix/bug', 'hotfix/urgent', 'arbitrary/experiment'):
            self.assertTrue(channel.eligible(branch))
            self.assertEqual(self.select([candidate(1, branch)])['branch'], branch)

    def test_excluded(self):
        for branch in ('main', 'main/1.3', 'master', 'stable', 'stable/1.3', 'release', 'release/1.3', 'MAIN', ''):
            self.assertFalse(channel.eligible(branch))
            with self.assertRaises(ValueError):
                channel.validate(candidate(1, branch))

    def test_older_build_finishes_later(self):
        # Old source run 10 finishes image build 999 after the newer source run 11's image 100.
        old = candidate(10, 'feature/slow', image_id=999)
        new = candidate(11, 'fix/fast', image_id=100)
        self.assertEqual(self.select([old, new])['commitSha'], new['commitSha'])
        self.assertEqual(self.select([new, old])['commitSha'], new['commitSha'])
        self.assertFalse(channel.can_promote(old, new))
        self.assertTrue(channel.can_promote(new, old))

    def test_created_at_precedes_id_and_completion(self):
        old = candidate(10, created='2026-10-22T01:00:00Z', image_id=999)
        new = candidate(11, created='2026-10-23T01:00:00Z', image_id=100)
        self.assertEqual(self.select([old, new])['sourceRun']['id'], 11)

    def test_failed_or_incomplete_candidate_cannot_win(self):
        old, new = candidate(10), candidate(11)
        self.assertEqual(self.select([new, old], failed=(111,))['sourceRun']['id'], 10)
        self.assertEqual(self.select([new, old], failed=(11,))['sourceRun']['id'], 10)
        self.assertEqual(self.select([new, old], incomplete=(111,))['sourceRun']['id'], 10)

    def test_no_ready_candidate_fails_closed(self):
        with self.assertRaises(RuntimeError):
            self.select([candidate(1)], incomplete=(101,))

    def test_forged_source_cannot_win(self):
        with self.assertRaises(RuntimeError):
            self.select([candidate(1)], tampered=True)

    def test_digest_required(self):
        m = candidate(1)
        m['serverImage'] = 'ghcr.io/theyyk/re-volution-server:dev'
        with self.assertRaises(ValueError):
            channel.validate(m)

    def test_rerun_does_not_beat_newer_source(self):
        old, new = candidate(10), candidate(11)
        old['sourceRun']['attempt'] = 50
        old['imageBuild']['id'] = 9999
        self.assertFalse(channel.can_promote(old, new))

    def test_same_source_new_image_and_legacy_migration(self):
        old = candidate(10)
        new = copy.deepcopy(old)
        new['imageBuild']['id'] += 1
        self.assertTrue(channel.can_promote(new, old))
        self.assertFalse(channel.can_promote(old, new))
        self.assertTrue(channel.can_promote(new, {'source': {'channel': 'develop'}}))
        self.assertTrue(channel.can_promote(new, None))


class PublicationTests(unittest.TestCase):
    def publish(self, failure=''):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'client').mkdir()
            (root / 'client' / 'Client.jar').write_bytes(b'client-mod')
            gameplay = b'exact-same-server-and-client-jar'
            m = candidate(10)
            m.update(gameplayFile='RE-volution-1.3.8.jar', gameplaySize=len(gameplay),
                     gameplaySha256=hashlib.sha256(gameplay).hexdigest())
            m['sourceRun']['url'] = 'https://github.com/Theyyk/re-volution-mod/actions/runs/10'
            sha = 'f'*40
            commands = []
            def gh(*args):
                commands.append(args)
                if args[0] == 'api' and args[-1] == '.sha':
                    return 'e'*40 if failure == 'client-moved' and sum(a[-1] == '.sha' for a in commands) > 1 else sha
                if args[0] == 'api':
                    return '[[]]'
                if args[:2] == ('release', 'download'):
                    destination = Path(args[args.index('--dir') + 1])
                    destination.mkdir(parents=True, exist_ok=True)
                    (destination / 'RE-volution.jar').write_bytes(b'wrong' if failure == 'jar' else gameplay)
                return ''
            def process(args, **kwargs):
                if args[0] == 'git':
                    return sha
                return json.dumps(dict(config=dict(Labels={
                    'org.opencontainers.image.revision': 'wrong' if failure == 'label' else m['commitSha'],
                    're.vol.mod.ref': m['branch'], 're.vol.mod.jar.sha256': m['gameplaySha256']})))
            newer = candidate(11)
            sources = [m, newer if failure == 'new-candidate' else m]
            pointers = [None, newer if failure == 'new-watermark' else None]
            with patch.object(publisher, 'latest_candidate', side_effect=sources), \
                 patch.object(publisher, 'current_channel', side_effect=pointers), \
                 patch.object(publisher, 'gh', side_effect=gh), \
                 patch.object(publisher.subprocess, 'check_output', side_effect=process), \
                 patch.dict(os.environ, GITHUB_RUN_ID='1000', GITHUB_RUN_ATTEMPT='1'):
                if failure:
                    with self.assertRaises(RuntimeError):
                        publisher.publish(root / 'client', root / 'output')
                    self.assertFalse(any(c[:2] == ('release', 'upload') for c in commands))
                else:
                    publisher.publish(root / 'client', root / 'output')
                    self.assertEqual(sum(c[:2] == ('release', 'upload') for c in commands), 1)
                    published = json.loads((root / 'output/manifest.dev.json').read_text())
                    self.assertEqual(published['source']['gameplaySha'], published['commitSha'])
                    self.assertEqual(published['files'][0]['sha256'], published['gameplaySha256'])
                    self.assertEqual(published['serverImage'], m['serverImage'])
                    prefix = published['manifestUrl'].rsplit('/', 1)[0] + '/'
                    self.assertTrue(all(f['downloadUrl'].startswith(prefix) for f in published['files']))

    def test_complete_publication(self):
        self.publish()

    def test_late_change_does_not_move_pointer(self):
        for failure in ('new-candidate', 'new-watermark', 'client-moved'):
            with self.subTest(failure=failure):
                self.publish(failure)

    def test_corrupt_payload_does_not_move_pointer(self):
        for failure in ('jar',):
            with self.subTest(failure=failure):
                self.publish(failure)

    def test_retention_never_deletes_newer_generations(self):
        active = candidate(20, branch='develop', created='2026-10-23T02:00:00Z', image_id=120)
        active['candidateId'] = 'dev-candidate-20-1-120-1'
        older = candidate(10, created='2026-10-23T01:00:00Z', image_id=110)
        older['candidateId'] = 'dev-candidate-10-1-110-1'
        newer = candidate(30, created='2026-10-23T03:00:00Z', image_id=130)
        newer['candidateId'] = 'dev-candidate-30-1-130-1'
        sha = 'f' * 40
        active_build = f'dev-build-20-1-120-1-{sha}-1000-1'
        older_build = f'dev-build-10-1-110-1-{sha}-900-1'
        newer_build = f'dev-build-30-1-130-1-{sha}-1100-1'
        transient = [
            dict(id=1, tag_name=older['candidateId'], body=json.dumps(older)),
            dict(id=2, tag_name=active['candidateId'], body=json.dumps(active)),
            dict(id=3, tag_name=newer['candidateId'], body=json.dumps(newer)),
            dict(id=4, tag_name=older_build, body=''),
            dict(id=5, tag_name=active_build, body=''),
            dict(id=6, tag_name=newer_build, body=''),
        ]
        commands = []
        with patch.object(publisher, 'releases', return_value=transient), \
             patch.object(publisher, 'gh', side_effect=lambda *args: commands.append(args) or ''):
            publisher.prune_transient_releases(active, active_build)
        deleted_ids = {
            int(command[-1].rsplit('/', 1)[1])
            for command in commands
            if command[:3] == ('api', '--method', 'DELETE') and '/releases/' in command[-1]
        }
        self.assertEqual(deleted_ids, {1, 4})


if __name__ == '__main__':
    unittest.main(verbosity=2)
