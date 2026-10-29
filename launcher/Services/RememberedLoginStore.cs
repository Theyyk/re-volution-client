using System.Security.Cryptography;
using System.Text.Json;
using REvolutionClient.Launcher.Infrastructure;

namespace REvolutionClient.Launcher.Services;

internal sealed class RememberedLoginStore
{
    private readonly string _file;

    public RememberedLoginStore(string? settingsRoot = null)
    {
        _file = Path.Combine(settingsRoot ?? LauncherProfile.SettingsRoot, "remembered-login.bin");
    }

    public RememberedLogin? Load()
    {
        if (!File.Exists(_file)) return null;
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(_file), null, DataProtectionScope.CurrentUser);
            var login = JsonSerializer.Deserialize<RememberedLogin>(plain);
            return login is not null && !string.IsNullOrWhiteSpace(login.Username) && !string.IsNullOrEmpty(login.Password)
                ? login : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Missing, damaged or another Windows user's data must not prevent manual login.
            return null;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Save(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Username and password are required.");
        var plain = JsonSerializer.SerializeToUtf8Bytes(new RememberedLogin(username, password));
        var temp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, _file, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void Delete()
    {
        File.Delete(_file);
    }
}

internal sealed record RememberedLogin(string Username, string Password);
