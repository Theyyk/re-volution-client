using System.Reflection;
using System.Text.Json;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class ManifestClient
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly ManifestSignatureVerifier _signatureVerifier;

    public ManifestClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _signatureVerifier = ManifestSignatureVerifier.FromEmbeddedKeys();
    }

    public async Task<ClientManifest> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Manifest endpoint must use HTTPS.");

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumManifestBytes)
            throw new InvalidDataException("Manifest is too large.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bytes = await ReadBoundedAsync(source, cancellationToken);
        return DeserializeRemote(bytes);
    }

    public async Task<ClientManifest> GetLocalAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Local manifest path is empty.");

        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("Local development manifest was not found.", fullPath);
        if (info.Length > MaximumManifestBytes)
            throw new InvalidDataException("Manifest is too large.");

        await using var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, true);
        var bytes = await ReadBoundedAsync(source, cancellationToken);
        return DeserializeManifest(bytes);
    }

    public ClientManifest GetEmbedded(string resourceName)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded manifest resource '{resourceName}' is missing.");
        var bytes = ReadBoundedAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        return DeserializeManifest(bytes);
    }

    private ClientManifest DeserializeRemote(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Manifest document must be a JSON object.");

        var looksSigned = root.TryGetProperty("payload", out _) || root.TryGetProperty("signature", out _) || root.TryGetProperty("keyId", out _);
        if (looksSigned)
        {
            var envelope = JsonSerializer.Deserialize<SignedManifestEnvelope>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Signed manifest envelope is invalid.");
            var payload = _signatureVerifier.Verify(envelope);
            return DeserializeManifest(payload);
        }

        var manifest = DeserializeManifest(bytes);
        if (!manifest.DevelopmentOnly)
            throw new InvalidDataException("Production manifests must use a trusted signed envelope.");
        return manifest;
    }

    private static ClientManifest DeserializeManifest(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<ClientManifest>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Manifest is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Manifest JSON is invalid.", ex);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
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
        return limited.ToArray();
    }
}
