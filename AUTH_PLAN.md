ABOUTME: Authentication plan for Android app and .NET API.
ABOUTME: Single source of truth for auth flow, contracts, and checklists.

# Auth Plan

## Context
- Android client hits a single ASP.NET Core API entry point.
- Identity provider is Google; backend never handles passwords.
- API issues and validates first-party JWTs for downstream authorization.

## Goals
- End-to-end Google Sign-In that lands users in the API with a JWT.
- Minimal, well-documented endpoints so mobile devs can self-serve.
- Operational guardrails (observability, rotations, testing) defined up front.

## Non-goals
- Multiple identity providers, social linking, or password auth.
- Web client, admin portal, or API key issuance.

## Architecture Overview
- **Android app** uses Google Play Services SDK to fetch an ID token scoped to the API’s web client ID.
- **Auth controller** exposes `/auth/google`, `/auth/refresh`, `/auth/logout` (optional) built with ASP.NET Core Minimal APIs.
- **Token service** validates Google ID tokens via `GoogleJsonWebSignature` and issues API-owned JWTs signed with an asymmetric key (RSA-256).
- **Persistence** uses Postgres (or existing relational DB) tables `users` and `refresh_tokens`.
- **Key management** keeps signing key material in Azure Key Vault; API caches active keys and rotates via configuration.

## Flow Narrative
1. Android user taps “Sign in with Google”; Google SDK returns an ID token for the configured web client ID.
2. Client sends `POST /auth/google` with `{ "idToken": "<GOOGLE_ID_TOKEN>" }` over HTTPS and includes device metadata headers (`X-App-Version`, `X-Device-Model`) for telemetry.
3. API validates request schema, checks rate limits per device, then verifies the ID token (audience, issuer, expiry, signature, email_verified).
4. API loads or upserts a user by `google_sub`. Any profile updates (name, photo URL) are stored if changed.
5. Token service issues:
   - Access JWT (15 min TTL) with claims `sub`, `email`, `name`, `roles` (default `["user"]`), `iat`, `exp`.
   - Optional refresh token (30 days) stored hashed with rotation on every refresh.
6. Response returns tokens + user profile. Android stores tokens in EncryptedSharedPreferences backed by Android Keystore.
7. All protected endpoints require `Authorization: Bearer <JWT>`. Middleware validates signature, expiry, and revocation status (if global logout implemented).
8. Refresh flow exchanges a valid refresh token for a new JWT pair, invalidating the previous refresh token record.

## Sequence (textual)
```
Android ──(Google sign-in)──> Google
Android <─(ID token)──────── Google
Android ──POST /auth/google (idToken)──────> API
API ──(verify token)────> Google certs cache (jwks)
API <─(validation ok)────
API ──(upsert user, persist refresh token)──> DB
API <─(record ids)──────────────────────────
API ──(response JWT + refresh)─────────────> Android
Android ──(Bearer JWT)─────────────────────> Protected endpoints
```

## API Contracts

### POST `/auth/google`
Request body:
```json
{ "idToken": "GOOGLE_ID_TOKEN" }
```
Success `200`:
```json
{
  "accessToken": "JWT_STRING",
  "expiresInSeconds": 900,
  "refreshToken": "REFRESH_TOKEN_OPTIONAL",
  "user": {
    "id": "UUID",
    "email": "user@example.com",
    "name": "Display Name",
    "avatarUrl": "https://photos.googleusercontent.com/... (optional)"
  }
}
```
Errors:
- `400` invalid/missing payload.
- `401` token verification failed (signature, audience, expired, email not verified).
- `429` rate limit, `5xx` unexpected.

### POST `/auth/refresh`
```json
{ "refreshToken": "REFRESH_TOKEN" }
```
Responses mirror `/auth/google` but returned refresh token replaces the caller’s stored value. Any mismatch or revoked token returns `401`.

### POST `/auth/logout`
Body accepts either the refresh token or device identifier so the backend can revoke outstanding refresh tokens. Always returns `204`. No refresh token means server simply logs the logout attempt.

### Protected endpoints
- Require `Authorization: Bearer <JWT>` header.
- Middleware attaches `UserId` claim and optional `Role` claims for downstream handlers.

