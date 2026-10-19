using System.Text.Json;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class ManifestClient
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private readonly HttpClient _httpClient;

    public ManifestClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<ClientManifest> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Manifest endpoint must use HTTPS.");

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumManifestBytes)
            throw new InvalidDataException("Manifest is too large.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new MemoryStream(MaximumManifestBytes);
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaximumManifestBytes) throw new InvalidDataException("Manifest is too large.");
            await limited.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        limited.Position = 0;
        var manifest = await JsonSerializer.DeserializeAsync<ClientManifest>(limited, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken);
        return manifest ?? throw new InvalidDataException("Manifest is empty or invalid.");
    }
}
