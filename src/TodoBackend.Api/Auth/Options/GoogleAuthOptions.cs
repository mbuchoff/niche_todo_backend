// ABOUTME: Simple configuration object for the Google Sign-In integration.
// ABOUTME: Bound from configuration and injected wherever token validation is needed.

namespace TodoBackend.Api.Auth.Options;

public sealed class GoogleAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
}
