import os
import subprocess
import time
from dev_channel import latest_candidate, current_channel, can_promote


def evaluate():
    candidate = latest_candidate()
    old = current_channel()
    if not can_promote(candidate, old):
        raise RuntimeError('No candidate at or above the DEV channel watermark')
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    changed = (old is None or old.get('candidateId') != candidate['candidateId']
               or old.get('source', {}).get('clientSha') != sha
               or os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch')
    return candidate, sha, changed


candidate, sha, changed = evaluate()

# repository_dispatch is emitted immediately after Docker publishes an immutable candidate.
# GitHub Releases can still be eventually consistent for a few seconds, so an unchanged
# view here may be stale. Retry briefly before treating the dispatch as a duplicate.
if os.environ.get('GITHUB_EVENT_NAME') == 'repository_dispatch' and not changed:
    for attempt in range(1, 9):
        time.sleep(2)
        candidate, sha, changed = evaluate()
        print(f"DEV candidate visibility retry {attempt}/8: {candidate['branch']} @ {candidate['commitSha']}; changed={changed}")
        if changed:
            break

with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
    output.write(f"value={str(changed).lower()}\nclient_sha={sha}\ncandidate_id={candidate['candidateId']}\n")
print(f"DEV candidate: {candidate['branch']} @ {candidate['commitSha']}; changed={changed}")
