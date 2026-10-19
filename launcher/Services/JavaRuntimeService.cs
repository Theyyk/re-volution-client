using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class JavaRuntimeService
{
    private const long MaximumJavaArchiveBytes = 512L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly LauncherPaths _paths;

    public JavaRuntimeService(HttpClient httpClient, LauncherPaths paths)
    {
        _httpClient = httpClient;
        _paths = paths;
    }

    public async Task<JavaRuntimeInstallation> EnsureJava8Async(JavaRuntimeManifest manifest, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (manifest.MajorVersion != 8) throw new InvalidDataException("RE:volution Client requires Java 8.");

        var managed = await FindManagedJava8Async(cancellationToken);
        if (managed is not null) return managed;

        var system = await FindSystemJava8Async(cancellationToken);
        if (system is not null) return system;

        progress.Report("Java 8 не найдена. Загружаю управляемый runtime…");
        var package = await ResolvePackageAsync(manifest, cancellationToken);
        await InstallManagedRuntimeAsync(package, cancellationToken);

        managed = await FindManagedJava8Async(cancellationToken);
        return managed ?? throw new InvalidDataException("Downloaded Java runtime did not provide a valid Java 8 executable.");
    }

    private async Task<JavaRuntimeInstallation?> FindManagedJava8Async(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.Runtime)) return null;
        foreach (var java in Directory.EnumerateFiles(_paths.Runtime, "java.exe", SearchOption.AllDirectories).OrderBy(path => path.Length))
        {
            if (!await IsJava8Async(java, cancellationToken)) continue;
            var javaw = Path.Combine(Path.GetDirectoryName(java)!, "javaw.exe");
            return new JavaRuntimeInstallation(java, File.Exists(javaw) ? javaw : java);
        }
        return null;
    }

    private async Task<JavaRuntimeInstallation?> FindSystemJava8Async(CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));

        try
        {
            using var where = new Process
            {
                StartInfo = new ProcessStartInfo("where.exe", "java.exe")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            where.Start();
            var output = await where.StandardOutput.ReadToEndAsync(cancellationToken);
            await where.WaitForExitAsync(cancellationToken);
            if (where.ExitCode == 0)
                candidates.AddRange(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
        catch
        {
            // PATH lookup is best-effort. Managed Java is the fallback.
        }

        foreach (var java in candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!await IsJava8Async(java, cancellationToken)) continue;
            var javaw = Path.Combine(Path.GetDirectoryName(java)!, "javaw.exe");
            return new JavaRuntimeInstallation(java, File.Exists(javaw) ? javaw : java);
        }
        return null;
    }

    private static async Task<bool> IsJava8Async(string executable, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable, "-version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var versionText = (await stdout) + "\n" + (await stderr);
            return process.ExitCode == 0 &&
                   (versionText.Contains("version \"1.8", StringComparison.OrdinalIgnoreCase) ||
                    versionText.Contains("version \"8", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private async Task<JavaRuntimePackage> ResolvePackageAsync(JavaRuntimeManifest manifest, CancellationToken cancellationToken)
    {
        if (string.Equals(manifest.Provider, "adoptium", StringComparison.OrdinalIgnoreCase))
            return await ResolveAdoptiumPackageAsync(cancellationToken);

        if (!string.Equals(manifest.Provider, "static", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported Java runtime provider.");
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Java runtime URL must use HTTPS.");
        if (manifest.Size <= 0 || manifest.Size > MaximumJavaArchiveBytes || manifest.Sha256.Length != 64)
            throw new InvalidDataException("Java runtime metadata is incomplete.");
        return new JavaRuntimePackage(uri, manifest.Sha256, manifest.Size);
    }

    private async Task<JavaRuntimePackage> ResolveAdoptiumPackageAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri("https://api.adoptium.net/v3/assets/latest/8/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse");
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            throw new InvalidDataException("Adoptium returned no Java 8 runtime.");

        var package = document.RootElement[0].GetProperty("binary").GetProperty("package");
        var link = package.GetProperty("link").GetString();
        var checksum = package.GetProperty("checksum").GetString();
        var size = package.GetProperty("size").GetInt64();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Adoptium Java package URL is invalid.");
        if (string.IsNullOrWhiteSpace(checksum) || checksum.Length != 64 || size <= 0 || size > MaximumJavaArchiveBytes)
            throw new InvalidDataException("Adoptium Java package metadata is invalid.");
        return new JavaRuntimePackage(downloadUri, checksum, size);
    }

    private async Task InstallManagedRuntimeAsync(JavaRuntimePackage package, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Downloads);
        var archive = Path.Combine(_paths.Downloads, $"java8-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await _httpClient.GetAsync(package.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long contentLength && contentLength != package.Size)
                    throw new InvalidDataException("Java runtime size does not match metadata.");

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long written = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    written += read;
                    if (written > package.Size) throw new InvalidDataException("Java runtime download exceeded expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (written != package.Size) throw new InvalidDataException("Java runtime download is incomplete.");
            }

            if (!string.Equals(await Hashing.Sha256Async(archive, cancellationToken), package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Java runtime verification failed.");

            if (Directory.Exists(_paths.Runtime)) Directory.Delete(_paths.Runtime, true);
            Directory.CreateDirectory(_paths.Runtime);
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                var destination = Path.GetFullPath(Path.Combine(_paths.Runtime, entry.FullName));
                EnsureInside(_paths.Runtime, destination);
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
        }
    }

    private static void EnsureInside(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive/path escapes the installation root.");
    }
}

internal sealed record JavaRuntimeInstallation(string JavaExecutable, string JavawExecutable);
internal sealed record JavaRuntimePackage(Uri DownloadUri, string Sha256, long Size);
