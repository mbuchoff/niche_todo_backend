// ABOUTME: End-to-end tests for the auth endpoints covering Google sign-in and token rotation.
// ABOUTME: Exercises the Minimal API via HttpClient to enforce real behavior over mocks.

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoBackend.Api.Auth.Contracts;
using TodoBackend.Api.Data;

namespace TodoBackend.Tests.Auth;

public sealed class AuthEndpointsTests
{
    [Fact]
    public async Task GoogleSignIn_InvalidToken_Returns401()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/google", new { idToken = "invalid" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleSignIn_ValidToken_CreatesUserAndReturnsTokens()
    {
        await using var factory = new TestAppFactory();
        var user = new GoogleUserInfo("sub-123", "user@example.com", "Test User", true, null);
        factory.GoogleTokenVerifier.Register("valid-token", user);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/google", new { idToken = "valid-token" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(payload.RefreshToken));
        Assert.Equal("user@example.com", payload.User.Email);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var storedUser = await db.Users.SingleAsync();
        Assert.Equal("sub-123", storedUser.GoogleSub);
    }

    [Fact]
    public async Task GoogleSignIn_ExistingUser_DoesNotDuplicate()
    {
        await using var factory = new TestAppFactory();
        var user = new GoogleUserInfo("sub-abc", "alpha@example.com", "Alpha", true, null);
        factory.GoogleTokenVerifier.Register("token-1", user);
        using var client = factory.CreateClient();

        var firstResponse = await client.PostAsJsonAsync("/auth/google", new { idToken = "token-1" });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PostAsJsonAsync("/auth/google", new { idToken = "token-1" });
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var userCount = await db.Users.CountAsync();
        Assert.Equal(1, userCount);
    }

    [Fact]
    public async Task Refresh_RotatesTokens()
    {
        await using var factory = new TestAppFactory();
        factory.GoogleTokenVerifier.Register("token-rotate", new GoogleUserInfo("sub-rotate", "rot@example.com", "Rot", true, null));
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/auth/google", new { idToken = "token-rotate" });
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loginPayload);

        var refreshResponse = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = loginPayload!.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshPayload = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(refreshPayload);
        Assert.NotEqual(loginPayload.RefreshToken, refreshPayload!.RefreshToken);

        // Old token should now be rejected.
        var replayResponse = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = loginPayload.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_RequiresJwt()
    {
        await using var factory = new TestAppFactory();
        factory.GoogleTokenVerifier.Register("token-me", new GoogleUserInfo("sub-me", "me@example.com", "Me", true, null));
        using var client = factory.CreateClient();

        var unauthorizedResponse = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/auth/google", new { idToken = "token-me" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loginPayload);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginPayload!.AccessToken);
        var meResponse = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var mePayload = await meResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("me@example.com", mePayload!.Email);
    }

    private sealed record AuthResponse(string AccessToken, int ExpiresInSeconds, string RefreshToken, UserResponse User);
    private sealed record UserResponse(Guid Id, string Email, string Name);
}
