using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Services;

namespace REvolutionClient.Launcher.UI;

internal sealed class MainForm : Form
{
    private readonly Label _status = new() { AutoSize = true, Text = "Готов к проверке", Left = 24, Top = 28 };
    private readonly Button _play = new() { Text = "Играть", Width = 180, Height = 44, Left = 24, Top = 70 };
    private CancellationTokenSource? _operation;

    // Bootstrap endpoint is intentionally distribution configuration, not a gameplay/server secret.
    private static readonly Uri ManifestUri = new("https://example.invalid/revolution/client/manifest.json");

    public MainForm()
    {
        Text = "RE:volution Client";
        ClientSize = new Size(460, 145);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Controls.Add(_status);
        Controls.Add(_play);
        _play.Click += OnPlayAsync;
    }

    private async void OnPlayAsync(object? sender, EventArgs e)
    {
        _play.Enabled = false;
        _operation = new CancellationTokenSource();
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromMinutes(10) };
            var paths = new LauncherPaths();
            var progress = new Progress<string>(message => _status.Text = message);
            var bootstrap = new ClientBootstrapService(paths, new ManifestClient(http), new JavaRuntimeService(http, paths));
            var result = await bootstrap.PrepareAsync(ManifestUri, progress, _operation.Token);
            MessageBox.Show(this, $"Java 8 готова: {result.JavaExecutable}\nСледующий этап foundation — установка Minecraft/Forge и запуск.", "RE:volution Client", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { _status.Text = "Операция отменена."; }
        catch (Exception ex)
        {
            _status.Text = "Ошибка подготовки клиента.";
            MessageBox.Show(this, ex.Message, "RE:volution Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _play.Enabled = true; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _operation?.Cancel();
        base.Dispose(disposing);
    }
}
