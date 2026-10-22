using System.Net;
using System.Security.Cryptography;
using System.Text;
using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;
using REvolutionClient.Launcher.Services;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

async Task Reject(Func<Task> action, string message)
{
    try { await action(); }
    catch (InvalidDataException) { Console.WriteLine("PASS " + message); return; }
    throw new Exception("Expected rejection: " + message);
}

using var http = new HttpClient();
if (args.Length == 1 && args[0] == "--verify-production")
{
    Check(!LauncherProfile.IsDevelopment, "Production verifier uses PROD profile");
    var manifest = await new ManifestClient(http).GetAsync(new Uri(LauncherProfile.ManifestUrl), default);
    Check(!manifest.DevelopmentOnly, "Current production manifest signature is accepted");
    Console.WriteLine("Stable endpoint: " + manifest.Server.Host + ":" + manifest.Server.Port);
    return;
}
if (args.Length == 1 && args[0] == "--prepare")
{
    Check(LauncherProfile.IsDevelopment, "Only DEV can prepare without opening the login form");
    var manifest = await new ManifestClient(http).GetAsync(new Uri(LauncherProfile.ManifestUrl + "?check=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds()), default);
    LauncherProfile.ValidateManifest(manifest);
    // This explicit diagnostic mode prepares files but never authenticates or launches a game.
    var paths = new LauncherPaths(LauncherProfile.DefaultInstallRoot);
    var minecraft = new MinecraftClientService(http, paths);
    var bootstrap = new ClientBootstrapService(paths, new JavaRuntimeService(http, paths), minecraft,
        new BundledPayloadService(paths), new ManagedFileService(http, paths), new ClientConfigService(paths));
    var result = await bootstrap.PrepareAsync(manifest, new InlineProgress(), default);
    Console.WriteLine("PREPARED " + result.ForgeVersionName + " " + paths.Root);
    return;
}

if (!LauncherProfile.IsDevelopment)
{
    Check(LauncherProfile.AppName == "REvolutionClient", "PROD keeps existing settings namespace");
    Check(LauncherProfile.ManifestUrl.EndsWith("/main/client/manifest.production.signed.json"), "PROD keeps signed stable URL");
    return;
}

Check(LauncherProfile.AppName == "REvolutionClientDev", "DEV has separate settings namespace");
Check(LauncherProfile.EnvironmentPrefix == "REVOLUTION_CLIENT_DEV", "DEV ignores PROD environment overrides");
Check(LauncherProfile.ManifestUrl.Contains("/dev-latest/manifest.dev.json"), "DEV uses separate public channel");
Check(new LauncherSettingsStore().Load().InstallRoot == LauncherProfile.DefaultInstallRoot, "DEV settings cannot import PROD install root");
await Reject(() => { new LauncherPaths(@"E:\MyProject\Client\Prod"); return Task.CompletedTask; }, "DEV rejects PROD root");
await Reject(() => new LauncherSettingsStore().SaveAsync(@"E:\Test", default), "DEV rejects legacy shared settings root");
await Reject(() => { LauncherProfile.ValidateManifest(new ClientManifest()); return Task.CompletedTask; }, "DEV rejects production manifest");
await Reject(() => { LauncherProfile.ValidateManifest(new ClientManifest { DevelopmentOnly = true }); return Task.CompletedTask; }, "DEV rejects missing remote payload");

// Exercise the real updater with deterministic HTTP fixtures: integrity failure, path traversal, and stale replacement.
var pathsForTest = new LauncherPaths(LauncherProfile.DefaultInstallRoot);
pathsForTest.EnsureCreated();
var bytes = Encoding.UTF8.GetBytes("verified-dev-test-payload");
using var fixtureHttp = new HttpClient(new PayloadHandler(bytes));
var updater = new ManagedFileService(fixtureHttp, pathsForTest);
ManagedFileManifest Entry(string path, string hash) => new() { Path = path, DownloadUrl = "https://fixture.invalid/mod.jar", Size = bytes.Length, Sha256 = hash, Role = "gameplay-mod" };
var hash = Convert.ToHexString(SHA256.HashData(bytes));
await Reject(() => updater.SyncAsync(new[] { Entry("mods/../../outside.jar", hash) }, new InlineProgress(), default), "Updater rejects path escape");
await Reject(() => updater.SyncAsync(new[] { Entry("mods/RE-volution-check.jar", new string('0', 64)) }, new InlineProgress(), default), "Updater rejects incorrect SHA-256");
Check(!File.Exists(Path.Combine(pathsForTest.Game, "mods/RE-volution-check.jar")), "Invalid download is never activated");
var oldPath = Path.Combine(pathsForTest.Game, "mods/RE-volution-old-check.jar");
Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
await File.WriteAllTextAsync(oldPath, "old");
await updater.SyncAsync(new[] { Entry("mods/RE-volution-check.jar", hash) }, new InlineProgress(), default);
Check(File.ReadAllBytes(Path.Combine(pathsForTest.Game, "mods/RE-volution-check.jar")).SequenceEqual(bytes), "Valid remote payload is installed");
Check(!File.Exists(oldPath), "Stale gameplay JAR is removed inside DEV only");
Console.WriteLine("All profile and updater checks passed.");

sealed class PayloadHandler(byte[] bytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
}

sealed class InlineProgress : IProgress<string>
{
    public void Report(string value) => Console.WriteLine(value);
}
