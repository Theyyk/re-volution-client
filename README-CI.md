# CI policy

CI optimization changes build scheduling and caches only. The unified DEV channel,
trusted completion boundary, manifest schema, PROD stable selector and runtime data
remain the same. No CI job deploys to the local DEV or PROD runtime.

## Change detection

Every existing push/PR trigger still starts a lightweight `changes` job. It validates
**all** workflow YAML with actionlint 1.7.12 and tests `.github/ci/changes.py` before
selecting components. `.github/ci/components.json` is the authoritative path policy;
`.github/ci/cases.json` contains regression examples.

Detection uses the complete Git diff, including deleted paths and both sides of
renames. PRs compare their head with the merge base. It does not use GitHub's
300-file trigger limit or paginated changed-files API. Unclassified non-documentation inputs also run all components. New branches, tags, manual
dispatch and unavailable comparison objects run all components. Docs-only changes
run lightweight checks; every workflow or filter change runs the component checks.
There is no workflow-level path skip that would leave a required check pending.

## Cache policy

Gradle has one owner for its user home: `gradle/actions/setup-gradle@v4`; do not add
`setup-java cache: gradle` or a second generic Gradle cache. It saves wrapper and
dependency downloads, compiled scripts/transforms and the local task build cache.
`--build-cache` permits reuse of cacheable compilation outputs. PRs read caches;
trusted branch builds may write their branch-scoped cache. Build directories,
credentials, project gradle.properties, daemon logs, runtime files and secrets are
not separately archived. Clean builds still run tests and rebuild final JARs.
Forge reobfuscation mutates its JAR, so archive tasks are explicitly excluded from
the task cache. Stable entry ordering/timestamps make JAR bytes reproducible.

.NET jobs use `actions/cache@v4` for an isolated `$GITHUB_WORKSPACE/.nuget/packages` directory only. There are no lock files:
keys include OS, SDK major, job family, and hashes of csproj/props/targets,
global.json, NuGet.config and any future packages.lock.json. Prefix fallback reuses
packages but never skips restore. No bin/obj/publish files, credentials or NuGet
configuration are cached. Existing publish/profile flags and deterministic compiler
behavior are preserved; a cache miss must yield the same build semantics.

References: [Gradle setup/cache](https://github.com/gradle/actions/blob/v4/docs/setup-gradle.md),
[NuGet caching inputs](https://github.com/actions/setup-dotnet#caching-nuget-packages),
[BuildKit GHA cache](https://docs.docker.com/build/cache/backends/gha/).

## Client triggers and publication

- `build.yml`: existing main/feature/release/fix pushes and main PRs, plus manual
  dispatch for full validation. Auth, manifest signing, client mod and launcher are
  selected independently. A launcher build also builds its embedded client mod;
  unrelated Auth code and docs do not build the Windows launcher. Manifest signer
  smoke tests no longer serialize the launcher when their artifacts are not inputs.
  A single publish compiles the launcher; the redundant standalone build is removed.
- `dev-client.yml`: develop push, dev-server-ready dispatch, manual and hourly
  fallback at minute 17. Immediate dispatch remains the normal path. Unchanged
  candidate/client SHA skips every heavy job. The hourly sweep replaces four
  15-minute sweeps and recovers a missed notification. Client source SHA remains
  part of the manifest identity: a real new develop revision still publishes.
- Ordinary build CI cancels older branch/PR runs. **dev-client-channel retains
  cancel-in-progress:false and its unchanged global lock.** Verification, immutable
  release creation, final monotonic/source rechecks and the single mutable manifest
  upload stay serialized. Neither a new push nor a schedule can interrupt publication.
- The server receipt supplies the already built exact gameplay JAR. No gameplay
  rebuild is introduced. Client Forge compile outputs may come from Gradle's task
  cache; the final reobfuscated client JAR is rebuilt deterministically.
  Trusted DEV publication also caches the complete verified DEV EXE and client JAR
  by exact client SHA, active workflow SHA and resolved toolchain version. No prefix
  restore is allowed. These are separate from Gradle's task cache: a new gameplay
  candidate can reuse the exact client payloads while all profile/race tests and final
  publication rechecks still run. PR/ordinary branch jobs never populate these keys.
  A missing/evicted entry rebuilds normally. No release asset is used as a mutable cache.
  Manual verify_only=true exercises both cache paths without publishing a release,
  tag, manifest or launcher download. The global channel lock remains held.
- Temporary client mod, launcher and DEV EXE handoff artifacts: 3 days. Immutable
  dev-build/dev-candidate releases, dev-latest and production artifacts are not pruned.

No Auth image build exists in CI: Compose builds Auth locally with its existing
separate csproj restore layer. Docker docs never trigger an Auth image build. No
registry publication or new Auth image pipeline was added just to use a cache.

ForgeGradle 5 resets timestamps during reobfuscation; gradle/reproducible-jar.gradle
normalizes the final ZIP envelope after reobfJar without changing class/resource bytes.
