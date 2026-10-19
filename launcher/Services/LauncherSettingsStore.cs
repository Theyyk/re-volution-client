using System.Text.Json;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class LauncherSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsFile;

    public LauncherSettingsStore()
    {
        var settingsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "REvolutionClient");
        _settingsFile = Path.Combine(settingsRoot, "launcher-settings.json");
    }

    public LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return new LauncherSettings();
            var json = File.ReadAllText(_settingsFile);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (settings is null || string.IsNullOrWhiteSpace(settings.InstallRoot)) return new LauncherSettings();
            return new LauncherSettings { InstallRoot = ValidateInstallRoot(settings.InstallRoot) };
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    public async Task SaveAsync(string installRoot, CancellationToken cancellationToken)
    {
        var validated = ValidateInstallRoot(installRoot);
        EnsureWritable(validated);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        var temp = _settingsFile + ".tmp";
        var json = JsonSerializer.Serialize(new LauncherSettings { InstallRoot = validated }, JsonOptions);
        await File.WriteAllTextAsync(temp, json, cancellationToken);
        File.Move(temp, _settingsFile, true);
    }

    public static string ValidateInstallRoot(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) throw new InvalidDataException("Папка установки не указана.");
        var full = Path.GetFullPath(installRoot.Trim());
        var root = Path.GetPathRoot(full);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                          root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                          StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Нельзя устанавливать RE:volution Client прямо в корень диска.");
        return full;
    }

    private static void EnsureWritable(string path)
    {
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, $".revolution-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "ok");
        }
        finally
        {
            if (File.Exists(probe)) File.Delete(probe);
        }
    }
}
