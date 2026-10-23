"""Component detection using the complete local Git diff; uncertainty builds everything."""
import fnmatch
import json
import os
from pathlib import Path
import subprocess


def select(paths, rules, force=False):
    # New/unclassified inputs must not silently skip a future project/build system.
    def documentation(path):
        return path.startswith('docs/') or path.endswith(('.md', '.rst')) or path == 'LICENSE'
    unknown = any(not documentation(path) and
                  not any(fnmatch.fnmatchcase(path, pattern)
                          for patterns in rules.values() for pattern in patterns)
                  for path in paths)
    force = force or unknown
    return {name: force or any(fnmatch.fnmatchcase(path, pattern)
                              for path in paths for pattern in patterns)
            for name, patterns in rules.items()}


def changed_paths(event_name, event, ref):
    if event_name not in ('push', 'pull_request') or ref.startswith('refs/tags/'):
        return None
    if event_name == 'push':
        before, after = event.get('before', ''), event.get('after', '')
        if not before or set(before) == {'0'} or not after or set(after) == {'0'}:
            return None
        revisions = [before, after]
    else:
        pr = event['pull_request']
        base, head = pr['base']['sha'], pr['head']['sha']
        merge_base = subprocess.check_output(['git', 'merge-base', base, head], text=True).strip()
        revisions = [merge_base, head]
    # --no-renames includes both deleted and added paths; -z preserves unusual filenames.
    raw = subprocess.check_output(['git', 'diff', '--name-only', '--no-renames', '-z', *revisions])
    return [p for p in raw.decode('utf-8', errors='surrogateescape').split('\0') if p]


def main():
    rules = json.loads(Path('.github/ci/components.json').read_text())
    try:
        event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
        paths = changed_paths(os.environ['GITHUB_EVENT_NAME'], event, os.environ['GITHUB_REF'])
    except (KeyError, OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f'Diff unavailable ({type(error).__name__}); run every component')
        paths = None
    result = select(paths or [], rules, force=paths is None)
    with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
        for name, enabled in result.items():
            output.write(f'{name}={str(enabled).lower()}\n')
    print(json.dumps(result, sort_keys=True))


if __name__ == '__main__':
    main()
