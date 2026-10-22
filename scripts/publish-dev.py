"""Publish only the unsigned DEV channel. Never reads production signing material."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

REPO = "Theyyk/re-volution-client"
MOD_REPO = "Theyyk/re-volution-mod"


def gh(*args):
    return subprocess.check_output(["gh", *args], text=True).strip()


def api(path):
    return json.loads(gh("api", path))


def one_jar(directory):
    jars = [p for p in Path(directory).rglob("*.jar") if not p.name.endswith(("-sources.jar", "-dev.jar"))]
    if len(jars) != 1:
        raise RuntimeError(f"Expected exactly one payload JAR in {directory}: {jars}")
    return jars[0]


def managed(path, tag, role):
    return {"path": f"mods/{path.name}",
            "downloadUrl": f"https://github.com/{REPO}/releases/download/{tag}/{path.name}",
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "size": path.stat().st_size, "role": role}


def publish(client_directory, output):
    runs = api(f"repos/{MOD_REPO}/actions/workflows/release.yml/runs?branch=develop&event=push&status=success&per_page=1")["workflow_runs"]
    if not runs:
        raise RuntimeError("No successful develop gameplay build is available")
    run = runs[0]
    gameplay_sha = run["head_sha"]
    client_sha = gh("api", f"repos/{REPO}/commits/develop", "--jq", ".sha")
    local_sha = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    if local_sha != client_sha:
        raise RuntimeError("Checkout is no longer the current client develop HEAD; retry the workflow")
    tag = f"dev-build-{gameplay_sha[:12]}-{client_sha[:12]}"
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        gh("run", "download", str(run["id"]), "--repo", MOD_REPO,
           "--name", "RE-volution-develop", "--dir", str(root / "gameplay"))
        gameplay = one_jar(root / "gameplay")
        client = one_jar(client_directory)
        releases = json.loads(gh("api", "--paginate", "--slurp", f"repos/{REPO}/releases?per_page=100"))
        releases = [release for page in releases for release in page]
        existing = next((r for r in releases if r["tag_name"] == tag), None)
        if existing is None:
            gh("release", "create", tag, str(gameplay), str(client), "--repo", REPO,
               "--target", client_sha, "--prerelease", "--latest=false", "--title", tag,
               "--notes", f"DEV only. Gameplay {gameplay_sha}; client {client_sha}; build {run['html_url']}")
        # Calculate metadata from the published immutable payload, including on reruns.
        payload = root / "published"
        gh("release", "download", tag, "--repo", REPO, "--pattern", "*.jar", "--dir", str(payload))
        gameplay = payload / gameplay.name
        client = payload / client.name
        manifest = {
            "productVersion": f"dev-{gameplay_sha[:12]}-{client_sha[:12]}",
            "developmentOnly": True,
            "auth": {"baseUrl": "http://127.0.0.1:25567/"},
            "java": {"majorVersion": 8, "provider": "adoptium"},
            "minecraft": {"version": "1.12.2", "forgeVersion": "14.23.5.2859", "memoryMb": 4096},
            "server": {"host": "127.0.0.1", "port": 25568},
            "files": [managed(gameplay, tag, "gameplay-mod"), managed(client, tag, "client-mod")],
            "source": {"gameplaySha": gameplay_sha, "clientSha": client_sha,
                       "gameplayBuild": run["html_url"], "channel": "develop"}}
        manifest_path = output / "manifest.dev.json"
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        # Do not move the channel backwards if a newer successful gameplay build appeared.
        latest = api(f"repos/{MOD_REPO}/actions/workflows/release.yml/runs?branch=develop&event=push&status=success&per_page=1")["workflow_runs"][0]
        if latest["head_sha"] != gameplay_sha or gh("api", f"repos/{REPO}/commits/develop", "--jq", ".sha") != client_sha:
            raise RuntimeError("A newer DEV build appeared; channel was not changed. Retry.")
        if not any(r["tag_name"] == "dev-latest" for r in releases):
            gh("release", "create", "dev-latest", "--repo", REPO, "--target", client_sha,
               "--prerelease", "--latest=false", "--title", "DEV channel (testing only)",
               "--notes", "Unsigned local Docker DEV channel. Stable production distribution is unchanged.")
        gh("release", "upload", "dev-latest", str(manifest_path), "--clobber", "--repo", REPO)
        print(json.dumps(manifest["source"], indent=2))
        if "GITHUB_STEP_SUMMARY" in os.environ:
            with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
                summary.write(f"DEV manifest published: gameplay `{gameplay_sha}`, client `{client_sha}`.\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--client-directory", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    publish(args.client_directory, args.output)
