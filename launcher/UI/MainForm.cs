using System.Net;
using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;
using REvolutionClient.Launcher.Services;

namespace REvolutionClient.Launcher.UI;

internal sealed class MainForm : Form
{
    private const string EmbeddedManifestResource = "REvolutionClient.Manifest.bootstrap.dev.json";
    private const string ManifestUrlEnvironmentVariable = "REVOLUTION_CLIENT_MANIFEST_URL";

    private readonly Label _status = new() { AutoSize = true, Text = "Готов к запуску", Left = 24, Top = 28, MaximumSize = new Size(610, 0) };
    private readonly Button _play = new() { Text = "Играть", Width = 180, Height = 44, Left = 24, Top = 78 };
    private CancellationTokenSource? _operation;

    public MainForm()
    {
        Text = "RE:volution Client";
        ClientSize = new Size(660, 150);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Controls.Add(_status);
        Controls.Add(_play);
        _play.Click += OnPlayAsync;
    }

    private async void OnPlayAsync(object? sender, EventArgs e)
    {
        _play.Enabled = false;
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.All
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            var paths = new LauncherPaths();
            var progress = new Progress<string>(message => _status.Text = message);
            var manifestClient = new ManifestClient(http);
            var manifest = await LoadManifestAsync(manifestClient, _operation.Token);
            var minecraft = new MinecraftClientService(http, paths);
            var bootstrap = new ClientBootstrapService(
                paths,
                new JavaRuntimeService(http, paths),
                minecraft,
                new BundledPayloadService(paths),
                new ManagedFileService(http, paths),
                new ClientConfigService(paths));

            var result = await bootstrap.PrepareAsync(manifest, progress, _operation.Token);
            await minecraft.AuthenticateAndLaunchAsync(result.ForgeVersionName, result.Manifest.Minecraft, result.Java, progress, _operation.Token);
            _status.Text = "Minecraft запущен.";
            WindowState = FormWindowState.Minimized;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Операция отменена.";
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка подготовки или запуска клиента.";
            MessageBox.Show(this, ex.Message, "RE:volution Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _play.Enabled = true;
        }
    }

    private static Task<ClientManifest> LoadManifestAsync(ManifestClient manifestClient, CancellationToken cancellationToken)
    {
        var overrideUrl = Environment.GetEnvironmentVariable(ManifestUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(overrideUrl))
            return Task.FromResult(manifestClient.GetEmbedded(EmbeddedManifestResource));
        if (!Uri.TryCreate(overrideUrl, UriKind.Absolute, out var uri))
            throw new InvalidDataException($"{ManifestUrlEnvironmentVariable} is not a valid absolute URL.");
        return manifestClient.GetAsync(uri, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _operation?.Cancel();
            _operation?.Dispose();
        }
        base.Dispose(disposing);
    }
}
