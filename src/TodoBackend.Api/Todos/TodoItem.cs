// ABOUTME: Entity representing a single todo item owned by a user.
// ABOUTME: Stores UTC timestamps, completion state, and server-side ordering.

using TodoBackend.Api.Auth.Entities;

namespace TodoBackend.Api.Todos;

public sealed class TodoItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? ParentId { get; set; }
    public TodoItem? Parent { get; set; }
    public ICollection<TodoItem> Children { get; set; } = new List<TodoItem>();
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset? StartDateTimeUtc { get; set; }
    public DateTimeOffset? EndDateTimeUtc { get; set; }
    public bool IsCompleted { get; set; }
    public int SortOrder { get; set; }
}
