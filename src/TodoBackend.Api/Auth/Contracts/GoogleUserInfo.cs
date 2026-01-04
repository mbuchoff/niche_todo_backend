// ABOUTME: Defines strongly-typed payload details returned from Google token verification.
// ABOUTME: Used by the auth flow to upsert users and issue first-party JWTs.

namespace TodoBackend.Api.Auth.Contracts;

/// <summary>
/// Captures the claims we care about from a verified Google ID token.
/// </summary>
public sealed record GoogleUserInfo(
    string Subject,
    string Email,
    string Name,
    bool EmailVerified,
    string? AvatarUrl);
