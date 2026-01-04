// ABOUTME: Production implementation that validates Google ID tokens.
// ABOUTME: Uses Google APIs library to enforce issuer, audience, and email verification.

using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using TodoBackend.Api.Auth.Contracts;
using TodoBackend.Api.Auth.Options;

namespace TodoBackend.Api.Auth.Services;

public sealed class GoogleTokenVerifier(IOptions<GoogleAuthOptions> options, ILogger<GoogleTokenVerifier> logger)
    : IGoogleTokenVerifier
{
    private readonly GoogleAuthOptions _options = options.Value;
    private readonly ILogger<GoogleTokenVerifier> _logger = logger;

    public async Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var validationSettings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _options.ClientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);
            if (payload is null || payload.EmailVerified is false)
            {
                return null;
            }

            return new GoogleUserInfo(
                payload.Subject,
                payload.Email,
                payload.Name ?? payload.Email,
                payload.EmailVerified,
                payload.Picture);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google token validation failed");
            return null;
        }
    }
}
