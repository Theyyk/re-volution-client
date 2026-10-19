namespace REvolutionClient.Auth.Services;

internal static class CredentialPolicy
{
    public static string ValidateAndNormalizeUsername(string username)
    {
        var value = username.Trim();
        if (value.Length is < 3 or > 16)
            throw new CredentialValidationException("Имя пользователя должно содержать от 3 до 16 символов.");
        if (!value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            throw new CredentialValidationException("Имя пользователя может содержать только латинские буквы, цифры и '_'.");
        return value.ToUpperInvariant();
    }

    public static void ValidatePassword(string password)
    {
        if (password.Length is < 8 or > 128)
            throw new CredentialValidationException("Пароль должен содержать от 8 до 128 символов.");
    }
}

internal sealed class CredentialValidationException : Exception
{
    public CredentialValidationException(string message) : base(message) { }
}
