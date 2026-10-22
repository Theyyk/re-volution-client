namespace REvolutionClient.Launcher.Model;

internal sealed class LauncherSettings
{
    public string InstallRoot { get; init; } = LauncherSettingsStoreDefaults.DefaultInstallRoot;
}

internal static class LauncherSettingsStoreDefaults
{
    public static string DefaultInstallRoot =>
        Infrastructure.LauncherProfile.DefaultInstallRoot;
}
