// ABOUTME: Issues JWT access tokens and hashed refresh tokens per policy.
// ABOUTME: Encapsulates signing, claim construction, and rotation helpers.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TodoBackend.Api.Auth.Entities;
using TodoBackend.Api.Auth.Options;
using TodoBackend.Api.Security;

namespace TodoBackend.Api.Auth.Services;

public interface ITokenService
{
    TokenPair IssueTokens(User user, string deviceId);
    string HashRefreshToken(string refreshToken);
}

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IJwtKeyProvider _keyProvider;
    private readonly TimeProvider _clock;

    public TokenService(IOptions<JwtOptions> options, IJwtKeyProvider keyProvider, TimeProvider clock)
    {
        _options = options.Value;
        _keyProvider = keyProvider;
        _clock = clock;
    }

    public TokenPair IssueTokens(User user, string deviceId)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = BuildClaims(user, deviceId);
        var credentials = new SigningCredentials(_keyProvider.SigningKey, SecurityAlgorithms.RsaSha256);
        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);
        jwt.Header["kid"] = _keyProvider.KeyId;
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(jwt);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);
        var refreshHash = HashRefreshToken(refreshToken);

        return new TokenPair(
            token,
            expires,
            refreshToken,
            refreshHash,
            refreshExpires);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var salted = refreshToken + _options.RefreshTokenSalt;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salted));
        return Convert.ToBase64String(bytes);
    }

    private static IEnumerable<Claim> BuildClaims(User user, string deviceId)
    {
        yield return new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString());
        yield return new Claim(ClaimTypes.NameIdentifier, user.Id.ToString());
        yield return new Claim(JwtRegisteredClaimNames.Email, user.Email);
        yield return new Claim(JwtRegisteredClaimNames.Name, user.Name);
        yield return new Claim("google_sub", user.GoogleSub);
        yield return new Claim("device_id", deviceId);
        yield return new Claim(ClaimTypes.Role, "user");
    }
}

public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    string RefreshTokenHash,
    DateTimeOffset RefreshTokenExpiresAt);
