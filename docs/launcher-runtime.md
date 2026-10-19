# Launcher runtime pipeline

The production target is a single `REvolutionClient.exe` for Windows x64. A player should not have to install Java, Minecraft, Forge, the gameplay mod, the client mod, or connection configuration manually.

## Runtime flow

1. Download and validate the client manifest from the stable HTTPS bootstrap endpoint. `REVOLUTION_CLIENT_MANIFEST_URL` may override it for controlled HTTPS tests; `REVOLUTION_CLIENT_MANIFEST_FILE` is an explicit local-development override.
2. Reuse Java 8 x64 when a valid installation is available; otherwise resolve an Eclipse Temurin/Adoptium Java 8 JRE package, verify its size and SHA-256 from provider metadata, and install it under the launcher-owned application directory without changing `PATH` or `JAVA_HOME`.
3. Prepare Minecraft 1.12.2 and exactly Forge 14.23.5.2859 in the launcher-owned game directory.
4. Install only those bundled RE:volution mod categories that are not managed by the manifest. A manifest-managed gameplay mod is never overwritten by the older gameplay JAR embedded in the EXE.
5. Synchronize manifest-managed files using exact size and SHA-256 verification and atomic replacement. Managed gameplay/client mod roles also remove stale RE:volution JAR versions from `mods/`.
6. Generate the client-only Forge configuration containing the server host and port.
7. Authenticate with the RE:volution username/password Auth API described by the manifest.
8. Launch Minecraft with the managed Java runtime. The client mod owns the custom main menu and the `Играть` connection action; the launcher does not bypass that screen with direct-connect arguments.

## Stable bootstrap endpoint

Feature builds currently use this HTTPS endpoint by default:

`https://raw.githubusercontent.com/Theyyk/re-volution-resource-pack/main/client/manifest.json`

The repository is public, so the launcher can fetch the manifest without GitHub credentials. The current manifest is deliberately `developmentOnly`, keeps Auth/server on loopback for local acceptance, and manages the released gameplay mod v1.3.2 from its public GitHub Release asset.

This allows a previously built EXE to consume a newer manifest and replace an outdated gameplay JAR without rebuilding the launcher. The client-only mod remains available as bundled fallback until it also has a stable public distribution URL; the manifest format already supports the `client-mod` role.

## Signed production manifests

A production remote manifest (`developmentOnly=false`) must be wrapped in a signed envelope:

```json
{
  "schemaVersion": 1,
  "algorithm": "ecdsa-p256-sha256",
  "keyId": "prod-2026-01",
  "payload": "<base64 of exact manifest.json bytes>",
  "signature": "<base64 64-byte P1363 ECDSA signature>"
}
```

The launcher verifies the signature before parsing or using the payload. Trusted public keys are embedded from `manifest/trusted-manifest-keys.json`. Private keys must remain outside the repository.

The repository includes `tools/ManifestSigner` for key generation, signing, and offline verification. Typical operator flow:

```powershell
dotnet run --project tools/ManifestSigner/ManifestSigner.csproj -- generate `
  --private E:\Secure\revolution-manifest.private.pem `
  --public E:\Secure\revolution-manifest.public.pem

dotnet run --project tools/ManifestSigner/ManifestSigner.csproj -- sign `
  --manifest manifest\production.json `
  --private E:\Secure\revolution-manifest.private.pem `
  --key-id prod-2026-01 `
  --output manifest\production.signed.json

dotnet run --project tools/ManifestSigner/ManifestSigner.csproj -- verify `
  --envelope manifest\production.signed.json `
  --public E:\Secure\revolution-manifest.public.pem `
  --key-id prod-2026-01
```

After generating the real production key, only its public PEM is added to `manifest/trusted-manifest-keys.json`, then the launcher is rebuilt once. Future gameplay/client-mod updates require only new published artifacts plus a newly signed manifest; they do not require rebuilding the launcher while the trusted key remains valid.

## Acceptance scenario

To verify the update path with the same EXE:

1. Put an older RE:volution gameplay JAR in the launcher's `game/mods` directory, for example v1.3.1.
2. Keep the active manifest pointing at v1.3.2 with its exact size and SHA-256.
3. Start the existing `REvolutionClient.exe` without rebuilding it.
4. The launcher must fetch the manifest, download v1.3.2, verify size and SHA-256, atomically install it, and remove the stale owned gameplay JAR.
5. Start the same EXE again. Because the managed gameplay file already matches the manifest, it must not restore the older bundled gameplay JAR or redownload v1.3.2.

For feature/runtime testing, use `REVOLUTION_CLIENT_MANIFEST_FILE` with `developmentOnly=true`; local `file://` managed artifacts are permitted only through that explicit local-development path.

## Production gate

Code-level production manifest enforcement is now present: unsigned production manifests are rejected, signatures are verified against embedded trusted keys, production Auth must use HTTPS, the server must not be loopback, and both gameplay/client mods must be manifest-managed.

The remaining deployment prerequisites before public v1.0.0 acceptance are operational rather than implicit launcher fallbacks:

- generate and securely retain the real production signing private key;
- add only the corresponding public key to the launcher trust keyring and rebuild the final launcher once;
- publish the client-only mod at a stable public HTTPS URL;
- set the production Auth/server values;
- publish a signed production manifest referencing both managed mod artifacts;
- run the same-EXE update acceptance test against that signed manifest.

## Product boundary

Gameplay code remains in `Theyyk/re-volution-mod`; this launcher pipeline only consumes published gameplay release artifacts. The public resource-pack repository currently provides the small development bootstrap JSON because the client repository itself is private.
