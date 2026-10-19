# RE:volution Auth API v1

The launcher-facing authentication API is intentionally separate from Microsoft/Mojang authentication.

## User flow

- Existing user: username + password -> login -> session.
- New user: explicit registration form -> account creation -> session returned immediately.
- An unknown username on the login endpoint MUST NOT auto-register.
- The launcher never forwards the password to Minecraft or the game server.

## Transport

Production endpoints MUST use HTTPS. Plain HTTP is allowed only for loopback development builds.

## Endpoints

### `POST /api/v1/auth/register`

Request:

```json
{
  "username": "Denis",
  "password": "example-password"
}
```

Creates a unique account. Passwords MUST be stored only using a password KDF/hash suitable for password storage; plaintext or reversible storage is forbidden. A successful registration returns the same session shape as login so the launcher is signed in immediately.

### `POST /api/v1/auth/login`

Uses the same request shape. An unknown username or wrong password returns `401`. Registration is never implicit.

Successful response:

```json
{
  "username": "Denis",
  "playerUuid": "stable-account-uuid",
  "accessToken": "opaque-short-lived-token",
  "expiresAtUtc": "2026-10-19T12:00:00Z"
}
```

The access token is a session credential, not a password. It must be short-lived and revocable. The API should rate-limit login and registration attempts.

## Minecraft integration boundary

The launcher currently starts Minecraft with the authenticated RE:volution username and deliberately does not put the access token on the process command line. A later server/client handshake must prove the RE:volution session token before gameplay access is granted. Until that handshake and Auth API implementation exist, this is an authentication UX/client contract foundation, not a completed security boundary.
