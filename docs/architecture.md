# RE:volution Client architecture

## Product boundary

`re-volution-client` is one private product with one version. It contains two runtime components:

1. `launcher/` — Windows launcher/updater and installation orchestration.
2. `mod/` — client-only Minecraft Forge 1.12.2 mod that owns the RE:volution client menu and connection UX.

The public gameplay repository `Theyyk/re-volution-mod` is outside this boundary and must not be changed for client-distribution work.

## Distribution goal

The supported player flow for v1.0.0 is intentionally simple:

1. download `REvolutionClient.exe`;
2. run it;
3. let the launcher prepare or repair the complete client installation;
4. press Play.

The player must not need to install Java, Forge, a Minecraft profile, mods, or client configuration manually.

## Launcher responsibilities

- be distributed as a Windows `.exe`;
- retrieve and validate a versioned manifest;
- keep downloads within the configured installation root;
- verify file size and SHA-256 before replacing installed files;
- prepare the required Java/Minecraft/Forge/client files;
- detect a usable 64-bit Java 8 runtime;
- if a compatible Java 8 runtime is unavailable or invalid, download the pinned managed Java 8 runtime declared by the distribution configuration;
- verify the managed Java runtime before use;
- keep the managed runtime private to RE:volution Client and do not modify the system `PATH`, `JAVA_HOME`, or global Java installation;
- launch Minecraft with the selected verified Java 8 executable;
- start Minecraft with the prepared profile;
- never access MongoDB or the private Admin API;
- never treat a server address as a secret.

The launcher must not embed gameplay logic.

## Managed Java policy

RE:volution Client owns the Java runtime used to launch Minecraft when a suitable existing Java 8 installation cannot be trusted or used.

The managed runtime is version-pinned, downloaded only from the configured trusted distribution source, integrity-checked before activation, and stored under the RE:volution Client installation root. A partially downloaded or failed runtime must never become the active runtime.

System Java discovery is an optimization only. Correct launch behavior must not depend on the player having Java installed globally.

## Client mod responsibilities

- run only on the Minecraft client;
- replace the vanilla main menu with the RE:volution client entry screen;
- expose one primary Play action;
- read the target Minecraft server from client configuration rather than hard-coding it into the JAR;
- provide connection/error UX only;
- never contain server gameplay logic or persistence access.

## Manifest trust model

The manifest is data, not executable instructions. Paths are relative and are rejected if they escape the installation root. Every managed file has an exact SHA-256 and size. A later release step will add manifest authenticity/signing before v1.0.0 distribution is accepted.

## Versioning

Launcher and client mod are released together as **RE:volution Client**. The first production release target is `v1.0.0`.
