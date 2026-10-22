using System.Net;
using REvolutionClient.Launcher.Infrastructure;
using REvolutionClient.Launcher.Model;
using REvolutionClient.Launcher.Services;

namespace REvolutionClient.Launcher.UI;

internal sealed class MainForm : Form
{
    private const string DefaultManifestUrl = LauncherProfile.ManifestUrl;
    private const string ManifestUrlEnvironmentVariable = LauncherProfile.EnvironmentPrefix + "_MANIFEST_URL";
    private const string ManifestFileEnvironmentVariable = LauncherProfile.EnvironmentPrefix + "_MANIFEST_FILE";

    private readonly TextBox _username = new() { Left = 142, Top = 22, Width = 250 };
    private readonly TextBox _password = new() { Left = 142, Top = 56, Width = 250, UseSystemPasswordChar = true };
    private readonly Button _login = new() { Text = "Войти", Left = 410, Top = 20, Width = 100, Height = 28 };
    private readonly Button _register = new() { Text = "Регистрация", Left = 518, Top = 20, Width = 118, Height = 28 };
    private readonly TextBox _installRoot = new() { Left = 142, Top = 101, Width = 390 };
    private readonly Button _browse = new() { Text = "Изменить…", Left = 540, Top = 99, Width = 96, Height = 28 };
    private readonly Label _status = new() { AutoSize = true, Text = "Войдите в RE:volution", Left = 24, Top = 147, MaximumSize = new Size(610, 0) };
    private readonly Button _play = new() { Text = "Играть", Width = 180, Height = 44, Left = 24, Top = 185, Enabled = false };

    private readonly LauncherSettingsStore _settingsStore = new();
    private CancellationTokenSource? _operation;
    private AuthSession? _session;

    public MainForm()
    {
        Text = LauncherProfile.DisplayName;
        ClientSize = new Size(660, 250);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        Controls.Add(new Label { AutoSize = true, Text = "Имя пользователя:", Left = 24, Top = 25 });
        Controls.Add(new Label { AutoSize = true, Text = "Пароль:", Left = 24, Top = 59 });
        Controls.Add(new Label { AutoSize = true, Text = "Папка установки:", Left = 24, Top = 105 });
        Controls.AddRange(new Control[] { _username, _password, _login, _register, _installRoot, _browse, _status, _play });

        _installRoot.Text = _settingsStore.Load().InstallRoot;
        _installRoot.ReadOnly = LauncherProfile.IsDevelopment;
        _browse.Visible = !LauncherProfile.IsDevelopment;
        _login.Click += (_, _) => AuthenticateAsync(register: false);
        _register.Click += (_, _) => AuthenticateAsync(register: true);
        _browse.Click += OnBrowse;
        _play.Click += OnPlayAsync;
    }

