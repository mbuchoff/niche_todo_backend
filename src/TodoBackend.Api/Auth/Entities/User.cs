// ABOUTME: Represents an authenticated user persisted from Google identity data.
// ABOUTME: Acts as the aggregate root for issuing tokens and tracking profile updates.

namespace TodoBackend.Api.Auth.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string GoogleSub { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
