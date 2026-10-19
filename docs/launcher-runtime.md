# Launcher runtime pipeline

The production target is a single `REvolutionClient.exe` for Windows x64. A player should not have to install Java, Minecraft, Forge, the gameplay mod, the client mod, or connection configuration manually.

## Runtime flow

1. Load and validate the client manifest.
2. Reuse Java 8 x64 when a valid installation is available; otherwise resolve an Eclipse Temurin/Adoptium Java 8 JRE package, verify its size and SHA-256 from provider metadata, and install it under the launcher-owned application directory without changing `PATH` or `JAVA_HOME`.
3. Prepare Minecraft 1.12.2 and exactly Forge 14.23.5.2859 in the launcher-owned game directory.
4. Install the bundled `RE:volution Mod v1.3.2` and `RE:volution Client Mod v1.0.0` payloads into `mods/`.
5. Synchronize any manifest-managed files using exact size and SHA-256 verification and atomic replacement.
6. Generate the client-only Forge configuration containing the server host and port.
7. Authenticate with the player's Microsoft/Minecraft account using the standard Microsoft flow. No RE:volution account database is introduced.
8. Launch Minecraft with the managed Java runtime. The client mod owns the custom main menu and the `Играть` connection action; the launcher does not bypass that screen with direct-connect arguments.

## Development bootstrap

`manifest/bootstrap.dev.json` is deliberately marked `developmentOnly` and points to `127.0.0.1:25566` for local acceptance testing. It is embedded into feature builds so a CI-produced EXE has a deterministic bootstrap without an external manifest host.

A production v1.0.0 release must replace this development bootstrap with the production update/manifest endpoint and add manifest authenticity verification before the tag is accepted.

## Product boundary

This pipeline changes only `Theyyk/re-volution-client`. `Theyyk/re-volution-mod` remains at v1.3.2 and is consumed as the immutable released JAR with SHA-256 `46624637e8d9c73947cef69ed9832a2f35f66abf81a647503254cd5a71a46d02`.
