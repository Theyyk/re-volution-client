using System.Diagnostics;
using System.IO.Compression;
using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class JavaRuntimeService
{
    private readonly HttpClient _httpClient;
    private readonly LauncherPaths _paths;

    public JavaRuntimeService(HttpClient httpClient, LauncherPaths paths)
    {
        _httpClient = httpClient;
        _paths = paths;
    }

    public async Task<string> EnsureJava8Async(JavaRuntimeManifest manifest, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (manifest.MajorVersion != 8) throw new InvalidDataException("RE:volution Client requires Java 8.");

        var managedJava = Path.GetFullPath(Path.Combine(_paths.Runtime, manifest.JavaRelativePath));
        EnsureInside(_paths.Runtime, managedJava);
        if (File.Exists(managedJava) && await IsJava8Async(managedJava, cancellationToken))
            return managedJava;

        var systemJava = await FindSystemJava8Async(cancellationToken);
        if (systemJava is not null) return systemJava;

        progress.Report("Java 8 не найдена. Загружаю управляемый runtime…");
        await InstallManagedRuntimeAsync(manifest, cancellationToken);
        if (!File.Exists(managedJava) || !await IsJava8Async(managedJava, cancellationToken))
            throw new InvalidDataException("Downloaded Java runtime did not provide a valid Java 8 executable.");
        return managedJava;
    }

    private async Task<string?> FindSystemJava8Async(CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { "javaw.exe", "java.exe" })
            if (await IsJava8Async(candidate, cancellationToken)) return candidate;
        return null;
    }

    private static async Task<bool> IsJava8Async(string executable, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(executable, "-version") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true } };
            process.Start();
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 && (error.Contains("version \"1.8", StringComparison.OrdinalIgnoreCase) || error.Contains("version \"8", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private async Task InstallManagedRuntimeAsync(JavaRuntimeManifest manifest, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Java runtime URL must use HTTPS.");
        if (manifest.Size <= 0 || manifest.Sha256.Length != 64)
            throw new InvalidDataException("Java runtime metadata is incomplete.");

        var archive = Path.Combine(_paths.Downloads, "java8.zip");
        using (var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }

        var info = new FileInfo(archive);
        if (info.Length != manifest.Size || !string.Equals(await Hashing.Sha256Async(archive, cancellationToken), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Java runtime verification failed.");

        if (Directory.Exists(_paths.Runtime)) Directory.Delete(_paths.Runtime, true);
        Directory.CreateDirectory(_paths.Runtime);
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(_paths.Runtime, entry.FullName));
            EnsureInside(_paths.Runtime, destination);
            if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(destination);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }
    }

    private static void EnsureInside(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive/path escapes the installation root.");
    }
}
