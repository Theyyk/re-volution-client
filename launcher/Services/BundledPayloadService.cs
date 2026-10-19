using System.Reflection;
using REvolutionClient.Launcher.Infrastructure;

namespace REvolutionClient.Launcher.Services;

internal sealed class BundledPayloadService
{
    private const string ResourcePrefix = "REvolutionClient.Payload.";
    private readonly LauncherPaths _paths;

    public BundledPayloadService(LauncherPaths paths) => _paths = paths;

    public async Task InstallAsync(
        bool installGameplayMod,
        bool installClientMod,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        if (!installGameplayMod && !installClientMod) return;

        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(resource => new PayloadResource(resource, resource[ResourcePrefix.Length..]))
            .ToArray();

        var selected = resources
            .Where(resource =>
                (installGameplayMod && IsGameplayMod(resource.FileName)) ||
                (installClientMod && IsClientMod(resource.FileName)))
            .ToArray();

        if (installGameplayMod && !selected.Any(resource => IsGameplayMod(resource.FileName)))
            throw new InvalidDataException("Bundled RE:volution gameplay mod is missing from the launcher build.");
        if (installClientMod && !selected.Any(resource => IsClientMod(resource.FileName)))
            throw new InvalidDataException("Bundled RE:volution client mod is missing from the launcher build.");

        var mods = Path.Combine(_paths.Game, "mods");
        Directory.CreateDirectory(mods);
        var installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resource.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Embedded payload file name is invalid.");

            installedNames.Add(resource.FileName);
            progress.Report($"Устанавливаю {resource.FileName}…");

            await using var source = assembly.GetManifestResourceStream(resource.ResourceName)
                ?? throw new InvalidDataException($"Embedded payload '{resource.ResourceName}' is unreadable.");
            var target = Path.Combine(mods, resource.FileName);
            var temporary = target + ".tmp";
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(output, cancellationToken);
            File.Move(temporary, target, true);
        }

        RemoveStaleOwnedJars(mods, installedNames, installGameplayMod, installClientMod);
    }

    private static void RemoveStaleOwnedJars(
        string mods,
        HashSet<string> installedNames,
        bool manageGameplayMod,
        bool manageClientMod)
    {
        foreach (var path in Directory.EnumerateFiles(mods, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (installedNames.Contains(name)) continue;

            if (manageClientMod && IsClientMod(name))
                File.Delete(path);
            else if (manageGameplayMod && IsGameplayMod(name))
                File.Delete(path);
        }
    }

    private static bool IsClientMod(string fileName) =>
        fileName.StartsWith("RE-volution-Client-Mod-", StringComparison.OrdinalIgnoreCase);

    private static bool IsGameplayMod(string fileName) =>
        fileName.StartsWith("RE-volution-", StringComparison.OrdinalIgnoreCase) && !IsClientMod(fileName);

    private sealed record PayloadResource(string ResourceName, string FileName);
}
