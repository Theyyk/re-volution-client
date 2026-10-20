namespace REvolutionClient.Auth.Model;

internal sealed class CredentialRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

internal sealed record AuthIdentity(long UserId, string Username, string PlayerUuid, string PasswordHash);

internal sealed record AuthenticatedSession(long UserId, string Username, string PlayerUuid, DateTimeOffset ExpiresAtUtc);

internal sealed class SessionResponse
{
    public string Username { get; init; } = string.Empty;
    public string PlayerUuid { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
