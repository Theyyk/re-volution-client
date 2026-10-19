using System.Security.Cryptography;
using System.Text.Json;

const string Algorithm = "ecdsa-p256-sha256";

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

try
{
    return args[0] switch
    {
        "generate" => Generate(args.Skip(1).ToArray()),
        "sign" => Sign(args.Skip(1).ToArray()),
        "verify" => Verify(args.Skip(1).ToArray()),
        _ => throw new ArgumentException($"Unknown command: {args[0]}")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Generate(string[] args)
{
    var privatePath = RequiredOption(args, "--private");
    var publicPath = RequiredOption(args, "--public");
    EnsureParentDirectory(privatePath);
    EnsureParentDirectory(publicPath);
    RefuseOverwrite(privatePath);
    RefuseOverwrite(publicPath);

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());

    Console.WriteLine($"Private key: {Path.GetFullPath(privatePath)}");
    Console.WriteLine($"Public key:  {Path.GetFullPath(publicPath)}");
    Console.WriteLine("Keep the private key outside the repository and back it up securely.");
    return 0;
}

static int Sign(string[] args)
{
    var manifestPath = RequiredOption(args, "--manifest");
    var privatePath = RequiredOption(args, "--private");
    var keyId = RequiredOption(args, "--key-id");
    var outputPath = RequiredOption(args, "--output");

    if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("--key-id must not be empty.");
    var payload = File.ReadAllBytes(manifestPath);
    if (payload.Length == 0) throw new InvalidDataException("Manifest payload is empty.");

    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(privatePath));
    var signature = key.SignData(
        payload,
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    var envelope = new SignedEnvelope
    {
        SchemaVersion = 1,
        Algorithm = Algorithm,
        KeyId = keyId,
        Payload = Convert.ToBase64String(payload),
        Signature = Convert.ToBase64String(signature)
    };

    EnsureParentDirectory(outputPath);
    var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
    File.WriteAllText(outputPath, json + Environment.NewLine);
    Console.WriteLine($"Signed manifest: {Path.GetFullPath(outputPath)}");
    return 0;
}

static int Verify(string[] args)
{
    var envelopePath = RequiredOption(args, "--envelope");
    var publicPath = RequiredOption(args, "--public");
    var expectedKeyId = Option(args, "--key-id");

    var envelope = JsonSerializer.Deserialize<SignedEnvelope>(
        File.ReadAllBytes(envelopePath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Signed manifest envelope is invalid.");

    if (envelope.SchemaVersion != 1) throw new InvalidDataException("Unsupported envelope version.");
    if (!string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal))
        throw new InvalidDataException("Unsupported signature algorithm.");
    if (!string.IsNullOrWhiteSpace(expectedKeyId) && !string.Equals(envelope.KeyId, expectedKeyId, StringComparison.Ordinal))
        throw new InvalidDataException("Envelope key id does not match the expected key id.");

    var payload = Convert.FromBase64String(envelope.Payload);
    var signature = Convert.FromBase64String(envelope.Signature);
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(publicPath));
    if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        throw new InvalidDataException("Signature verification failed.");

    Console.WriteLine($"Signature valid. keyId={envelope.KeyId}, payloadBytes={payload.Length}");
    return 0;
}

static string RequiredOption(string[] args, string name) =>
    Option(args, name) ?? throw new ArgumentException($"Missing required option {name}.");

static string? Option(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], name, StringComparison.Ordinal)) continue;
        if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {name}.");
        return args[i + 1];
    }
    return null;
}

static void EnsureParentDirectory(string path)
{
    var full = Path.GetFullPath(path);
    var parent = Path.GetDirectoryName(full);
    if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
}

static void RefuseOverwrite(string path)
{
    if (File.Exists(path)) throw new IOException($"Refusing to overwrite existing key file: {Path.GetFullPath(path)}");
}

static void PrintUsage()
{
    Console.WriteLine("RE:volution manifest signing tool");
    Console.WriteLine("  generate --private <private.pem> --public <public.pem>");
    Console.WriteLine("  sign --manifest <manifest.json> --private <private.pem> --key-id <id> --output <manifest.signed.json>");
    Console.WriteLine("  verify --envelope <manifest.signed.json> --public <public.pem> [--key-id <id>]");
}

internal sealed class SignedEnvelope
{
    public int SchemaVersion { get; init; }
    public string Algorithm { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
}
