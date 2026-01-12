// ABOUTME: End-to-end tests for todo endpoints covering auth, CRUD, and ordering.
// ABOUTME: Confirms server-side UTC normalization and list ordering behavior.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TodoBackend.Api.Auth.Contracts;
using TodoBackend.Api.Todos;

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
            false,
            null
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
            new CreateTodoRequest(longTitle, null, null, false, null)
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
            new CreateTodoRequest("Initial", null, null, false, null)
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
            new CreateTodoRequest("Initial", null, null, false, null)
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
            new CreateTodoRequest("Remove me", null, null, false, null)
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
    public async Task DeleteTodo_CascadesToChildren()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var parent = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var child = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child", null, null, false, parent!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(parent);
        Assert.NotNull(child);

        var deleteResponse = await client.DeleteAsync($"/todos/{parent!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/todos");
        var list = await listResponse.Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task DeleteTodo_RecomputesParentCompletion()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var parent = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var childA = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child A", null, null, true, parent!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var childB = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child B", null, null, false, parent!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(parent);
        Assert.NotNull(childA);
        Assert.NotNull(childB);

        var beforeDelete = await (await client.GetAsync("/todos"))
            .Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(beforeDelete);
        Assert.False(beforeDelete!.Single(item => item.Id == parent!.Id).IsCompleted);

        var deleteResponse = await client.DeleteAsync($"/todos/{childB!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await (await client.GetAsync("/todos"))
            .Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(afterDelete);
        Assert.True(afterDelete!.Single(item => item.Id == parent.Id).IsCompleted);
    }

    [Fact]
    public async Task GetTodos_OrdersParentsBeforeChildren()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var parentA = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent A", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var parentB = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent B", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var childA = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child A", null, null, false, parentA!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var childB = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child B", null, null, false, parentB!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(parentA);
        Assert.NotNull(parentB);
        Assert.NotNull(childA);
        Assert.NotNull(childB);

        var list = await (await client.GetAsync("/todos"))
            .Content.ReadFromJsonAsync<List<TodoResponse>>();

        Assert.NotNull(list);
        Assert.Equal(
            ["Parent A", "Child A", "Parent B", "Child B"],
            list!.Select(item => item.Title).ToList());
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
            new CreateTodoRequest("First", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var second = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Second", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(first);
        Assert.NotNull(second);

        var reorderResponse = await client.PutAsJsonAsync(
            "/todos/reorder",
            new ReorderTodosRequest(new List<ReorderTodoItem>
            {
                new(second!.Id, null, 0),
                new(first!.Id, null, 1)
            })
        );
        Assert.Equal(HttpStatusCode.NoContent, reorderResponse.StatusCode);

        var listResponse = await client.GetAsync("/todos");
        var list = await listResponse.Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(list);
        Assert.Equal(new[] { second.Id, first.Id }, list!.OrderBy(item => item.SortOrder).Select(item => item.Id));
    }

    [Fact]
    public async Task ReorderTodos_RejectsSelfReferencingParent()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var todo = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Solo", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(todo);

        var response = await client.PutAsJsonAsync(
            "/todos/reorder",
            new ReorderTodosRequest(new List<ReorderTodoItem>
            {
                new(todo!.Id, todo.Id, 0)
            })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(details);
        Assert.Contains("items", details!.Errors.Keys);
    }

    [Fact]
    public async Task ReorderTodos_RejectsCycles()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var first = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("First", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var second = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Second", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(first);
        Assert.NotNull(second);

        var response = await client.PutAsJsonAsync(
            "/todos/reorder",
            new ReorderTodosRequest(new List<ReorderTodoItem>
            {
                new(first!.Id, second!.Id, 0),
                new(second.Id, first.Id, 0)
            })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(details);
        Assert.Contains("items", details!.Errors.Keys);
    }

    [Fact]
    public async Task CreateTodo_WithParentId_AssignsParent()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var parent = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(parent);

        var childResponse = await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child", null, null, false, parent!.Id)
        );

        Assert.Equal(HttpStatusCode.Created, childResponse.StatusCode);

        var child = await childResponse.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.NotNull(child);
        Assert.Equal(parent.Id, child!.ParentId);

        var listResponse = await client.GetAsync("/todos");
        var list = await listResponse.Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(list);
        Assert.Contains(list!, item => item.Id == child.Id && item.ParentId == parent.Id);
    }

    [Fact]
    public async Task CreateTodo_WithMissingParentId_Rejects()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child", null, null, false, Guid.NewGuid())
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(details);
        Assert.Contains("parentId", details!.Errors.Keys);
    }

    [Fact]
    public async Task UpdateTodo_CompletionCascadesAcrossHierarchy()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var parent = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var childA = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child A", null, null, false, parent!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var childB = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child B", null, null, false, parent!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(childA);
        Assert.NotNull(childB);

        var completeParent = await client.PutAsJsonAsync(
            $"/todos/{parent!.Id}",
            new UpdateTodoRequest("Parent", null, null, true)
        );
        Assert.Equal(HttpStatusCode.OK, completeParent.StatusCode);

        var afterComplete = await (await client.GetAsync("/todos"))
            .Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(afterComplete);
        Assert.True(afterComplete!.Single(item => item.Id == parent.Id).IsCompleted);
        Assert.True(afterComplete.Single(item => item.Id == childA!.Id).IsCompleted);
        Assert.True(afterComplete.Single(item => item.Id == childB!.Id).IsCompleted);

        var uncompleteChild = await client.PutAsJsonAsync(
            $"/todos/{childA!.Id}",
            new UpdateTodoRequest("Child A", null, null, false)
        );
        Assert.Equal(HttpStatusCode.OK, uncompleteChild.StatusCode);

        var afterUncomplete = await (await client.GetAsync("/todos"))
            .Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(afterUncomplete);
        Assert.False(afterUncomplete!.Single(item => item.Id == parent.Id).IsCompleted);
        Assert.False(afterUncomplete.Single(item => item.Id == childA.Id).IsCompleted);
        Assert.True(afterUncomplete.Single(item => item.Id == childB!.Id).IsCompleted);

        var completeChild = await client.PutAsJsonAsync(
            $"/todos/{childA.Id}",
            new UpdateTodoRequest("Child A", null, null, true)
        );
        Assert.Equal(HttpStatusCode.OK, completeChild.StatusCode);

        var afterAllComplete = await (await client.GetAsync("/todos"))
            .Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(afterAllComplete);
        Assert.True(afterAllComplete!.Single(item => item.Id == parent.Id).IsCompleted);
    }

    [Fact]
    public async Task ReorderTodos_AllowsReparenting()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var parentA = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent A", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var parentB = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent B", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var child = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child", null, null, false, parentA!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(parentA);
        Assert.NotNull(parentB);
        Assert.NotNull(child);

        var reorderResponse = await client.PutAsJsonAsync(
            "/todos/reorder",
            new ReorderTodosRequest(new List<ReorderTodoItem>
            {
                new(parentA!.Id, null, 0),
                new(parentB!.Id, null, 1),
                new(child!.Id, parentB.Id, 0)
            })
        );
        Assert.Equal(HttpStatusCode.NoContent, reorderResponse.StatusCode);

        var list = await (await client.GetAsync("/todos"))
            .Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(list);
        var movedChild = list!.Single(item => item.Id == child.Id);
        Assert.Equal(parentB.Id, movedChild.ParentId);
        Assert.Equal(0, movedChild.SortOrder);
    }

    [Fact]
    public async Task ReorderTodos_RecomputesCompletionFromChildren()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var accessToken = await AuthenticateAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var parentA = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent A", null, null, true, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var parentB = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Parent B", null, null, false, null)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var childA = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child A", null, null, false, parentB!.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();
        var childB = await (await client.PostAsJsonAsync(
            "/todos",
            new CreateTodoRequest("Child B", null, null, true, parentB.Id)
        )).Content.ReadFromJsonAsync<TodoResponse>();

        Assert.NotNull(parentA);
        Assert.NotNull(parentB);
        Assert.NotNull(childA);
        Assert.NotNull(childB);

        var reorderResponse = await client.PutAsJsonAsync(
            "/todos/reorder",
            new ReorderTodosRequest(new List<ReorderTodoItem>
            {
                new(parentA!.Id, null, 0),
                new(parentB!.Id, null, 1),
                new(childA!.Id, parentA.Id, 0),
                new(childB!.Id, parentB.Id, 0)
            })
        );
        Assert.Equal(HttpStatusCode.NoContent, reorderResponse.StatusCode);

        var list = await (await client.GetAsync("/todos"))
            .Content.ReadFromJsonAsync<List<TodoResponse>>();
        Assert.NotNull(list);
        Assert.False(list!.Single(item => item.Id == parentA.Id).IsCompleted);
        Assert.True(list.Single(item => item.Id == parentB.Id).IsCompleted);
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
}
