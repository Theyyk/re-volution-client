namespace REvolutionClient.Launcher.Model;

internal sealed class SignedManifestEnvelope
{
    public int SchemaVersion { get; init; } = 1;
    public string Algorithm { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
}

internal sealed class TrustedManifestKeySet
{
    public List<TrustedManifestKey> Keys { get; init; } = new();
}

internal sealed class TrustedManifestKey
{
    public string KeyId { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public string PublicKeyPem { get; init; } = string.Empty;
}
