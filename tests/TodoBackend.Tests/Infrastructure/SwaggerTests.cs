// ABOUTME: Verifies Swagger documents describe auth requirements for secured endpoints.
// ABOUTME: Ensures bearer auth is exposed for the todo API in OpenAPI output.

using System.Net.Http.Json;
using System.Text.Json;

namespace TodoBackend.Tests.Infrastructure;

public sealed class SwaggerTests
{
    [Fact]
    public async Task SwaggerDocument_IncludesBearerSecurityForTodos()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        var googleScheme = default(JsonElement);
        var hasGoogleScheme = document.TryGetProperty("components", out var components) &&
                              components.TryGetProperty("securitySchemes", out var schemes) &&
                              schemes.TryGetProperty("Google", out googleScheme);
        Assert.True(hasGoogleScheme, "Swagger document should define a Google security scheme.");

        Assert.Equal("http", googleScheme.GetProperty("type").GetString());
        Assert.Equal("bearer", googleScheme.GetProperty("scheme").GetString());
        Assert.Contains(
            "developers.google.com/oauthplayground",
            googleScheme.GetProperty("description").GetString()
        );

        var hasTodoSecurity = document.TryGetProperty("paths", out var paths) &&
                              paths.TryGetProperty("/todos", out var todosPath) &&
                              todosPath.TryGetProperty("get", out var getOperation) &&
                              getOperation.TryGetProperty("security", out var security) &&
                              security.ValueKind == JsonValueKind.Array &&
                              security.EnumerateArray()
                                  .SelectMany(entry => entry.EnumerateObject())
                                  .Any(entry => entry.NameEquals("Google"));

        Assert.True(hasTodoSecurity, "Swagger GET /todos should require Google auth.");
    }
}
