// ABOUTME: Persists hashed refresh tokens for rotation and revocation.
// ABOUTME: Records device metadata for targeted logout and auditing.

namespace TodoBackend.Api.Auth.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string TokenHash { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
