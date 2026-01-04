// ABOUTME: Focused unit tests for the TokenService claim contents and hashing helpers.
// ABOUTME: Verifies TTL calculations, device claim emission, and salted refresh hashing.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using TodoBackend.Api.Auth.Entities;
using TodoBackend.Api.Auth.Options;
using TodoBackend.Api.Auth.Services;
using TodoBackend.Api.Security;

namespace TodoBackend.Tests.Auth;

public sealed class TokenServiceTests
{
    private static readonly JwtOptions DefaultOptions = new()
    {
        Issuer = "todo-tests",
        Audience = "todo-tests",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 10,
        RefreshTokenSalt = "unit-test-salt"
    };

    [Fact]
    public void IssueTokens_EmbedsClaimsAndHonorsTtl()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            GoogleSub = "google-subject",
            Email = "user@example.com",
            Name = "User Name"
        };
        var deviceId = "device-123";
        var now = new DateTimeOffset(2024, 01, 01, 12, 00, 00, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var keyProvider = new InMemoryKeyProvider();
        var service = new TokenService(Options.Create(DefaultOptions), keyProvider, clock);

        var pair = service.IssueTokens(user, deviceId);

        Assert.Equal(now.AddMinutes(DefaultOptions.AccessTokenMinutes), pair.AccessTokenExpiresAt);
        Assert.Equal(now.AddDays(DefaultOptions.RefreshTokenDays), pair.RefreshTokenExpiresAt);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(pair.AccessToken);
        Assert.Equal(DefaultOptions.Issuer, parsed.Issuer);
        Assert.Equal(DefaultOptions.Audience, parsed.Audiences.Single());
        Assert.Equal(keyProvider.KeyId, parsed.Header.Kid);

        AssertClaims(parsed.Claims, user, deviceId);
    }

    [Fact]
    public void HashRefreshToken_UsesSaltedHash()
    {
        var service = new TokenService(Options.Create(DefaultOptions), new InMemoryKeyProvider(), new FixedTimeProvider(DateTimeOffset.UtcNow));

        const string token = "refresh-token-value";
        var hashed = service.HashRefreshToken(token);
        Assert.False(string.IsNullOrWhiteSpace(hashed));

        using var sha = SHA256.Create();
        var unsaltedBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        var unsalted = Convert.ToBase64String(unsaltedBytes);

        Assert.NotEqual(unsalted, hashed); // proves salt inclusion
        Assert.Equal(hashed, service.HashRefreshToken(token)); // deterministic
    }

    private static void AssertClaims(IEnumerable<Claim> claims, User user, string deviceId)
    {
        var dict = claims.ToDictionary(c => c.Type, c => c.Value);
        Assert.Equal(user.Id.ToString(), dict[JwtRegisteredClaimNames.Sub]);
        Assert.Equal(user.Id.ToString(), dict[ClaimTypes.NameIdentifier]);
        Assert.Equal(user.Email, dict[JwtRegisteredClaimNames.Email]);
        Assert.Equal(user.Name, dict[JwtRegisteredClaimNames.Name]);
        Assert.Equal(user.GoogleSub, dict["google_sub"]);
        Assert.Equal(deviceId, dict["device_id"]);
        Assert.Equal("user", dict[ClaimTypes.Role]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class InMemoryKeyProvider : IJwtKeyProvider
    {
        private readonly RsaSecurityKey _key;

        public InMemoryKeyProvider()
        {
            var rsa = RSA.Create(2048);
            _key = new RsaSecurityKey(rsa)
            {
                KeyId = Guid.NewGuid().ToString("N")
            };
        }

        public RsaSecurityKey SigningKey => _key;
        public RsaSecurityKey ValidationKey => _key;
        public string KeyId => _key.KeyId!;
    }
}