    private async void AuthenticateAsync(bool register)
    {
        SetBusy(true);
        StartOperation();
        try
        {
            using var http = CreateHttpClient();
            var manifestClient = new ManifestClient(http);
            var manifestSource = await LoadManifestAsync(manifestClient, _operation!.Token);
            var manifest = manifestSource.Manifest;
            var auth = new AuthApiClient(http);
            _status.Text = register ? "Создаю аккаунт RE:volution…" : "Выполняю вход в RE:volution…";
            _session = register
                ? await auth.RegisterAsync(manifest.Auth, manifest.DevelopmentOnly, _username.Text, _password.Text, _operation.Token)
                : await auth.LoginAsync(manifest.Auth, manifest.DevelopmentOnly, _username.Text, _password.Text, _operation.Token);
            _password.Clear();
            _username.Text = _session.Username;
            _status.Text = register
                ? $"Аккаунт {_session.Username} создан. Вход выполнен."
                : $"Вход выполнен: {_session.Username}";
            _play.Enabled = true;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Операция отменена.";
        }
        catch (Exception ex)
        {
            _session = null;
            _play.Enabled = false;
            _status.Text = register ? "Ошибка регистрации." : "Ошибка входа.";
            MessageBox.Show(this, ex.Message, "RE:volution Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnPlayAsync(object? sender, EventArgs e)
    {
        if (_session is null)
        {
            _status.Text = "Сначала войдите в RE:volution.";
            return;
        }

        SetBusy(true);
        StartOperation();
        try
        {
            var installRoot = LauncherSettingsStore.ValidateInstallRoot(_installRoot.Text);
            await _settingsStore.SaveAsync(installRoot, _operation!.Token);

            using var http = CreateHttpClient();
            var paths = new LauncherPaths(installRoot);
            var progress = new Progress<string>(message => _status.Text = message);
            var manifestClient = new ManifestClient(http);
            var manifestSource = await LoadManifestAsync(manifestClient, _operation.Token);
            var manifest = manifestSource.Manifest;
            var minecraft = new MinecraftClientService(http, paths);
            var bootstrap = new ClientBootstrapService(
                paths,
                new JavaRuntimeService(http, paths),
                minecraft,
                new BundledPayloadService(paths),
                new ManagedFileService(http, paths, manifestSource.AllowLocalManagedFiles),
                new ClientConfigService(paths));

            var result = await bootstrap.PrepareAsync(manifest, progress, _operation.Token);
            await minecraft.LaunchAsync(result.ForgeVersionName, result.Manifest.Minecraft, result.Java, _session, progress, _operation.Token);
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
            SetBusy(false);
        }
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Выберите папку установки RE:volution Client",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_installRoot.Text) ? _installRoot.Text : string.Empty,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _installRoot.Text = dialog.SelectedPath;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseProxy = true,
            AutomaticDecompression = DecompressionMethods.All
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
    }

    private void StartOperation()
    {
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
    }

    private void SetBusy(bool busy)
    {
        _login.Enabled = !busy;
        _register.Enabled = !busy;
        _browse.Enabled = !busy;
        _username.Enabled = !busy;
        _password.Enabled = !busy;
        _installRoot.Enabled = !busy;
        _play.Enabled = !busy && _session is not null;
    }

    private static async Task<ManifestSource> LoadManifestAsync(ManifestClient manifestClient, CancellationToken cancellationToken)
    {
        var manifestFile = Environment.GetEnvironmentVariable(ManifestFileEnvironmentVariable);
        var manifestUrl = Environment.GetEnvironmentVariable(ManifestUrlEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(manifestFile) && !string.IsNullOrWhiteSpace(manifestUrl))
            throw new InvalidDataException($"Use only one of {ManifestFileEnvironmentVariable} or {ManifestUrlEnvironmentVariable}.");

        if (!string.IsNullOrWhiteSpace(manifestFile))
        {
            var manifest = await manifestClient.GetLocalAsync(manifestFile, cancellationToken);
            if (!manifest.DevelopmentOnly)
                throw new InvalidDataException("A local manifest override must set developmentOnly=true.");
            LauncherProfile.ValidateManifest(manifest);
            return new ManifestSource(manifest, AllowLocalManagedFiles: true);
        }

        if (string.IsNullOrWhiteSpace(manifestUrl))
            manifestUrl = DefaultManifestUrl;
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri))
            throw new InvalidDataException($"{ManifestUrlEnvironmentVariable} is not a valid absolute URL.");
        if (LauncherProfile.IsDevelopment)
        {
            var builder = new UriBuilder(uri);
            builder.Query = string.IsNullOrEmpty(builder.Query)
                ? "dev_refresh=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                : builder.Query.TrimStart('?') + "&dev_refresh=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            uri = builder.Uri;
        }
        var remoteManifest = await manifestClient.GetAsync(uri, cancellationToken);
        LauncherProfile.ValidateManifest(remoteManifest);
        return new ManifestSource(remoteManifest, AllowLocalManagedFiles: false);
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

    private sealed record ManifestSource(ClientManifest Manifest, bool AllowLocalManagedFiles);
}
