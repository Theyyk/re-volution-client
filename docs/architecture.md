# RE:volution Client architecture

## Product boundary

`re-volution-client` is one private product with one version. It contains two runtime components:

1. `launcher/` — Windows launcher/updater and installation orchestration.
2. `mod/` — client-only Minecraft Forge 1.12.2 mod that owns the RE:volution client menu and connection UX.

The public gameplay repository `Theyyk/re-volution-mod` is outside this boundary and must not be changed for client-distribution work.

## Launcher responsibilities

- retrieve and validate a versioned manifest;
- keep downloads within the configured installation root;
- verify file size and SHA-256 before replacing installed files;
- prepare the required Java/Minecraft/Forge/client files;
- start Minecraft with the prepared profile;
- never access MongoDB or the private Admin API;
- never treat a server address as a secret.

The launcher must not embed gameplay logic.

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
