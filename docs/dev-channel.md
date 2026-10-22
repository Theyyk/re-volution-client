# Separate PROD and DEV launchers

PROD remains at `E:\MyProject\Client\Prod\REvolutionClient.exe`. This migration
does not replace that EXE, change its settings, or publish a production manifest.
Its default URL remains:

`https://raw.githubusercontent.com/Theyyk/re-volution-resource-pack/main/client/manifest.production.signed.json`

DEV is built with `-p:LauncherChannel=Dev` and installed at
`E:\MyProject\Client\Dev\REvolutionClient_dev.exe`. No BAT wrapper or global
environment variable is needed. Its default URL is:

`https://github.com/Theyyk/re-volution-client/releases/download/dev-latest/manifest.dev.json`

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

## Updating DEV

`.github/workflows/dev-client.yml` is independent from `build.yml` and the stable
resource-pack distribution pipeline. It runs on client `develop` pushes, manual
dispatch, and a 15-minute schedule (GitHub may delay scheduled jobs).
Unchanged scheduled runs skip building and publishing.

After Windows isolation checks pass, the workflow builds the client mod from
current client `develop`, downloads the latest **successful** gameplay `develop`
`Build and Release` artifact, and publishes both JARs to a SHA-specific prerelease.
The DEV manifest points at those immutable assets. Only after payload publication
does it replace `dev-latest/manifest.dev.json`. All releases are prereleases and
are explicitly excluded from GitHub's latest stable release selection.

`scripts/publish-dev.py` records both source SHAs and rejects obsolete checkout
or gameplay selections before updating the channel. Only the repository's
`GITHUB_TOKEN` is used; no production signing key or new cross-repository secret
is required. A failed or incomplete gameplay run is never selected.

The DEV Docker image is built independently from the same gameplay source.
Before manual testing, compare `manifest.source.gameplaySha` with the running
container's `org.opencontainers.image.revision` label. Pull/recreate the DEV
server using its existing update procedure if they differ; publishing a manifest
does not itself restart a local server.

```text
gameplay develop -> successful build artifact -> DEV prerelease + manifest
client develop  -> verified DEV EXE + client mod ------^
Docker DEV image with matching gameplay SHA
                 -> DEV launcher -> manual test
                 -> controlled stable tag/release
                 -> existing signing pipeline -> PROD launcher
```

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
without changing it. `--prepare` on the DEV checker prepares a full runtime next
to that diagnostic binary, without logging in, creating accounts or starting a game.

The final acceptance step uses the user's existing account: open the DEV EXE,
log in, press Play and connect. No credentials are stored or supplied by these tools.
