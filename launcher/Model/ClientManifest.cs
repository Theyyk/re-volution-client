namespace REvolutionClient.Launcher.Model;

internal sealed class ClientManifest
{
    public string ProductVersion { get; init; } = string.Empty;
    public bool DevelopmentOnly { get; init; }
    public JavaRuntimeManifest Java { get; init; } = new();
    public MinecraftManifest Minecraft { get; init; } = new();
    public ServerManifest Server { get; init; } = new();
    public List<ManagedFileManifest> Files { get; init; } = new();
}

internal sealed class JavaRuntimeManifest
{
    public int MajorVersion { get; init; } = 8;
    public string Provider { get; init; } = "adoptium";
    public string DownloadUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Size { get; init; }
}

internal sealed class MinecraftManifest
{
    public string Version { get; init; } = "1.12.2";
    public string ForgeVersion { get; init; } = "14.23.5.2859";
    public int MemoryMb { get; init; } = 4096;
}

internal sealed class ServerManifest
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 25565;
}

internal sealed class ManagedFileManifest
{
    public string Path { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Size { get; init; }
}
