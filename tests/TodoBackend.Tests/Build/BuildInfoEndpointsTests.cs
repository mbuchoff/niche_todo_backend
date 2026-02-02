// ABOUTME: End-to-end tests for build metadata endpoints.
// ABOUTME: Ensures the API returns the configured Git SHA without auth.

using System.Net;
using System.Net.Http.Json;

namespace TodoBackend.Tests.Build;

public sealed class BuildInfoEndpointsTests
{
    [Fact]
    public async Task ShaEndpoint_ReturnsConfiguredSha()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["GIT_SHA"] = "abc123"
        };
        await using var factory = new TestAppFactory(overrides);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sha");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ShaResponse>();
        Assert.NotNull(payload);
        Assert.Equal("abc123", payload!.Sha);
    }

    [Fact]
    public async Task ShaEndpoint_WhenMissing_ReturnsNull()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sha");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ShaResponse>();
        Assert.NotNull(payload);
        Assert.Null(payload!.Sha);
    }

    private sealed record ShaResponse(string? Sha);
}
