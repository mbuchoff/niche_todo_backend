// ABOUTME: Entity Framework Core DbContext for auth and todo persistence needs.
// ABOUTME: Configures users, refresh tokens, and todo tables with required indexes.

using Microsoft.EntityFrameworkCore;
using TodoBackend.Api.Auth.Entities;
using TodoBackend.Api.Todos;

namespace TodoBackend.Api.Data;

public sealed class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("users");
            builder.HasKey(user => user.Id);
            builder.HasIndex(user => user.GoogleSub).IsUnique();
            builder.Property(user => user.GoogleSub).IsRequired().HasMaxLength(128);
            builder.Property(user => user.Email).IsRequired().HasMaxLength(320);
            builder.Property(user => user.Name).IsRequired().HasMaxLength(256);
            builder.Property(user => user.AvatarUrl).HasMaxLength(512);
            builder.Property(user => user.CreatedAt).IsRequired();
            builder.Property(user => user.UpdatedAt).IsRequired();
            builder.Property(user => user.LastLoginAt);
        });

        modelBuilder.Entity<RefreshToken>(builder =>
        {
            builder.ToTable("refresh_tokens");
            builder.HasKey(token => token.Id);
            builder.HasIndex(token => token.TokenHash).IsUnique();
            builder.Property(token => token.TokenHash).IsRequired().HasMaxLength(256);
            builder.Property(token => token.DeviceId).IsRequired().HasMaxLength(128);
            builder.Property(token => token.ExpiresAt).IsRequired();
            builder.Property(token => token.CreatedAt).IsRequired();
            builder.Property(token => token.UpdatedAt).IsRequired();
            builder.HasOne(token => token.User)
                .WithMany(user => user.RefreshTokens)
                .HasForeignKey(token => token.UserId);
        });

        modelBuilder.Entity<TodoItem>(builder =>
        {
            builder.ToTable("todos");
            builder.HasKey(todo => todo.Id);
            builder.HasIndex(todo => new { todo.UserId, todo.SortOrder });
            builder.Property(todo => todo.Title).IsRequired().HasMaxLength(256);
            builder.Property(todo => todo.SortOrder).IsRequired();
            builder.Property(todo => todo.StartDateTimeUtc);
            builder.Property(todo => todo.EndDateTimeUtc);
            builder.Property(todo => todo.IsCompleted).IsRequired();
            builder.HasOne(todo => todo.User)
                .WithMany(user => user.Todos)
                .HasForeignKey(todo => todo.UserId);
        });
    }
}
