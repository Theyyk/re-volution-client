# Separate PROD and DEV launchers

PROD remains at `E:\MyProject\Client\Prod\REvolutionClient.exe`. This migration
does not replace that EXE, change its settings, or publish a production manifest.
Its default URL remains:

`https://raw.githubusercontent.com/Theyyk/re-volution-resource-pack/main/client/manifest.production.signed.json`

DEV is built with `-p:LauncherChannel=Dev` and installed at
`E:\MyProject\Client\Dev\REvolutionClient_dev.exe`. No BAT wrapper or global
environment variable is needed. Its default URL is:

`http://127.0.0.1:25567/api/v1/dev/channel` (the exact manifest accepted by the local DEV server)

## Isolation

| Item | PROD | DEV |
| --- | --- | --- |
| Settings | `%LOCALAPPDATA%\REvolutionClient\launcher-settings.json` | `%LOCALAPPDATA%\REvolutionClientDev\launcher-settings.json` |
| Installation | Existing user selection, unchanged | `install-dev` next to DEV EXE; fixed in UI and validated in code |
| Runtime/game/downloads | Existing installation | All under `Dev\install-dev` |
| Overrides | `REVOLUTION_CLIENT_MANIFEST_URL/FILE` | `REVOLUTION_CLIENT_DEV_MANIFEST_URL/FILE` |
| Server | Existing stable manifest endpoint | `127.0.0.1:25568` (local Docker) |
| Auth | Existing HTTPS endpoint | `http://127.0.0.1:25567/` (local Docker) |
| Manifest | Trusted signed production envelope | HTTPS DEV channel, `developmentOnly=true`, exact SHA-256/size |

DEV rejects production manifests, missing gameplay/client entries, alternative
installation roots, and filesystem links inside its installation. No bundled
gameplay fallback is allowed for DEV. If the channel is unavailable, the launcher
shows an error instead of silently installing a stable or stale bundled mod.
Both profiles keep the existing SHA-256, size and path traversal checks.

The existing PROD EXE can still honor its old user-selected installation directory.
Moving an EXE does not change that setting. This task deliberately preserves it.

## Unified DEV publication

The single mutable pointer is the **whole** JSON document at
`https://github.com/Theyyk/re-volution-client/releases/download/dev-latest/manifest.dev.json`.
It is both channel metadata and a backwards compatible client manifest; there is
no separately updated server pointer that can disagree with it.

1. Mod `release.yml` runs on all branch pushes. The `workflow_run` dispatcher on
   mod default `main` dispatches only after the entire official `Build and Release`
   workflow succeeds. `main`, `master`, `stable`, `release` and their slash namespaces
   are excluded from DEV (case insensitive validation in the Docker publisher).
   New feature/fix/hotfix/arbitrary branches must inherit the updated workflow
   from `develop`; old branches need to merge/rebase the workflow update to gain
   push triggers they did not previously have. Tags never qualify as DEV sources.
2. Docker validates the source run through GitHub API, downloads its exact CI JAR,
   and embeds those bytes in an image tagged
   `dev-<full gameplay SHA>-<Docker run ID>-<attempt>`. No DEV code is rebuilt and
   no floating `:dev` tag moves. Revision, branch and JAR SHA-256 labels identify
   the image. After the complete image workflow succeeds, trusted Docker
   `publish-dev-candidate.yml` validates that success through its own GitHub API
   token and publishes `dev-candidate-<source run>-<attempt>-<image run>-<attempt>`
   in the public client repository. It uses the existing Docker
   `RE_VOLUTION_DOCKER` cross-repository secret. This completion receipt includes
   the exact JAR/digest and `imageBuild.conclusion=success`; private infrastructure
   source and credentials are never exported. The completion publisher checks
   the exact private GHCR digest and revision/branch/JAR hash labels with its
   own GITHUB_TOKEN; client CI never needs private GHCR credentials.
