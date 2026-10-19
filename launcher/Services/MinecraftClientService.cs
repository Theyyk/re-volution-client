using System.Diagnostics;
using CmlLib.Core;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class MinecraftClientService
{
    private readonly HttpClient _httpClient;
    private readonly LauncherPaths _paths;

    public MinecraftClientService(HttpClient httpClient, LauncherPaths paths)
    {
        _httpClient = httpClient;
        _paths = paths;
    }

    public async Task<string> EnsureInstalledAsync(MinecraftManifest manifest, JavaRuntimeInstallation java, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var launcher = CreateLauncher();
        var fileProgress = new Progress<InstallerProgressChangedEventArgs>(e =>
            progress.Report($"Minecraft/Forge: {e.Name} ({e.ProgressedTasks}/{e.TotalTasks})"));
        var byteProgress = new Progress<ByteProgress>(_ => { });

        progress.Report($"Проверяю Minecraft {manifest.Version} и Forge {manifest.ForgeVersion}…");
        var loader = new ForgeVersionLoader(_httpClient);
        var versions = await loader.GetForgeVersions(manifest.Version);
        cancellationToken.ThrowIfCancellationRequested();
        var forge = versions.FirstOrDefault(version => string.Equals(version.ForgeVersionName, manifest.ForgeVersion, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Forge {manifest.ForgeVersion} for Minecraft {manifest.Version} was not found.");
        var installer = new ForgeInstallerVersionMapper().CreateInstaller(forge);

        if (!await IsVersionInstalledAsync(launcher, installer.VersionName, cancellationToken))
        {
            progress.Report("Устанавливаю Minecraft и Forge…");
            await launcher.InstallAsync(manifest.Version, fileProgress, byteProgress, cancellationToken);
            await installer.Install(launcher.MinecraftPath, launcher.GameInstaller, new ForgeInstallOptions
            {
                JavaPath = java.JavaExecutable,
                FileProgress = fileProgress,
                ByteProgress = byteProgress,
                InstallerOutput = new Progress<string>(line =>
                {
                    if (!string.IsNullOrWhiteSpace(line)) progress.Report("Forge: " + line.Trim());
                }),
                CancellationToken = cancellationToken,
                SkipIfAlreadyInstalled = true
            });
            await launcher.GetAllVersionsAsync(cancellationToken);
        }

        await launcher.InstallAsync(installer.VersionName, fileProgress, byteProgress, cancellationToken);
        return installer.VersionName;
    }

    public async Task<Process> AuthenticateAndLaunchAsync(string versionName, MinecraftManifest manifest, JavaRuntimeInstallation java, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Проверяю вход в Microsoft…");
        var loginHandler = JELoginHandlerBuilder.BuildDefault();
        CmlLib.Core.Auth.MSession session;
        try
        {
            session = await loginHandler.AuthenticateSilently(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            progress.Report("Требуется вход в Microsoft. Откройте окно авторизации…");
            session = await loginHandler.AuthenticateInteractively(cancellationToken);
        }

        progress.Report("Запускаю Minecraft…");
        var launcher = CreateLauncher();
        var process = await launcher.BuildProcessAsync(versionName, new MLaunchOption
        {
            Session = session,
            JavaPath = java.JavawExecutable,
            MaximumRamMb = manifest.MemoryMb,
            GameLauncherName = "RE:volution Client",
            GameLauncherVersion = "1.0.0"
        }, cancellationToken);
        if (!process.Start()) throw new InvalidOperationException("Minecraft process could not be started.");
        return process;
    }

    private MinecraftLauncher CreateLauncher() => new(new MinecraftPath(_paths.Game));

    private static async Task<bool> IsVersionInstalledAsync(MinecraftLauncher launcher, string versionName, CancellationToken cancellationToken)
    {
        try
        {
            await launcher.GetVersionAsync(versionName, cancellationToken);
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }
}
