// ABOUTME: Contracts for todo CRUD and ordering API requests and responses.
// ABOUTME: Defines the JSON shapes shared between clients and the backend.

namespace TodoBackend.Api.Todos;

public sealed record CreateTodoRequest(
    string Title,
    DateTimeOffset? StartDateTimeUtc,
    DateTimeOffset? EndDateTimeUtc,
    bool IsCompleted
);

public sealed record UpdateTodoRequest(
    string Title,
    DateTimeOffset? StartDateTimeUtc,
    DateTimeOffset? EndDateTimeUtc,
    bool IsCompleted
);

public sealed record ReorderTodosRequest(
    IReadOnlyList<Guid> OrderedIds
);

public sealed record TodoResponse(
    Guid Id,
    string Title,
    DateTimeOffset? StartDateTimeUtc,
    DateTimeOffset? EndDateTimeUtc,
    bool IsCompleted,
    int SortOrder
);
