#!/usr/bin/env python3
import argparse
import base64
import hashlib
import json
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

RELEASE_API = "https://api.github.com/repos/Theyyk/re-volution-mod/releases/latest"
TAG_RE = re.compile(r"^v(\d+)\.(\d+)\.(\d+)$")


def github_get_json(url: str):
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "re-volution-manifest-rollout",
        },
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def hash_remote(url: str):
    request = urllib.request.Request(url, headers={"User-Agent": "re-volution-manifest-rollout"})
    digest = hashlib.sha256()
    size = 0
    with urllib.request.urlopen(request, timeout=60) as response:
        while True:
            chunk = response.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
            size += len(chunk)
    return digest.hexdigest(), size


def parse_version_from_path(path: str):
    match = re.fullmatch(r"mods/RE-volution-(\d+)\.(\d+)\.(\d+)\.jar", path)
    if not match:
        return None
    return tuple(int(part) for part in match.groups())


def append_output(path: Path | None, key: str, value: str):
    if path is None:
        return
    with path.open("a", encoding="utf-8") as handle:
        handle.write(f"{key}={value}\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--envelope", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--github-output")
    args = parser.parse_args()

    envelope_path = Path(args.envelope)
    output_path = Path(args.output)
    github_output = Path(args.github_output) if args.github_output else None

    envelope = json.loads(envelope_path.read_text(encoding="utf-8"))
    if envelope.get("schemaVersion") != 1:
        raise SystemExit("Unsupported signed envelope schemaVersion")
    if envelope.get("algorithm") != "ecdsa-p256-sha256":
        raise SystemExit("Unsupported signed envelope algorithm")
    key_id = envelope.get("keyId")
    if not key_id:
        raise SystemExit("Signed envelope keyId is missing")

    try:
        payload_bytes = base64.b64decode(envelope["payload"], validate=True)
        payload = json.loads(payload_bytes.decode("utf-8"))
    except Exception as exc:
        raise SystemExit(f"Cannot decode current manifest payload: {exc}") from exc

    files = payload.get("files")
    if not isinstance(files, list):
        raise SystemExit("Manifest files array is missing")

    gameplay_entries = [entry for entry in files if isinstance(entry, dict) and entry.get("role") == "gameplay-mod"]
    if len(gameplay_entries) != 1:
        raise SystemExit(f"Expected exactly one gameplay-mod entry, found {len(gameplay_entries)}")
    gameplay = gameplay_entries[0]

    release = github_get_json(RELEASE_API)
    if release.get("draft") or release.get("prerelease"):
        raise SystemExit("GitHub latest release unexpectedly points to a draft/prerelease")

    tag = release.get("tag_name", "")
    match = TAG_RE.fullmatch(tag)
    if not match:
        raise SystemExit(f"Latest release tag is not a stable vX.Y.Z tag: {tag!r}")
    version_tuple = tuple(int(part) for part in match.groups())
    version = ".".join(match.groups())

    current_version = parse_version_from_path(str(gameplay.get("path", "")))
    if current_version and version_tuple < current_version:
        raise SystemExit(
            f"Refusing gameplay-mod downgrade from {'.'.join(map(str, current_version))} to {version}"
        )

    asset_name = f"RE-volution-{version}.jar"
    assets = [asset for asset in release.get("assets", []) if asset.get("name") == asset_name]
    if len(assets) != 1:
        raise SystemExit(f"Expected exactly one release asset named {asset_name}, found {len(assets)}")
    asset = assets[0]
    download_url = asset.get("browser_download_url")
    if not download_url or not download_url.startswith("https://github.com/Theyyk/re-volution-mod/releases/download/"):
        raise SystemExit("Release asset download URL is missing or unexpected")

    sha256, actual_size = hash_remote(download_url)
    api_size = asset.get("size")
    if api_size != actual_size:
        raise SystemExit(f"Release asset size mismatch: API={api_size}, downloaded={actual_size}")

    next_entry = {
        **gameplay,
        "path": f"mods/{asset_name}",
        "downloadUrl": download_url,
        "sha256": sha256,
        "size": actual_size,
        "role": "gameplay-mod",
    }

    changed = any(gameplay.get(key) != next_entry.get(key) for key in ("path", "downloadUrl", "sha256", "size", "role"))
    gameplay.clear()
    gameplay.update(next_entry)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    append_output(github_output, "changed", "true" if changed else "false")
    append_output(github_output, "version", version)
    append_output(github_output, "key_id", key_id)
    append_output(github_output, "sha256", sha256)
    append_output(github_output, "size", str(actual_size))
    append_output(github_output, "download_url", download_url)

    print(json.dumps({"changed": changed, "version": version, "keyId": key_id, "sha256": sha256, "size": actual_size, "downloadUrl": download_url}, indent=2))


if __name__ == "__main__":
    try:
        main()
    except urllib.error.HTTPError as exc:
        print(f"HTTP error: {exc.code} {exc.reason}", file=sys.stderr)
        raise
