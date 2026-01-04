// ABOUTME: Configuration knobs for the API-owned JWT and refresh token policies.
// ABOUTME: Controls issuer metadata, TTLs, salts, and optional PEM key material.

namespace TodoBackend.Api.Auth.Options;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "todo-backend";
    public string Audience { get; set; } = "todo-backend";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public string RefreshTokenSalt { get; set; } = "replace-me";
    public string? PrivateKeyPem { get; set; }
    public string? PublicKeyPem { get; set; }
}
