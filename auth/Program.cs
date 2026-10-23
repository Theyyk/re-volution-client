using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using REvolutionClient.Auth.Infrastructure;
using REvolutionClient.Auth.Model;
using REvolutionClient.Auth.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 16 * 1024;
    options.AddServerHeader = false;
});
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("REVOLUTION_AUTH_URL") ?? "http://127.0.0.1:25567");

var databasePath = ResolveDatabasePath();
builder.Services.AddSingleton(new AuthRepository(databasePath));
builder.Services.AddSingleton<AuthService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();
var repository = app.Services.GetRequiredService<AuthRepository>();
await repository.InitializeAsync(CancellationToken.None);

app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Enabled only by local Docker DEV configuration. No channel endpoint in normal PROD configuration.
var devChannelDirectory = Environment.GetEnvironmentVariable("REVOLUTION_DEV_CHANNEL_DIRECTORY");
if (!string.IsNullOrWhiteSpace(devChannelDirectory))
{
    app.MapGet("/api/v1/dev/channel", async (HttpContext context, CancellationToken token) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        var active = Path.Combine(devChannelDirectory, "active.json");
        var updating = Path.Combine(devChannelDirectory, "updating");
        if (File.Exists(updating) || !File.Exists(active))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        try
        {
            var json = await File.ReadAllTextAsync(active, token);
            if (File.Exists(updating)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Results.Text(json, "application/json");
        }
        catch (IOException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    });
}

app.MapPost("/api/v1/auth/register", async (CredentialRequest request, AuthService auth, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await auth.RegisterAsync(request, cancellationToken));
    }
    catch (CredentialValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (UsernameTakenException)
    {
        return Results.Conflict(new { error = "username_taken" });
    }
}).RequireRateLimiting("auth");

app.MapPost("/api/v1/auth/login", async (CredentialRequest request, AuthService auth, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await auth.LoginAsync(request, cancellationToken));
    }
    catch (CredentialValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidCredentialsException)
    {
        return Results.Unauthorized();
    }
}).RequireRateLimiting("auth");

app.MapGet("/api/v1/auth/session", async (HttpContext context, AuthService auth, CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context.Request);
    if (token is null) return Results.Unauthorized();
    var session = await auth.GetSessionAsync(token, cancellationToken);
    if (session is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        session.Username,
        session.PlayerUuid,
        ExpiresAtUtc = session.ExpiresAtUtc
    });
}).RequireRateLimiting("auth");

app.MapPost("/api/v1/auth/logout", async (HttpContext context, AuthService auth, CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context.Request);
    if (token is null) return Results.Unauthorized();
    var session = await auth.GetSessionAsync(token, cancellationToken);
    if (session is null) return Results.Unauthorized();
    await auth.LogoutAsync(token, cancellationToken);
    return Results.NoContent();
}).RequireRateLimiting("auth");

await app.RunAsync();

static string? GetBearerToken(HttpRequest request)
{
    var header = request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
    var token = header[prefix.Length..].Trim();
    return token.Length == 0 ? null : token;
}

static string ResolveDatabasePath()
{
    var configured = Environment.GetEnvironmentVariable("REVOLUTION_AUTH_DB");
    if (!string.IsNullOrWhiteSpace(configured)) return configured;
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(local)) local = AppContext.BaseDirectory;
    return Path.Combine(local, "REvolutionClient", "auth", "auth.db");
}
