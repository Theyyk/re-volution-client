# RE:volution Auth API v1

The launcher-facing authentication API is intentionally separate from Microsoft/Mojang authentication.

## Current implementation

The repository now contains a development Auth API in `auth/`.

- Default bind: `http://127.0.0.1:25567`.
- Override bind with `REVOLUTION_AUTH_URL`.
- Default database: `%LOCALAPPDATA%\REvolutionClient\auth\auth.db` on Windows.
- Override database path with `REVOLUTION_AUTH_DB`.
- SQLite persists accounts and sessions.
- Passwords are stored with ASP.NET Core `PasswordHasher` (PBKDF2); plaintext and reversible password storage are forbidden.
- Access tokens are 256-bit random opaque values. Only SHA-256 token hashes are stored in the database.
- Sessions expire after 8 hours and are revocable.
- Login/register/session/logout endpoints are rate-limited.
- Production transport MUST use HTTPS. Plain HTTP is allowed only for loopback development.

Run locally from the repository root:

```powershell
dotnet run --project .\auth\REvolutionClient.Auth.csproj -c Release
```

## User flow

- Existing user: username + password -> login -> session.
- New user: explicit registration form -> account creation -> session returned immediately.
- An unknown username on the login endpoint MUST NOT auto-register.
- The launcher never forwards the password to Minecraft or the game server.
- Usernames are case-insensitive for uniqueness and login, while the originally registered casing is preserved for display.
- Usernames are limited to 3-16 ASCII letters/digits/underscore to remain compatible with the Minecraft 1.12.2 client identity path.

## Endpoints

### `POST /api/v1/auth/register`

Request:

```json
{
  "username": "Denis",
  "password": "example-password"
}
```

Creates a unique account. Successful registration also creates a session, so the launcher is signed in immediately. A duplicate username returns `409`.

### `POST /api/v1/auth/login`

Uses the same request shape. An unknown username or wrong password returns `401`. Registration is never implicit.

Successful response:

```json
{
  "username": "Denis",
  "playerUuid": "stable-account-uuid",
  "accessToken": "opaque-short-lived-token",
  "expiresAtUtc": "2026-10-19T18:00:00Z"
}
```

`playerUuid` is generated once at registration and remains stable for the account.

### `GET /api/v1/auth/session`

Send `Authorization: Bearer <accessToken>`. Returns the username, stable player UUID, and session expiry when the token is valid. Returns `401` for missing, expired, revoked, or invalid tokens.

### `POST /api/v1/auth/logout`

Send `Authorization: Bearer <accessToken>`. Revokes the current session and returns `204`.

## Security boundary

The access token is a session credential, not a password. It must never be logged or placed on the Minecraft process command line. CORS is not enabled. Request bodies are bounded and Kestrel does not expose its server header.

The launcher currently starts Minecraft with the authenticated RE:volution username and deliberately does not put the token on the process command line. The next security step is a client-mod/server handshake that proves the RE:volution session before gameplay access is granted. Until that handshake exists, Auth API + launcher login is functional account authentication but is not yet the final game-server admission boundary.
