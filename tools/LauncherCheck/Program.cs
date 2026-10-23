using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
Check(LauncherProfile.ManifestUrl == "http://127.0.0.1:25567/api/v1/dev/channel", "DEV follows locally accepted server channel");
Check(new LauncherSettingsStore().Load().InstallRoot == LauncherProfile.DefaultInstallRoot, "DEV settings cannot import PROD install root");
await Reject(() => { new LauncherPaths(@"E:\MyProject\Client\Prod"); return Task.CompletedTask; }, "DEV rejects PROD root");
await Reject(() => new LauncherSettingsStore().SaveAsync(@"E:\Test", default), "DEV rejects legacy shared settings root");
await Reject(() => { LauncherProfile.ValidateManifest(new ClientManifest()); return Task.CompletedTask; }, "DEV rejects production manifest");
await Reject(() => { LauncherProfile.ValidateManifest(new ClientManifest { DevelopmentOnly = true }); return Task.CompletedTask; }, "DEV rejects missing remote payload");

var fixture = JsonNode.Parse("""
{
  "schemaVersion":1,"developmentOnly":true,"channel":"dev","branch":"fix/channel-test","commitSha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "gameplayVersion":"1.3.8","gameplaySha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","gameplaySize":123,
  "serverImage":"ghcr.io/theyyk/re-volution-server@sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
  "manifestUrl":"https://github.com/Theyyk/re-volution-client/releases/download/dev-build-fixture/manifest.dev.json",
  "source":{"gameplaySha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","clientSha":"dddddddddddddddddddddddddddddddddddddddd"},
  "sourceRun":{"id":10,"attempt":1,"createdAt":"2026-10-23T01:00:00Z"},"imageBuild":{"id":20,"attempt":1},
  "files":[
    {"role":"gameplay-mod","path":"mods/RE-volution.jar","size":123,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","downloadUrl":"https://github.com/Theyyk/re-volution-client/releases/download/dev-build-fixture/RE-volution.jar"},
    {"role":"client-mod","path":"mods/Client.jar","size":456,"sha256":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","downloadUrl":"https://github.com/Theyyk/re-volution-client/releases/download/dev-build-fixture/Client.jar"}
  ]
}
""")!;
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
ClientManifest Decode(JsonNode node) => JsonSerializer.Deserialize<ClientManifest>(node.ToJsonString(), jsonOptions)!;
LauncherProfile.ValidateManifest(Decode(fixture));
Console.WriteLine("PASS Complete exact DEV channel is accepted");
foreach (var field in new[] { "commitSha", "serverImage", "gameplaySha256", "branch", "manifestUrl" })
{
    var invalid = fixture.DeepClone();
    invalid[field] = field == "branch" ? "main" : "invalid";
    await Reject(() => { LauncherProfile.ValidateManifest(Decode(invalid)); return Task.CompletedTask; }, "DEV rejects inconsistent " + field);
}
using (var channelHttp = new HttpClient(new PayloadHandler(Encoding.UTF8.GetBytes(fixture.ToJsonString()))))
{
    var accepted = await new ManifestClient(channelHttp).GetAsync(new Uri(LauncherProfile.ManifestUrl), default);
    LauncherProfile.ValidateManifest(accepted);
    Check(accepted.CommitSha == Decode(fixture).CommitSha, "Local channel endpoint preserves server SHA");
}

// Exercise the real updater with deterministic HTTP fixtures: integrity failure, path traversal, and stale replacement.
var pathsForTest = new LauncherPaths(LauncherProfile.DefaultInstallRoot);
pathsForTest.EnsureCreated();
var bytes = Encoding.UTF8.GetBytes("verified-dev-test-payload");
using var fixtureHttp = new HttpClient(new PayloadHandler(bytes));
var updater = new ManagedFileService(fixtureHttp, pathsForTest);
ManagedFileManifest Entry(string path, string hash) => new() { Path = path, DownloadUrl = "https://fixture.invalid/mod.jar", Size = bytes.Length, Sha256 = hash, Role = "gameplay-mod" };
var hash = Convert.ToHexString(SHA256.HashData(bytes));
var fixtureName = "RE-volution-check-" + Guid.NewGuid().ToString("N") + ".jar";
await Reject(() => updater.SyncAsync(new[] { Entry("mods/../../outside.jar", hash) }, new InlineProgress(), default), "Updater rejects path escape");
await Reject(() => updater.SyncAsync(new[] { Entry("mods/" + fixtureName, new string('0', 64)) }, new InlineProgress(), default), "Updater rejects incorrect SHA-256");
Check(!File.Exists(Path.Combine(pathsForTest.Game, "mods/" + fixtureName)), "Invalid download is never activated");
var oldPath = Path.Combine(pathsForTest.Game, "mods/RE-volution-old-check.jar");
Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
await File.WriteAllTextAsync(oldPath, "old");
await updater.SyncAsync(new[] { Entry("mods/" + fixtureName, hash) }, new InlineProgress(), default);
Check(File.ReadAllBytes(Path.Combine(pathsForTest.Game, "mods/" + fixtureName)).SequenceEqual(bytes), "Valid remote payload is installed");
Check(!File.Exists(oldPath), "Stale gameplay JAR is removed inside DEV only");
File.Delete(Path.Combine(pathsForTest.Game, "mods/" + fixtureName));
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
