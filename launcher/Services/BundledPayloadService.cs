using System.Reflection;
using REvolutionClient.Launcher.Infrastructure;

namespace REvolutionClient.Launcher.Services;

internal sealed class BundledPayloadService
{
    private const string ResourcePrefix = "REvolutionClient.Payload.";
    private readonly LauncherPaths _paths;

    public BundledPayloadService(LauncherPaths paths) => _paths = paths;

    public async Task InstallAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
            throw new InvalidDataException("RE:volution Client payload is missing from the launcher build.");

        var mods = Path.Combine(_paths.Game, "mods");
        Directory.CreateDirectory(mods);
        var installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = resource[ResourcePrefix.Length..];
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Embedded payload file name is invalid.");
            installedNames.Add(fileName);
            progress.Report($"Устанавливаю {fileName}…");

            await using var source = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException($"Embedded payload '{resource}' is unreadable.");
            var target = Path.Combine(mods, fileName);
            var temporary = target + ".tmp";
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(output, cancellationToken);
            File.Move(temporary, target, true);
        }

        RemoveStaleOwnedJars(mods, installedNames);
    }

    private static void RemoveStaleOwnedJars(string mods, HashSet<string> installedNames)
    {
        foreach (var path in Directory.EnumerateFiles(mods, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (installedNames.Contains(name)) continue;
            var ownedClient = name.StartsWith("RE-volution-Client-Mod-", StringComparison.OrdinalIgnoreCase);
            var ownedGameplay = name.StartsWith("RE-volution-", StringComparison.OrdinalIgnoreCase) && !ownedClient;
            if (ownedClient || ownedGameplay) File.Delete(path);
        }
    }
}
