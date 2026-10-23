using System.Net;
using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class ClientBootstrapService
{
    private readonly LauncherPaths _paths;
    private readonly JavaRuntimeService _javaRuntime;
    private readonly MinecraftClientService _minecraft;
    private readonly BundledPayloadService _payload;
    private readonly ManagedFileService _managedFiles;
    private readonly ClientConfigService _config;

    public ClientBootstrapService(
        LauncherPaths paths,
        JavaRuntimeService javaRuntime,
        MinecraftClientService minecraft,
        BundledPayloadService payload,
        ManagedFileService managedFiles,
        ClientConfigService config)
    {
        _paths = paths;
        _javaRuntime = javaRuntime;
        _minecraft = minecraft;
        _payload = payload;
        _managedFiles = managedFiles;
        _config = config;
    }

    public async Task<BootstrapResult> PrepareAsync(ClientManifest manifest, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ValidateManifest(manifest);
        LauncherProfile.ValidateManifest(manifest);
        _paths.EnsureCreated();

        if (LauncherProfile.IsDevelopment)
            progress.Report($"DEV {manifest.Branch} / {manifest.GameplayVersion} / {manifest.CommitSha[..12]}");

        progress.Report("Проверяю Java 8…");
        var java = await _javaRuntime.EnsureJava8Async(manifest.Java, progress, cancellationToken);

        var forgeVersionName = await _minecraft.EnsureInstalledAsync(manifest.Minecraft, java, progress, cancellationToken);

        progress.Report("Проверяю моды RE:volution…");
        var hasManagedGameplayMod = manifest.Files.Any(file => file.Role == ManagedFileRoles.GameplayMod);
        var hasManagedClientMod = manifest.Files.Any(file => file.Role == ManagedFileRoles.ClientMod);
        await _payload.InstallAsync(
            installGameplayMod: !hasManagedGameplayMod,
            installClientMod: !hasManagedClientMod,
            progress,
            cancellationToken);
        await _managedFiles.SyncAsync(manifest.Files, progress, cancellationToken);

        progress.Report("Настраиваю подключение к серверу…");
        await _config.WriteAsync(manifest.Server, cancellationToken);

        progress.Report("Клиент готов к запуску.");
        return new BootstrapResult(manifest, java, forgeVersionName);
    }

    private static void ValidateManifest(ClientManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ProductVersion)) throw new InvalidDataException("Manifest productVersion is required.");
        if (string.IsNullOrWhiteSpace(manifest.Auth.BaseUrl)) throw new InvalidDataException("Manifest auth.baseUrl is required.");
        if (manifest.Minecraft.Version != "1.12.2") throw new InvalidDataException("Unsupported Minecraft version.");
        if (manifest.Minecraft.ForgeVersion != "14.23.5.2859") throw new InvalidDataException("Unsupported Forge version.");
        if (manifest.Minecraft.MemoryMb is < 1024 or > 16384) throw new InvalidDataException("Minecraft memory setting is out of range.");
        if (string.IsNullOrWhiteSpace(manifest.Server.Host)) throw new InvalidDataException("Server host is missing.");
        if (manifest.Server.Port is < 1 or > 65535) throw new InvalidDataException("Server port is invalid.");

        var gameplayCount = manifest.Files.Count(file => file.Role == ManagedFileRoles.GameplayMod);
        var clientCount = manifest.Files.Count(file => file.Role == ManagedFileRoles.ClientMod);
        if (gameplayCount > 1) throw new InvalidDataException("Manifest may define only one gameplay mod.");
        if (clientCount > 1) throw new InvalidDataException("Manifest may define only one client mod.");

        if (manifest.DevelopmentOnly) return;

        if (!Uri.TryCreate(manifest.Auth.BaseUrl, UriKind.Absolute, out var authUri) || authUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Production auth.baseUrl must use HTTPS.");
        if (IsLoopbackHost(manifest.Server.Host))
            throw new InvalidDataException("Production server host must not be loopback.");
        if (gameplayCount != 1 || clientCount != 1)
            throw new InvalidDataException("Production manifest must manage exactly one gameplay mod and one client mod.");
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}

internal sealed record BootstrapResult(ClientManifest Manifest, JavaRuntimeInstallation Java, string ForgeVersionName);
