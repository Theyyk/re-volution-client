using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Infrastructure;

internal static class LauncherProfile
{
#if DEV_LAUNCHER
    public static bool IsDevelopment => true;
    public const string AppName = "REvolutionClientDev";
    public const string DisplayName = "RE:volution Client DEV";
    public const string ManifestUrl = "https://github.com/Theyyk/re-volution-client/releases/download/dev-latest/manifest.dev.json";
    public const string EnvironmentPrefix = "REVOLUTION_CLIENT_DEV";
#else
    public static bool IsDevelopment => false;
    public const string AppName = "REvolutionClient";
    public const string DisplayName = "RE:volution Client";
    public const string ManifestUrl = "https://raw.githubusercontent.com/Theyyk/re-volution-resource-pack/main/client/manifest.production.signed.json";
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
    }
}
