# Launcher runtime pipeline

The production target is a single `REvolutionClient.exe` for Windows x64. A player should not have to install Java, Minecraft, Forge, the gameplay mod, the client mod, or connection configuration manually.

## Runtime flow

1. Download and validate the client manifest from the stable HTTPS bootstrap endpoint. `REVOLUTION_CLIENT_MANIFEST_URL` may override it for controlled tests.
2. Reuse Java 8 x64 when a valid installation is available; otherwise resolve an Eclipse Temurin/Adoptium Java 8 JRE package, verify its size and SHA-256 from provider metadata, and install it under the launcher-owned application directory without changing `PATH` or `JAVA_HOME`.
3. Prepare Minecraft 1.12.2 and exactly Forge 14.23.5.2859 in the launcher-owned game directory.
4. Install only those bundled RE:volution mod categories that are not managed by the manifest. A manifest-managed gameplay mod is never overwritten by the older gameplay JAR embedded in the EXE.
5. Synchronize manifest-managed files using exact size and SHA-256 verification and atomic replacement. Managed gameplay/client mod roles also remove stale RE:volution JAR versions from `mods/`.
6. Generate the client-only Forge configuration containing the server host and port.
7. Authenticate with the RE:volution username/password Auth API described by the manifest.
8. Launch Minecraft with the managed Java runtime. The client mod owns the custom main menu and the `Играть` connection action; the launcher does not bypass that screen with direct-connect arguments.

## Stable bootstrap endpoint

Feature builds now use this HTTPS endpoint by default:

`https://raw.githubusercontent.com/Theyyk/re-volution-resource-pack/main/client/manifest.json`

The repository is public, so the launcher can fetch the manifest without GitHub credentials. The current manifest is deliberately `developmentOnly`, keeps Auth/server on loopback for local acceptance, and manages the released gameplay mod v1.3.2 from its public GitHub Release asset.

This allows a previously built EXE to consume a newer manifest and replace an outdated gameplay JAR without rebuilding the launcher. The client-only mod remains available as bundled fallback until it also has a stable public distribution URL; the manifest format already supports the `client-mod` role.

## Acceptance scenario

To verify the update path with the same EXE:

1. Put an older RE:volution gameplay JAR in the launcher's `game/mods` directory, for example v1.3.1.
2. Keep the public manifest pointing at v1.3.2 with its exact size and SHA-256.
3. Start the existing `REvolutionClient.exe` without rebuilding it.
4. The launcher must fetch the manifest, download v1.3.2, verify size and SHA-256, atomically install it, and remove the stale owned gameplay JAR.
5. Start the same EXE again. Because the managed gameplay file already matches the manifest, it must not restore the older bundled gameplay JAR or redownload v1.3.2.

## Production gate

Before the public v1.0.0 tag is accepted, the development loopback values must be replaced with production Auth/server values and manifest authenticity verification/signing must be implemented. HTTPS plus per-file SHA-256 protects file integrity against accidental corruption but does not by itself authenticate a mutable manifest.

## Product boundary

Gameplay code remains in `Theyyk/re-volution-mod`; this launcher pipeline only consumes published gameplay release artifacts. The public resource-pack repository currently provides the small unauthenticated bootstrap JSON because the client repository itself is private.
