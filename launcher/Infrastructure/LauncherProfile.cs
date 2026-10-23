using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Infrastructure;

internal static class LauncherProfile
{
#if DEV_LAUNCHER
    public static bool IsDevelopment => true;
    public const string AppName = "REvolutionClientDev";
    public const string DisplayName = "RE:volution Client DEV";
    public const string ManifestUrl = "http://127.0.0.1:25567/api/v1/dev/channel";
    public const string EnvironmentPrefix = "REVOLUTION_CLIENT_DEV";
#else
    public static bool IsDevelopment => false;
    public const string AppName = "REvolutionClient";
    public const string DisplayName = "RE:volution Client";
    public const string ManifestUrl = "https://raw.githubusercontent.com/Theyyk/re-volution-client/main/manifest/manifest.production.signed.json";
    public const string EnvironmentPrefix = "REVOLUTION_CLIENT";
#endif

    public static string SettingsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string DefaultInstallRoot => IsDevelopment
        ? Path.Combine(AppContext.BaseDirectory, "install-dev")
        : Path.Combine(SettingsRoot);

    // DEV always uses its own directory, even if a settings file was edited by hand.
    public static void ValidateInstallRoot(string path)
    {
        if (!IsDevelopment) return;
        var expected = Path.GetFullPath(DefaultInstallRoot).TrimEnd('\\', '/');
        var actual = Path.GetFullPath(path).TrimEnd('\\', '/');
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DEV использует только собственную папку install-dev рядом с EXE.");
        for (var directory = new DirectoryInfo(actual); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "Prod", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DEV launcher нельзя запускать из папки PROD.");
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("DEV install root cannot contain directory links or junctions.");
        }
    }

    public static void ValidateManifest(ClientManifest manifest)
    {
        if (!IsDevelopment) return;
        if (!manifest.DevelopmentOnly)
            throw new InvalidDataException("DEV launcher requires a developmentOnly manifest.");
        if (manifest.Files.Count(file => file.Role == ManagedFileRoles.GameplayMod) != 1 ||
            manifest.Files.Count(file => file.Role == ManagedFileRoles.ClientMod) != 1)
            throw new InvalidDataException("DEV manifest must manage exactly one gameplay mod and one client mod; bundled fallback is disabled.");
        bool Match(string value, string pattern) => System.Text.RegularExpressions.Regex.IsMatch(value ?? "", pattern);
        if (manifest.SchemaVersion != 1 || manifest.Channel != "dev" ||
            !Match(manifest.CommitSha, "^[a-f0-9]{40}$") || manifest.Source.GameplaySha != manifest.CommitSha ||
            !Match(manifest.Source.ClientSha, "^[a-f0-9]{40}$") ||
            string.IsNullOrWhiteSpace(manifest.Branch) ||
            System.Text.RegularExpressions.Regex.IsMatch(manifest.Branch, "^(main|master|stable|release)(/|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            string.IsNullOrWhiteSpace(manifest.GameplayVersion) ||
            !Match(manifest.ServerImage, @"^ghcr\.io/theyyk/re-volution-server@sha256:[a-f0-9]{64}$") ||
            !Match(manifest.ManifestUrl, @"^https://github\.com/Theyyk/re-volution-client/releases/download/dev-build-[A-Za-z0-9-]+/manifest\.dev\.json$") ||
            manifest.SourceRun.Id <= 0 || manifest.ImageBuild.Id <= 0 ||
            manifest.SourceRun.Attempt <= 0 || manifest.ImageBuild.Attempt <= 0 ||
            !DateTimeOffset.TryParse(manifest.SourceRun.CreatedAt, out _))
            throw new InvalidDataException("DEV channel identity or ordering metadata is invalid.");
        var gameplay = manifest.Files.Single(file => file.Role == ManagedFileRoles.GameplayMod);
        if (gameplay.Sha256 != manifest.GameplaySha256 || gameplay.Size != manifest.GameplaySize ||
            manifest.GameplaySize <= 0 || !Match(manifest.GameplaySha256, "^[a-f0-9]{64}$"))
            throw new InvalidDataException("DEV gameplay artifact differs from the server channel.");
        var prefix = manifest.ManifestUrl[..(manifest.ManifestUrl.LastIndexOf('/') + 1)];
        if (manifest.Files.Any(file => !file.DownloadUrl.StartsWith(prefix, StringComparison.Ordinal) ||
            !Match(file.Sha256, "^[a-f0-9]{64}$") || file.Size <= 0))
            throw new InvalidDataException("DEV artifacts must belong to the same immutable channel release.");
    }
}
