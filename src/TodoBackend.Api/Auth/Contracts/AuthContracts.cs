// ABOUTME: Defines request/response shapes for the auth endpoints.
// ABOUTME: Keeps Minimal API handlers lean by centralizing DTOs.

using System.ComponentModel.DataAnnotations;

namespace TodoBackend.Api.Auth.Contracts;

public sealed record GoogleAuthRequest(
    [property: Required]
    string IdToken);

public sealed record RefreshTokenRequest(
    [property: Required]
    string RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    AuthenticatedUser User);

public sealed record AuthenticatedUser(Guid Id, string Email, string Name, string? AvatarUrl);
