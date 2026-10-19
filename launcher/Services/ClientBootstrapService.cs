using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class ClientBootstrapService
{
    private readonly LauncherPaths _paths;
    private readonly ManifestClient _manifestClient;
    private readonly JavaRuntimeService _javaRuntime;

    public ClientBootstrapService(LauncherPaths paths, ManifestClient manifestClient, JavaRuntimeService javaRuntime)
    {
        _paths = paths;
        _manifestClient = manifestClient;
        _javaRuntime = javaRuntime;
    }

    public async Task<BootstrapResult> PrepareAsync(Uri manifestUri, IProgress<string> progress, CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        progress.Report("Получаю manifest…");
        var manifest = await _manifestClient.GetAsync(manifestUri, cancellationToken);
        ValidateManifest(manifest);
        progress.Report("Проверяю Java 8…");
        var javaExecutable = await _javaRuntime.EnsureJava8Async(manifest.Java, progress, cancellationToken);
        progress.Report("Базовая проверка окружения завершена.");
        return new BootstrapResult(manifest, javaExecutable);
    }

    private static void ValidateManifest(ClientManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ProductVersion)) throw new InvalidDataException("Manifest productVersion is required.");
        if (manifest.Minecraft.Version != "1.12.2") throw new InvalidDataException("Unsupported Minecraft version.");
        if (manifest.Minecraft.ForgeVersion != "14.23.5.2859") throw new InvalidDataException("Unsupported Forge version.");
        if (string.IsNullOrWhiteSpace(manifest.Server.Host)) throw new InvalidDataException("Server host is missing.");
        if (manifest.Server.Port is < 1 or > 65535) throw new InvalidDataException("Server port is invalid.");
    }
}

internal sealed record BootstrapResult(ClientManifest Manifest, string JavaExecutable);
