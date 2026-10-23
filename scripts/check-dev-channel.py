import os
import subprocess
from dev_channel import latest_candidate, current_channel, can_promote

candidate = latest_candidate()
old = current_channel()
if not can_promote(candidate, old):
    raise RuntimeError('No candidate at or above the DEV channel watermark')
sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
changed = (old is None or old.get('candidateId') != candidate['candidateId']
           or old.get('source', {}).get('clientSha') != sha or os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch')
with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
    output.write(f"value={str(changed).lower()}\nclient_sha={sha}\n")
print(f"DEV candidate: {candidate['branch']} @ {candidate['commitSha']}; changed={changed}")
