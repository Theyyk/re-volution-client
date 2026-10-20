using Microsoft.Data.Sqlite;
using REvolutionClient.Auth.Model;

namespace REvolutionClient.Auth.Infrastructure;

internal sealed class AuthRepository
{
    private readonly string _connectionString;

    public AuthRepository(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Auth database directory is invalid.");
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL,
                normalized_username TEXT NOT NULL UNIQUE,
                player_uuid TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                created_utc INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sessions (
                token_hash TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL,
                created_utc INTEGER NOT NULL,
                expires_utc INTEGER NOT NULL,
                revoked_utc INTEGER NULL,
                FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_sessions_user_id ON sessions(user_id);
            CREATE INDEX IF NOT EXISTS ix_sessions_expires_utc ON sessions(expires_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AuthIdentity?> FindUserAsync(string normalizedUsername, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, username, player_uuid, password_hash FROM users WHERE normalized_username = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", normalizedUsername);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AuthIdentity(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    public async Task<AuthIdentity?> FindUserByIdAsync(long userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, username, player_uuid, password_hash FROM users WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AuthIdentity(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    public async Task<AuthIdentity?> CreateUserAsync(string username, string normalizedUsername, string playerUuid, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users(username, normalized_username, player_uuid, password_hash, created_utc)
            VALUES($username, $normalized, $uuid, $hash, $created);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$normalized", normalizedUsername);
        command.Parameters.AddWithValue("$uuid", playerUuid);
        command.Parameters.AddWithValue("$hash", passwordHash);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            var id = Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
            return new AuthIdentity(id, username, playerUuid, passwordHash);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return null;
        }
    }

    public async Task UpdatePasswordHashAsync(long userId, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE users SET password_hash = $hash WHERE id = $id;";
        command.Parameters.AddWithValue("$hash", passwordHash);
        command.Parameters.AddWithValue("$id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateSessionAsync(long userId, string tokenHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = (SqliteTransaction)transaction;
            cleanup.CommandText = "DELETE FROM sessions WHERE expires_utc < $cutoff OR revoked_utc IS NOT NULL;";
            cleanup.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO sessions(token_hash, user_id, created_utc, expires_utc, revoked_utc)
                VALUES($token, $user, $created, $expires, NULL);
                """;
            insert.Parameters.AddWithValue("$token", tokenHash);
            insert.Parameters.AddWithValue("$user", userId);
            insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            insert.Parameters.AddWithValue("$expires", expiresAtUtc.ToUnixTimeSeconds());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AuthenticatedSession?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id, u.username, u.player_uuid, s.expires_utc
            FROM sessions s
            JOIN users u ON u.id = s.user_id
            WHERE s.token_hash = $token
              AND s.revoked_utc IS NULL
              AND s.expires_utc > $now
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$token", tokenHash);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AuthenticatedSession(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)));
    }

    public async Task RevokeSessionAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET revoked_utc = $revoked WHERE token_hash = $token AND revoked_utc IS NULL;";
        command.Parameters.AddWithValue("$revoked", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$token", tokenHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }
}
