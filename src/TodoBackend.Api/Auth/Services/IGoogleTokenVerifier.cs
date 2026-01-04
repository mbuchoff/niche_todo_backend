// ABOUTME: Abstraction for server-side Google ID token validation.
// ABOUTME: Allows swapping in fakes for tests while production hits Google APIs.

using TodoBackend.Api.Auth.Contracts;

namespace TodoBackend.Api.Auth.Services;

public interface IGoogleTokenVerifier
{
    Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken);
}