3. Client `dev-client.yml` checks on `develop` pushes, manual dispatch or every
   15 minutes. It reads public Docker completion receipts, verifies the source CI
   through GitHub API and requires the successful image completion receipt. The
   client token needs no access to the private Docker repository. It selects the greatest marker, builds the
   client mod at one pinned client `develop` SHA, checks the downloaded gameplay
   hash against the verified Docker receipt, and publishes JARs plus manifest to an immutable
   `dev-build-*` prerelease. Manual dispatch also rebuilds the DEV EXE.
4. Only this workflow replaces `dev-latest/manifest.dev.json`, after all assets
   exist. All DEV releases use `--prerelease --latest=false`. No production key,
   production manifest, stable release or signed distribution is published.

The ordering tuple is lexicographic:

```text
(sourceRun.createdAt, sourceRun.id, sourceRun.attempt, imageBuild.id, imageBuild.attempt)
```

Source creation time/run ID takes priority over completion time and Docker run
ID. Thus source run 11 from `fix/fast` beats source run 10 from `feature/slow`
even when run 10's Docker image finishes last. A rerun of run 10 cannot overtake
11. Attempts and image build IDs break ties only within the same source run.
This intentionally defines "newest" by the successful CI run's creation order,
not semantic version, branch ancestry or a mutable commit clock.

The unchanged `dev-client-channel` concurrency group is the sole writer lock.
Ordering in that queue is not assumed: publication rechecks the newest complete
candidate, current client HEAD and persisted manifest watermark immediately
before upload. An older candidate is rejected even if its workflow runs last.
An interrupted upload can temporarily return 404; consumers fail closed and
retry, without mixing two pointers. Do not run the publisher outside this workflow
or add another workflow that writes `dev-latest`.

### Metadata format

The manifest includes these fields in addition to normal auth/Java/Minecraft/files:

```json
{
  "schemaVersion": 1,
  "developmentOnly": true,
  "channel": "dev",
  "branch": "fix/example",
  "commitSha": "<full gameplay commit SHA>",
  "gameplayVersion": "1.3.8",
  "gameplaySha256": "<SHA-256 of the same CI JAR embedded in the server>",
  "gameplaySize": 123,
  "serverImage": "ghcr.io/theyyk/re-volution-server@sha256:<exact digest>",
  "serverImageTag": "ghcr.io/theyyk/re-volution-server:dev-<SHA>-<image run>-<attempt>",
  "candidateId": "dev-candidate-<source run>-<attempt>-<image run>-<attempt>",
  "sourceRun": {"id": 11, "createdAt": "2026-10-23T01:00:00Z", "attempt": 1, "url": "<CI URL>", "artifactId": 123},
  "imageBuild": {"id": 22, "attempt": 1, "conclusion": "success", "url": "<Docker CI URL>"},
  "manifestUrl": "https://github.com/Theyyk/re-volution-client/releases/download/dev-build-<candidate>-<client SHA>-<publisher run>-<attempt>/manifest.dev.json",
  "source": {"gameplaySha": "<same gameplay SHA>", "clientSha": "<client source SHA>", "channel": "dev", "branch": "fix/example"},
  "files": [
    {"role": "gameplay-mod", "path": "mods/RE-volution-1.3.8.jar", "downloadUrl": "<same immutable dev-build release>/RE-volution-1.3.8.jar", "sha256": "<same gameplay hash>", "size": 123},
    {"role": "client-mod", "path": "mods/RE-volution-Client-Mod-1.0.0.jar", "downloadUrl": "<same immutable dev-build release>/RE-volution-Client-Mod-1.0.0.jar", "sha256": "<client hash>", "size": 456}
  ]
}
```

### Keeping a running server and launcher together

Run `dev.ps1 restart -CheckOnly` to inspect branch/version/SHA/digest without any
pull, state write, container operation or world/database change. `update`, `start`
and `restart` read the pointer once, validate its fields, inspect the exact registry
digest, pull it, validate its revision/JAR labels and hash the embedded JAR before
changing containers. Supporting image downloads and the Auth build also finish
before replacement. Startup must pass health checks and the running container's
exact image/revision must match before acceptance.

