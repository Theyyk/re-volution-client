using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using REvolutionClient.Auth.Infrastructure;
using REvolutionClient.Auth.Model;

namespace REvolutionClient.Auth.Services;

internal sealed class AuthService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private readonly AuthRepository _repository;
    private readonly PasswordHasher<AuthIdentity> _passwordHasher = new();

    public AuthService(AuthRepository repository) => _repository = repository;

    public async Task<SessionResponse> RegisterAsync(CredentialRequest request, CancellationToken cancellationToken)
    {
        var normalized = CredentialPolicy.ValidateAndNormalizeUsername(request.Username);
        CredentialPolicy.ValidatePassword(request.Password);
        var username = request.Username.Trim();
        var provisional = new AuthIdentity(0, username, Guid.NewGuid().ToString("N"), string.Empty);
        var passwordHash = _passwordHasher.HashPassword(provisional, request.Password);
        var created = await _repository.CreateUserAsync(username, normalized, provisional.PlayerUuid, passwordHash, cancellationToken);
        if (created is null) throw new UsernameTakenException();
        return await CreateSessionAsync(created, cancellationToken);
    }

    public async Task<SessionResponse> LoginAsync(CredentialRequest request, CancellationToken cancellationToken)
    {
        var normalized = CredentialPolicy.ValidateAndNormalizeUsername(request.Username);
        CredentialPolicy.ValidatePassword(request.Password);
        var user = await _repository.FindUserAsync(normalized, cancellationToken);
        if (user is null) throw new InvalidCredentialsException();

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed) throw new InvalidCredentialsException();
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var replacement = _passwordHasher.HashPassword(user, request.Password);
            await _repository.UpdatePasswordHashAsync(user.UserId, replacement, cancellationToken);
        }

        return await CreateSessionAsync(user, cancellationToken);
    }

    public async Task<AuthenticatedSession?> GetSessionAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!IsTokenShapeValid(accessToken)) return null;
        return await _repository.GetSessionAsync(HashToken(accessToken), cancellationToken);
    }

    public async Task LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!IsTokenShapeValid(accessToken)) return;
        await _repository.RevokeSessionAsync(HashToken(accessToken), cancellationToken);
    }

    private async Task<SessionResponse> CreateSessionAsync(AuthIdentity user, CancellationToken cancellationToken)
    {
        var token = CreateToken();
        var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);
        await _repository.CreateSessionAsync(user.UserId, HashToken(token), expiresAt, cancellationToken);
        return new SessionResponse
        {
            Username = user.Username,
            PlayerUuid = user.PlayerUuid,
            AccessToken = token,
            ExpiresAtUtc = expiresAt
        };
    }

    private static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static bool IsTokenShapeValid(string token) => token.Length is >= 40 and <= 128;
}

internal sealed class UsernameTakenException : Exception
{
}

internal sealed class InvalidCredentialsException : Exception
{
}
