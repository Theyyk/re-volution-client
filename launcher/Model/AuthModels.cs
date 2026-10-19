namespace REvolutionClient.Launcher.Model;

internal sealed class AuthRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

internal sealed class AuthSession
{
    public string Username { get; init; } = string.Empty;
    public string PlayerUuid { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
