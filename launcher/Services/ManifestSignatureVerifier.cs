using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using REvolutionClient.Launcher.Model;

namespace REvolutionClient.Launcher.Services;

internal sealed class ManifestSignatureVerifier
{
    public const string Algorithm = "ecdsa-p256-sha256";
    private const string TrustedKeysResourceName = "REvolutionClient.Manifest.trusted-manifest-keys.json";
    private readonly IReadOnlyDictionary<string, TrustedManifestKey> _keys;

    private ManifestSignatureVerifier(IReadOnlyDictionary<string, TrustedManifestKey> keys) => _keys = keys;

    public static ManifestSignatureVerifier FromEmbeddedKeys()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TrustedKeysResourceName)
            ?? throw new InvalidDataException("Trusted manifest key set is missing from the launcher build.");
        var set = JsonSerializer.Deserialize<TrustedManifestKeySet>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Trusted manifest key set is invalid.");

        var keys = new Dictionary<string, TrustedManifestKey>(StringComparer.Ordinal);
        foreach (var key in set.Keys)
        {
            if (string.IsNullOrWhiteSpace(key.KeyId) || string.IsNullOrWhiteSpace(key.PublicKeyPem))
                throw new InvalidDataException("Trusted manifest key metadata is incomplete.");
            if (!string.Equals(key.Algorithm, Algorithm, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported trusted manifest key algorithm: {key.Algorithm}");
            if (!keys.TryAdd(key.KeyId, key))
                throw new InvalidDataException($"Duplicate trusted manifest key id: {key.KeyId}");
        }

        return new ManifestSignatureVerifier(keys);
    }

    public byte[] Verify(SignedManifestEnvelope envelope)
    {
        if (envelope.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported signed manifest envelope version.");
        if (!string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported manifest signature algorithm.");
        if (string.IsNullOrWhiteSpace(envelope.KeyId) || !_keys.TryGetValue(envelope.KeyId, out var trustedKey))
            throw new InvalidDataException("Manifest was signed by an unknown key.");

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Convert.FromBase64String(envelope.Payload);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Signed manifest payload or signature is not valid Base64.", ex);
        }

        if (payload.Length == 0)
            throw new InvalidDataException("Signed manifest payload is empty.");
        if (signature.Length != 64)
            throw new InvalidDataException("Manifest ECDSA signature has an invalid length.");

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(trustedKey.PublicKeyPem);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException($"Trusted manifest key '{envelope.KeyId}' is invalid.", ex);
        }

        var valid = ecdsa.VerifyData(
            payload,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (!valid)
            throw new InvalidDataException("Manifest signature verification failed.");

        return payload;
    }
}
