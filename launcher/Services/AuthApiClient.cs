using System.Net.Http.Json;
using System.Text.Json;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class AuthApiClient
{
    private const int MaximumResponseBytes = 64 * 1024;
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient) => _httpClient = httpClient;

    public Task<AuthSession> LoginAsync(AuthManifest manifest, bool developmentOnly, string username, string password, CancellationToken cancellationToken) =>
        AuthenticateAsync(manifest, developmentOnly, "login", username, password, cancellationToken);

    public Task<AuthSession> RegisterAsync(AuthManifest manifest, bool developmentOnly, string username, string password, CancellationToken cancellationToken) =>
        AuthenticateAsync(manifest, developmentOnly, "register", username, password, cancellationToken);

    private async Task<AuthSession> AuthenticateAsync(
        AuthManifest manifest,
        bool developmentOnly,
        string action,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        ValidateCredentials(username, password);
        var baseUri = ValidateBaseUri(manifest.BaseUrl, developmentOnly);
        var endpoint = new Uri(baseUri, $"api/v1/auth/{action}");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new AuthRequest { Username = username.Trim(), Password = password })
        };
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Неверное имя пользователя или пароль.");
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new InvalidOperationException("Это имя пользователя уже занято.");
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Слишком много попыток. Подождите минуту и попробуйте снова.");
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw new InvalidOperationException("Проверьте имя пользователя и пароль.");
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new InvalidDataException("Auth API returned an oversized response.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaximumResponseBytes) throw new InvalidDataException("Auth API returned an oversized response.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        var session = await JsonSerializer.DeserializeAsync<AuthSession>(buffer, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? throw new InvalidDataException("Auth API returned an invalid session.");

        if (string.IsNullOrWhiteSpace(session.Username) ||
            string.IsNullOrWhiteSpace(session.PlayerUuid) ||
            string.IsNullOrWhiteSpace(session.AccessToken) ||
            session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Auth API returned an incomplete or expired session.");

        return session;
    }

    private static Uri ValidateBaseUri(string value, bool developmentOnly)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new InvalidDataException("Auth API URL is invalid.");
        if (uri.Scheme == Uri.UriSchemeHttps) return EnsureTrailingSlash(uri);
        if (developmentOnly && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) return EnsureTrailingSlash(uri);
        throw new InvalidDataException("Auth API must use HTTPS. HTTP is allowed only for loopback development builds.");
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/");

    private static void ValidateCredentials(string username, string password)
    {
        var name = username.Trim();
        if (name.Length is < 3 or > 16) throw new InvalidDataException("Имя пользователя должно содержать от 3 до 16 символов.");
        if (!name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            throw new InvalidDataException("Имя пользователя может содержать только латинские буквы, цифры и '_'.");
        if (password.Length is < 8 or > 128) throw new InvalidDataException("Пароль должен содержать от 8 до 128 символов.");
    }
}
