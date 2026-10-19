using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class ManagedFileService
{
    private const long MaximumManagedFileBytes = 2L * 1024 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly LauncherPaths _paths;

    public ManagedFileService(HttpClient httpClient, LauncherPaths paths)
    {
        _httpClient = httpClient;
        _paths = paths;
    }

    public async Task SyncAsync(IEnumerable<ManagedFileManifest> files, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var materialized = files.ToArray();
        foreach (var file in materialized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(file);
            var target = ResolveTarget(file.Path);
            if (await IsCurrentAsync(target, file, cancellationToken)) continue;

            progress.Report($"Обновляю {file.Path}…");
            await DownloadAndReplaceAsync(target, file, cancellationToken);
        }

        RemoveStaleOwnedMods(materialized);
    }

    private static void Validate(ManagedFileManifest file)
    {
        if (string.IsNullOrWhiteSpace(file.Path) || Path.IsPathRooted(file.Path))
            throw new InvalidDataException("Managed file path must be relative.");
        if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"Managed file URL must use HTTPS: {file.Path}");
        if (file.Size <= 0 || file.Size > MaximumManagedFileBytes || file.Sha256.Length != 64)
            throw new InvalidDataException($"Managed file metadata is invalid: {file.Path}");
        if (!string.IsNullOrWhiteSpace(file.Role) &&
            file.Role != ManagedFileRoles.GameplayMod &&
            file.Role != ManagedFileRoles.ClientMod)
            throw new InvalidDataException($"Unknown managed file role: {file.Role}");
        if (!string.IsNullOrWhiteSpace(file.Role) && !file.Path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Managed mod role must target the mods directory: {file.Path}");
    }

    private string ResolveTarget(string relativePath)
    {
        var target = Path.GetFullPath(Path.Combine(_paths.Game, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(_paths.Game).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Managed file path escapes the game directory.");
        return target;
    }

    private static async Task<bool> IsCurrentAsync(string target, ManagedFileManifest file, CancellationToken cancellationToken)
    {
        if (!File.Exists(target)) return false;
        var info = new FileInfo(target);
        if (info.Length != file.Size) return false;
        return string.Equals(await Hashing.Sha256Async(target, cancellationToken), file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task DownloadAndReplaceAsync(string target, ManagedFileManifest file, CancellationToken cancellationToken)
    {
        var uri = new Uri(file.DownloadUrl);
        Directory.CreateDirectory(_paths.Downloads);
        var temporary = Path.Combine(_paths.Downloads, $"managed-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length != file.Size)
                    throw new InvalidDataException($"Unexpected size for {file.Path}.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long written = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    written += read;
                    if (written > file.Size) throw new InvalidDataException($"Download exceeded expected size for {file.Path}.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (written != file.Size) throw new InvalidDataException($"Download is incomplete for {file.Path}.");
            }

            if (!string.Equals(await Hashing.Sha256Async(temporary, cancellationToken), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 verification failed for {file.Path}.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void RemoveStaleOwnedMods(IEnumerable<ManagedFileManifest> files)
    {
        var gameplay = files.SingleOrDefault(file => file.Role == ManagedFileRoles.GameplayMod);
        var client = files.SingleOrDefault(file => file.Role == ManagedFileRoles.ClientMod);
        if (gameplay is null && client is null) return;

        var mods = Path.Combine(_paths.Game, "mods");
        if (!Directory.Exists(mods)) return;

        var gameplayName = gameplay is null ? null : Path.GetFileName(ResolveTarget(gameplay.Path));
        var clientName = client is null ? null : Path.GetFileName(ResolveTarget(client.Path));

        foreach (var path in Directory.EnumerateFiles(mods, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            var isClient = name.StartsWith("RE-volution-Client-Mod-", StringComparison.OrdinalIgnoreCase);
            var isGameplay = name.StartsWith("RE-volution-", StringComparison.OrdinalIgnoreCase) && !isClient;

            if (client is not null && isClient && !string.Equals(name, clientName, StringComparison.OrdinalIgnoreCase))
                File.Delete(path);
            else if (gameplay is not null && isGameplay && !string.Equals(name, gameplayName, StringComparison.OrdinalIgnoreCase))
                File.Delete(path);
        }
    }
}