## Token Policy
- **Access token TTL**: 15 minutes.
- **Refresh token TTL**: 30 days, rotate on each refresh, store bcrypt/SHA256 hash only.
- **Signing**: RSA-256; keep `kid` in JWT header so future rotations can coexist.
- **Clock skew**: allow ±60 seconds when validating `exp`/`nbf`.
- **Device storage**: Android Keystore-backed encrypted prefs; never log token bodies.

## Data Model
`users`
- `id` UUID PK.
- `google_sub` unique text.
- `email`, `name`, `avatar_url`.
- `created_at`, `updated_at`, `last_login_at`.

`refresh_tokens`
- `id` UUID PK.
- `user_id` FK `users(id)`.
- `token_hash` text (hashed refresh token).
- `device_id` text (from client header) for targeted revocation.
- `expires_at`, `revoked_at`, `created_at`, `updated_at`.

Optional `audit_log` table records auth attempts with reason codes for diagnostics.

## Security Requirements
- Validate Google ID tokens against the project’s OAuth client ID, issuer `https://accounts.google.com`, and `email_verified`.
- Enforce HTTPS + HSTS; reject `http`.
- Implement per-IP and per-device rate limiting for `/auth/*`.
- Store refresh tokens hashed and salt them before hashing to prevent rainbow table reuse.
- Never include tokens in logs or analytics.
- Use Key Vault backed `SigningCredentials` and rotate quarterly.
- Add structured audit logging for auth successes/failures (user id, sub, reason, device id).
- Optional: add risk checks (suspicious device, repeated failures) before issuing refresh tokens.

## Observability
- Metrics: counts for login success/failure, refresh success/failure, revoked tokens used.
- Logs: structured JSON with `event`, `userId`, `googleSub`, `deviceId`, `reason`.
- Traces: wrap token verification + DB calls to spot latency.

## Implementation Notes
- Use `GoogleJsonWebSignature.ValidateAsync` with cached Google certs (expires hourly).
- Define `IUserRepository`, `ITokenService`, and `IGoogleTokenVerifier` interfaces for isolated testing.
- Middleware order: `UseAuthentication` → `UseAuthorization`.
- Consider background job to purge expired refresh tokens.

## Testing Plan (TDD)
1. Unit tests for `GoogleTokenVerifier` covering valid token, invalid signature, wrong audience, expired token.
2. Unit tests for `TokenService` verifying claim contents, TTL, refresh rotation, and hashing.
3. Repository tests ensuring upsert semantics (existing user updates, new user inserted).
4. API integration tests:
   - `/auth/google` 400 on missing field.
   - `/auth/google` 401 on invalid token.
   - `/auth/google` 200 returns JWT + persists user.
   - `/auth/refresh` rotates refresh tokens, rejects reused ones.
   - Protected sample endpoint returns 401 without JWT, 200 with valid JWT.
5. Instrumentation test (Android) hitting a mock server to verify headers + storage (documented for mobile team).

## Frontend Checklist (Android)
- Configure Google Sign-In with backend-provided web client ID.
- Request ID token each launch; handle `resend` states gracefully.
- Call `/auth/google` immediately after retrieving ID token.
- Persist tokens in EncryptedSharedPreferences; wipe on logout.
- Intercept `401` responses to trigger refresh or re-login.
- Include device metadata headers for backend telemetry.

## Backend Checklist (.NET)
- Wire up Google token validation service with caching.
- Build `/auth/google`, `/auth/refresh`, `/auth/logout` endpoints and response DTOs.
- Configure JWT authentication middleware with correct issuer/audience/signing key.
- Implement repositories for `users` and `refresh_tokens`.
- Add health check verifying Key Vault + DB connectivity for auth components.
- Add dashboards/alerts for auth errors > threshold.

## Rollout & Operations
- Dry run using Postman + mock Google token (via Google sandbox) before exposing endpoint.
- Gate production rollout behind feature flag; allowlist QA accounts first.
- Rotate signing keys and refresh token secrets every 90 days; automate reminder.
- Document manual revocation process (SQL script, admin command).

## Open Questions
- Do we require additional profile data (photo URL, locale) for the Android UI?
- Do we need device-based revocation (per phone) or account-wide?
- Is anonymous/guest mode required before login?