The accepted manifest is saved atomically in Docker's ignored
`.dev-channel/active.json`; the prior manifest is kept as `previous-<SHAs>.json`.
Auth mounts this directory read-only and serves the accepted snapshot at
`http://127.0.0.1:25567/api/v1/dev/channel` with `Cache-Control: no-store`.
Missing state, a stopped DEV, or a failed/in-progress transition returns 503.
The route is absent unless `REVOLUTION_DEV_CHANNEL_DIRECTORY` is explicitly set.

The new DEV EXE follows this local snapshot, so a remote channel publication
**during a test** does not change gameplay under a still-running older server.
Pressing Play loads the accepted manifest, verifies the shared SHA/digest/hash,
installs its exact gameplay JAR with size/SHA-256 checks and removes stale JARs.
It checks the accepted channel again after preparation and refuses to launch if
the server changed meanwhile. Use the scripts for server transitions; raw Docker
commands bypass acceptance/blocking and are not a supported DEV update path.

Download the new `REvolutionClient_dev.exe` from the `dev-latest` prerelease once.
Older EXEs still understand the remote manifest's file list but cannot pin to the
local running-server snapshot. Do not retain the old EXE for synchronized tests.
The EXE's gameplay JAR updates automatically; replacing the EXE itself remains a
deliberate download/deployment operation, not an in-place executable self-updater.

### Rollback and bootstrap

Local rollback pins both sides without moving the global watermark backwards:

```powershell
.\dev.ps1 restart -ManifestPath .\.dev-channel\previous-<gameplay SHA>-<client SHA>.json -CheckOnly
.\dev.ps1 restart -ManifestPath .\.dev-channel\previous-<gameplay SHA>-<client SHA>.json
```

Or download `manifest.dev.json` from a retained immutable `dev-build-*` release
and pass its local path. Hashes/digest/health checks still apply. This changes
DEV code selection only; it does not restore worlds/databases or delete volumes.
Take separate data backups when testing gameplay with incompatible data changes.
`dev.ps1 restart` without `-ManifestPath` returns to the current global channel.
If activation fails, keep the `updating` marker and rerun/recover through the script;
do not manually remove it while the container is mismatched.

For bootstrap or a lost dispatch, manually run Docker `dev-server.yml` with
`publish_dev=true` and an eligible `mod_ref`, or an exact successful `source_run_id`.
Then run the client DEV publisher (or wait for its schedule). The existing `RE_VOLUTION_DOCKER` secrets in mod and Docker cover the
dispatch and public completion receipt respectively. The Docker token must include
client repository Contents: read and write; add that selected repository to the
earlier Docker-only token. A dedicated limited DEV_CHANNEL_PUBLISH_TOKEN in Docker
is also supported. No broad desktop login token is required. No failed/PR/fork run is promoted. Retain immutable releases and image
tags needed for rollback; source Actions artifacts must be unexpired for bootstrap.

## Build and checks

```powershell
dotnet run --project tools/LauncherCheck -c Release -p:LauncherChannel=Dev
dotnet run --project tools/LauncherCheck -c Release -p:LauncherChannel=Prod
dotnet publish launcher/REvolutionClient.Launcher.csproj -c Release -p:LauncherChannel=Dev -o launcher/publish-dev
```

The profiles use distinct build/intermediate folders to prevent cross-profile
assembly/dependency reuse. The checker verifies installation isolation and real
updater rejection of a bad hash/path escape plus removal of stale DEV JARs.
`--verify-production` verifies the current remote signed production manifest
without changing it. `--prepare` on the DEV checker reads local Auth acceptance and prepares a full runtime next
to that diagnostic binary, without logging in, creating accounts or starting a game.

The final acceptance step uses the user's existing account: open the DEV EXE,
log in, press Play and connect. No credentials are stored or supplied by these tools.
