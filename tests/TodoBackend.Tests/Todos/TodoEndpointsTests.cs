// ABOUTME: End-to-end tests for todo endpoints covering auth, CRUD, and ordering.
// ABOUTME: Confirms server-side UTC normalization and list ordering behavior.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TodoBackend.Api.Auth.Contracts;

namespace TodoBackend.Tests.Todos;

public sealed class TodoEndpointsTests
{
    [Fact]
    public async Task Todos_RequireAuth()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/todos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTodo_NormalizesUtcAndPersists()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var request = new CreateTodoRequest(
            "Write tests",
            new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.FromHours(-5)),
            false
        );

        var createResponse = await client.PostAsJsonAsync("/todos", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.NotNull(created);
        Assert.Equal("Write tests", created!.Title);
        Assert.Equal(TimeSpan.Zero, created.StartDateTimeUtc?.Offset);
        Assert.Equal(TimeSpan.Zero, created.EndDateTimeUtc?.Offset);
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 15, 0, 0, TimeSpan.Zero), created.StartDateTimeUtc);
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 17, 0, 0, TimeSpan.Zero), created.EndDateTimeUtc);

        var listResponse = await client.GetAsync("/todos");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await listResponse.Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal(created.Id, list![0].Id);
        Assert.Equal(created.StartDateTimeUtc, list[0].StartDateTimeUtc);
    }

    [Fact]
    public async Task CreateTodo_RejectsTitleOverMaxLength()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var longTitle = new string('a', 257);
        var response = await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest(longTitle, null, null, false)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(details);
        Assert.Contains("title", details!.Errors.Keys);
    }

    [Fact]
    public async Task UpdateTodo_ChangesFields()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Initial", null, null, false)
        );
        var created = await createResponse.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync(
            $"/todos/{created!.Id}",
            new UpdateTodoRequest(
                "Updated",
                new DateTimeOffset(2025, 4, 1, 9, 0, 0, TimeSpan.Zero),
                null,
                true
            )
        );
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.Title);
        Assert.True(updated.IsCompleted);
        Assert.Equal(new DateTimeOffset(2025, 4, 1, 9, 0, 0, TimeSpan.Zero), updated.StartDateTimeUtc);
    }

    [Fact]
    public async Task UpdateTodo_RejectsTitleOverMaxLength()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Initial", null, null, false)
        );
        var created = await createResponse.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.NotNull(created);

        var longTitle = new string('a', 257);
        var updateResponse = await client.PutAsJsonAsync(
            $"/todos/{created!.Id}",
            new UpdateTodoRequest(longTitle, null, null, false)
        );

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);

        var details = await updateResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(details);
        Assert.Contains("title", details!.Errors.Keys);
    }

    [Fact]
    public async Task DeleteTodo_RemovesItem()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Remove me", null, null, false)
        );
        var created = await createResponse.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.NotNull(created);

        var deleteResponse = await client.DeleteAsync($"/todos/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/todos");
        var list = await listResponse.Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task ReorderTodos_PersistsOrder()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var first = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("First", null, null, false)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var second = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Second", null, null, false)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(first);
        Assert.NotNull(second);

        var reorderResponse = await client.PutAsJsonAsync(
            "/todos/reorder",
            new ReorderTodosRequest(new List<Guid> { second!.Id, first!.Id })
        );
        Assert.Equal(HttpStatusCode.NoContent, reorderResponse.StatusCode);

        var listResponse = await client.GetAsync("/todos");
        var list = await listResponse.Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(list);
        Assert.Equal(new[] { second.Id, first.Id }, list!.Select(item => item.Id));
    }

    private static async Task<string> AuthenticateAsync(TestAppFactory factory, HttpClient client)
    {
        factory.GoogleTokenVerifier.Register("token-todos", new GoogleUserInfo("sub-todo", "todo@example.com", "Todo User", true, null));
        var response = await client.PostAsJsonAsync("/auth/google", new { idToken = "token-todos" });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(payload);
        return payload!.AccessToken;
    }

    private sealed record AuthResponse(string AccessToken, int ExpiresInSeconds, string RefreshToken, UserResponse User);
    private sealed record UserResponse(Guid Id, string Email, string Name);
    private sealed record CreateTodoRequest(
        string Title,
        DateTimeOffset? StartDateTimeUtc,
        DateTimeOffset? EndDateTimeUtc,
        bool IsCompleted
    );
    private sealed record UpdateTodoRequest(
        string Title,
        DateTimeOffset? StartDateTimeUtc,
        DateTimeOffset? EndDateTimeUtc,
        bool IsCompleted
    );
    private sealed record ReorderTodosRequest(List<Guid> OrderedIds);
    private sealed record TodoResponse(
        Guid Id,
        string Title,
        DateTimeOffset? StartDateTimeUtc,
        DateTimeOffset? EndDateTimeUtc,
        bool IsCompleted,
        int SortOrder
    );
}
