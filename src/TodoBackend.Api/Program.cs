// ABOUTME: Entry point wiring together the Minimal API, auth services, and persistence.
// ABOUTME: Hosts JWT-secured endpoints for Google sign-in, refresh, logout, and `/me`.

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TodoBackend.Api.Auth.Contracts;
using TodoBackend.Api.Auth.Entities;
using TodoBackend.Api.Auth.Options;
using TodoBackend.Api.Auth.Services;
using TodoBackend.Api.Data;
using TodoBackend.Api.Security;
using TodoBackend.Api.Todos;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Google", new OpenApiSecurityScheme
    {
        Description = "JWT from /auth/google. Use https://developers.google.com/oauthplayground/ to obtain a Google ID token, then exchange it.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.OperationFilter<AuthorizedEndpointsOperationFilter>();
});
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection("Auth:Google"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Auth:Jwt"));

builder.Services.AddSingleton<IJwtKeyProvider, JwtKeyProvider>();
builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();
builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddDbContext<TodoDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Database");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseInMemoryDatabase("todo-backend-dev");
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>, IJwtKeyProvider>((options, jwtOptions, keyProvider) =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Value.Issuer,
            ValidAudience = jwtOptions.Value.Audience,
            IssuerSigningKey = keyProvider.ValidationKey,
            ClockSkew = TimeSpan.FromSeconds(60)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing")
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    // When running behind a reverse proxy (CloudFront/ALB), trust forwarded proto so HTTPS redirection
    // doesn't cause redirect loops (the origin may be HTTP even when the viewer is HTTPS).
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        // This app is intended to run behind managed AWS proxies (CloudFront/ALB).
        // Accepting all proxy sources is sufficient for this project; a hardened setup should restrict
        // these to known proxy networks to prevent header spoofing.
        KnownIPNetworks =
        {
            new System.Net.IPNetwork(IPAddress.Any, 0),
            new System.Net.IPNetwork(IPAddress.IPv6Any, 0)
        }
    });
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/auth/google", async Task<IResult> (
    [FromBody] GoogleAuthRequest request,
    IGoogleTokenVerifier verifier,
    TodoDbContext db,
    ITokenService tokenService,
    TimeProvider clock,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.IdToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["idToken"] = ["idToken is required."]
        });
    }

    var googleUser = await verifier.ValidateAsync(request.IdToken, cancellationToken);
    if (googleUser is null)
    {
        return Results.Unauthorized();
    }

    var nowInstant = clock.GetUtcNow();
    var timestamp = nowInstant.UtcDateTime;
    var user = await db.Users.SingleOrDefaultAsync(u => u.GoogleSub == googleUser.Subject, cancellationToken);
    if (user is null)
    {
        user = new User
        {
            Id = Guid.NewGuid(),
            GoogleSub = googleUser.Subject,
            Email = googleUser.Email,
            Name = googleUser.Name,
            AvatarUrl = googleUser.AvatarUrl,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            LastLoginAt = timestamp
        };
        await db.Users.AddAsync(user, cancellationToken);
    }
    else
    {
        if (!string.Equals(user.Email, googleUser.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = googleUser.Email;
        }

        if (!string.Equals(user.Name, googleUser.Name, StringComparison.Ordinal))
        {
            user.Name = googleUser.Name;
        }

        if (!string.Equals(user.AvatarUrl, googleUser.AvatarUrl, StringComparison.Ordinal))
        {
            user.AvatarUrl = googleUser.AvatarUrl;
        }

        user.LastLoginAt = timestamp;
        user.UpdatedAt = timestamp;
    }

    var deviceId = ResolveDeviceId(httpContext);
    var tokens = tokenService.IssueTokens(user, deviceId);

    var refresh = new RefreshToken
    {
        Id = Guid.NewGuid(),
        User = user,
        TokenHash = tokens.RefreshTokenHash,
        DeviceId = deviceId,
        ExpiresAt = tokens.RefreshTokenExpiresAt.UtcDateTime,
        CreatedAt = timestamp,
        UpdatedAt = timestamp
    };

    await db.RefreshTokens.AddAsync(refresh, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);

    var responseTimestamp = clock.GetUtcNow();
    return Results.Ok(ToAuthResponse(user, tokens, responseTimestamp));
})
.WithName("GoogleSignIn")
.WithOpenApi();

app.MapPost("/auth/refresh", async Task<IResult> (
    [FromBody] RefreshTokenRequest request,
    ITokenService tokenService,
    TodoDbContext db,
    TimeProvider clock,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["refreshToken"] = ["refreshToken is required."]
        });
    }

    var hash = tokenService.HashRefreshToken(request.RefreshToken);
    var refresh = await db.RefreshTokens
        .Include(rt => rt.User)
        .SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

    if (refresh is null)
    {
        return Results.Unauthorized();
    }

    var nowInstant = clock.GetUtcNow();
    var now = nowInstant.UtcDateTime;
    if (refresh.RevokedAt.HasValue || refresh.ExpiresAt <= now)
    {
        return Results.Unauthorized();
    }

    refresh.RevokedAt = now;
    refresh.UpdatedAt = now;

    var deviceId = ResolveDeviceId(httpContext, refresh.DeviceId);
    var tokens = tokenService.IssueTokens(refresh.User, deviceId);

    var newRefresh = new RefreshToken
    {
        Id = Guid.NewGuid(),
        User = refresh.User,
        TokenHash = tokens.RefreshTokenHash,
        DeviceId = deviceId,
        ExpiresAt = tokens.RefreshTokenExpiresAt.UtcDateTime,
        CreatedAt = now,
        UpdatedAt = now
    };
    await db.RefreshTokens.AddAsync(newRefresh, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);

    var responseTimestamp = clock.GetUtcNow();
    return Results.Ok(ToAuthResponse(refresh.User, tokens, responseTimestamp));
})
.WithName("RefreshToken")
.WithOpenApi();

app.MapPost("/auth/logout", async Task<IResult> (
    [FromBody] LogoutRequest request,
    ITokenService tokenService,
    TodoDbContext db,
    TimeProvider clock,
    CancellationToken cancellationToken) =>
{
    if (request?.RefreshToken is null)
    {
        return Results.NoContent();
    }

    var hash = tokenService.HashRefreshToken(request.RefreshToken);
    var existing = await db.RefreshTokens.SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);
    if (existing is not null)
    {
        existing.RevokedAt = clock.GetUtcNow().UtcDateTime;
        existing.UpdatedAt = existing.RevokedAt.Value;
        await db.SaveChangesAsync(cancellationToken);
    }

    return Results.NoContent();
})
.WithName("Logout")
.WithOpenApi();

app.MapGet("/me", async Task<IResult> (
    ClaimsPrincipal principal,
    TodoDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.FindAsync(new object?[] { userId }, cancellationToken);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new AuthenticatedUser(user.Id, user.Email, user.Name, user.AvatarUrl));
})
.RequireAuthorization()
.WithName("Me")
.WithOpenApi();

var todosGroup = app.MapGroup("/todos")
    .RequireAuthorization()
    .WithOpenApi();

const int todoTitleMaxLength = 256;

todosGroup.MapGet("", async Task<IResult> (
    ClaimsPrincipal principal,
    TodoDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var todos = await db.Todos
        .AsNoTracking()
        .Where(todo => todo.UserId == userId)
        .ToListAsync(cancellationToken);

    var ordered = OrderTodosForHierarchy(todos);
    return Results.Ok(ordered.Select(ToTodoResponse).ToList());
})
.WithName("GetTodos")
.Produces<List<TodoResponse>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

todosGroup.MapPost("", async Task<IResult> (
    [FromBody] CreateTodoRequest request,
    ClaimsPrincipal principal,
    TodoDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = ["title is required."]
        });
    }

    var trimmedTitle = request.Title.Trim();
    if (trimmedTitle.Length > todoTitleMaxLength)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = [$"title must be {todoTitleMaxLength} characters or fewer."]
        });
    }

    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    if (request.ParentId.HasValue)
    {
        var parentExists = await db.Todos.AnyAsync(
            todo => todo.UserId == userId && todo.Id == request.ParentId.Value,
            cancellationToken);
        if (!parentExists)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["parentId"] = ["parentId must reference an existing todo."]
            });
        }
    }

    var maxOrder = await db.Todos
        .Where(todo => todo.UserId == userId && todo.ParentId == request.ParentId)
        .MaxAsync(todo => (int?)todo.SortOrder, cancellationToken);

    var todo = new TodoItem
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ParentId = request.ParentId,
        Title = trimmedTitle,
        StartDateTimeUtc = NormalizeUtc(request.StartDateTimeUtc),
        EndDateTimeUtc = NormalizeUtc(request.EndDateTimeUtc),
        IsCompleted = request.IsCompleted,
        SortOrder = (maxOrder ?? -1) + 1
    };

    await db.Todos.AddAsync(todo, cancellationToken);
    await ApplyCompletionRulesAsync(db, userId, todo, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/todos/{todo.Id}", ToTodoResponse(todo));
})
.WithName("CreateTodo")
.Produces<TodoResponse>(StatusCodes.Status201Created)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

todosGroup.MapPut("/{id:guid}", async Task<IResult> (
    Guid id,
    [FromBody] UpdateTodoRequest request,
    ClaimsPrincipal principal,
    TodoDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = ["title is required."]
        });
    }

    var trimmedTitle = request.Title.Trim();
    if (trimmedTitle.Length > todoTitleMaxLength)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = [$"title must be {todoTitleMaxLength} characters or fewer."]
        });
    }

    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var todo = await db.Todos.SingleOrDefaultAsync(
        item => item.Id == id && item.UserId == userId,
        cancellationToken);
    if (todo is null)
    {
        return Results.NotFound();
    }

    todo.Title = trimmedTitle;
    todo.StartDateTimeUtc = NormalizeUtc(request.StartDateTimeUtc);
    todo.EndDateTimeUtc = NormalizeUtc(request.EndDateTimeUtc);
    todo.IsCompleted = request.IsCompleted;

    await ApplyCompletionRulesAsync(db, userId, todo, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToTodoResponse(todo));
})
.WithName("UpdateTodo")
.Produces<TodoResponse>(StatusCodes.Status200OK)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status404NotFound);

todosGroup.MapDelete("/{id:guid}", async Task<IResult> (
    Guid id,
    ClaimsPrincipal principal,
    TodoDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var todo = await db.Todos.SingleOrDefaultAsync(
        item => item.Id == id && item.UserId == userId,
        cancellationToken);
    if (todo is null)
    {
        return Results.NotFound();
    }

    var todos = await db.Todos
        .Where(item => item.UserId == userId)
        .ToListAsync(cancellationToken);

    var childrenLookup = todos
        .Where(item => item.ParentId.HasValue)
        .GroupBy(item => item.ParentId!.Value)
        .ToDictionary(group => group.Key, group => group.ToList());

    var deletedIds = new HashSet<Guid>();
    var stack = new Stack<Guid>();
    stack.Push(todo.Id);
    while (stack.Count > 0)
    {
        var currentId = stack.Pop();
        if (!deletedIds.Add(currentId))
        {
            continue;
        }

        if (!childrenLookup.TryGetValue(currentId, out var children))
        {
            continue;
        }

        foreach (var child in children)
        {
            stack.Push(child.Id);
        }
    }

    todos.RemoveAll(item => deletedIds.Contains(item.Id));
    db.Todos.Remove(todo);
    RecomputeCompletionFromChildren(todos);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
})
.WithName("DeleteTodo")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status404NotFound);

todosGroup.MapPut("/reorder", async Task<IResult> (
    [FromBody] ReorderTodosRequest request,
    ClaimsPrincipal principal,
    TodoDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request?.Items is null || request.Items.Count == 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["items"] = ["items must contain every todo id."]
        });
    }

    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var todos = await db.Todos
        .Where(todo => todo.UserId == userId)
        .ToListAsync(cancellationToken);

    if (todos.Count != request.Items.Count)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["items"] = ["items must contain every todo id."]
        });
    }

    var orderedSet = request.Items.Select(item => item.Id).ToHashSet();
    if (orderedSet.Count != todos.Count || todos.Any(todo => !orderedSet.Contains(todo.Id)))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["items"] = ["items must contain every todo id."]
        });
    }

    var itemLookup = request.Items.ToDictionary(item => item.Id, item => item);
    if (itemLookup.Count != request.Items.Count)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["items"] = ["items must list each todo id exactly once."]
        });
    }

    foreach (var item in request.Items)
    {
        if (item.ParentId == item.Id)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["items"] = ["parentId cannot reference the same todo."]
            });
        }

        if (item.ParentId.HasValue && !itemLookup.ContainsKey(item.ParentId.Value))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["items"] = ["parentId must reference a todo in the same reorder request."]
            });
        }
    }

    if (HasParentCycles(request.Items))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["items"] = ["parentId must not introduce cycles."]
        });
    }

    var hasDuplicateSortOrder = request.Items
        .GroupBy(item => item.ParentId)
        .Any(group => group.Select(item => item.SortOrder).Distinct().Count() != group.Count());
    if (hasDuplicateSortOrder)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["items"] = ["sortOrder must be unique within the same parent."]
        });
    }

    foreach (var todo in todos)
    {
        var item = itemLookup[todo.Id];
        todo.ParentId = item.ParentId;
        todo.SortOrder = item.SortOrder;
    }

    RecomputeCompletionFromChildren(todos);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
})
.WithName("ReorderTodos")
.WithOpenApi(operation =>
{
    operation.Summary = "Reorder and optionally reparent todos.";
    operation.Description = "Breaking change: the request now requires an items list " +
                            "containing todo id, parentId, and sortOrder; orderedIds is no longer supported.";
    return operation;
})
.Produces(StatusCodes.Status204NoContent)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("Healthz");

app.Run();

static AuthResponse ToAuthResponse(User user, TokenPair pair, DateTimeOffset issuedAt)
{
    var ttlSeconds = (int)Math.Max(0, (pair.AccessTokenExpiresAt - issuedAt).TotalSeconds);
    return new AuthResponse(
        pair.AccessToken,
        ttlSeconds,
        pair.RefreshToken,
        new AuthenticatedUser(user.Id, user.Email, user.Name, user.AvatarUrl));
}

static TodoResponse ToTodoResponse(TodoItem todo) =>
    new(
        todo.Id,
        todo.Title,
        todo.StartDateTimeUtc,
        todo.EndDateTimeUtc,
        todo.IsCompleted,
        todo.SortOrder,
        todo.ParentId
    );

static DateTimeOffset? NormalizeUtc(DateTimeOffset? value) =>
    value?.ToUniversalTime();

static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
{
    var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                      principal.FindFirstValue("sub");

    return Guid.TryParse(userIdValue, out userId);
}

static async Task ApplyCompletionRulesAsync(
    TodoDbContext db,
    Guid userId,
    TodoItem updatedTodo,
    CancellationToken cancellationToken)
{
    var todos = await db.Todos
        .Where(todo => todo.UserId == userId)
        .ToListAsync(cancellationToken);

    if (todos.All(todo => todo.Id != updatedTodo.Id))
    {
        todos.Add(updatedTodo);
    }

    var lookup = todos.ToDictionary(todo => todo.Id, todo => todo);
    if (!lookup.TryGetValue(updatedTodo.Id, out var resolvedTodo))
    {
        return;
    }

    var childrenLookup = todos
        .Where(todo => todo.ParentId.HasValue)
        .GroupBy(todo => todo.ParentId!.Value)
        .ToDictionary(group => group.Key, group => group.ToList());

    if (resolvedTodo.IsCompleted)
    {
        CompleteDescendants(resolvedTodo, childrenLookup);
        CompleteAncestorsWhenAllChildrenComplete(resolvedTodo, lookup, childrenLookup);
    }
    else
    {
        MarkAncestorsIncomplete(resolvedTodo, lookup);
    }
}

static IReadOnlyList<TodoItem> OrderTodosForHierarchy(IReadOnlyList<TodoItem> todos)
{
    if (todos.Count == 0)
    {
        return Array.Empty<TodoItem>();
    }

    var lookup = todos.ToDictionary(todo => todo.Id, todo => todo);
    var childrenLookup = todos
        .Where(todo => todo.ParentId.HasValue)
        .GroupBy(todo => todo.ParentId!.Value)
        .ToDictionary(
            group => group.Key,
            group => group.OrderBy(todo => todo.SortOrder).ThenBy(todo => todo.Id).ToList());

    var roots = todos
        .Where(todo => !todo.ParentId.HasValue || !lookup.ContainsKey(todo.ParentId.Value))
        .OrderBy(todo => todo.SortOrder)
        .ThenBy(todo => todo.Id)
        .ToList();

    var ordered = new List<TodoItem>(todos.Count);
    var visited = new HashSet<Guid>();

    foreach (var root in roots)
    {
        AppendPreOrder(root, ordered, visited, childrenLookup);
    }

    if (visited.Count != todos.Count)
    {
        foreach (var todo in todos.OrderBy(todo => todo.SortOrder).ThenBy(todo => todo.Id))
        {
            if (visited.Add(todo.Id))
            {
                ordered.Add(todo);
            }
        }
    }

    return ordered;
}

static void AppendPreOrder(
    TodoItem root,
    ICollection<TodoItem> ordered,
    ISet<Guid> visited,
    IReadOnlyDictionary<Guid, List<TodoItem>> childrenLookup)
{
    var stack = new Stack<TodoItem>();
    stack.Push(root);

    while (stack.Count > 0)
    {
        var current = stack.Pop();
        if (!visited.Add(current.Id))
        {
            continue;
        }

        ordered.Add(current);

        if (!childrenLookup.TryGetValue(current.Id, out var children))
        {
            continue;
        }

        for (var index = children.Count - 1; index >= 0; index -= 1)
        {
            stack.Push(children[index]);
        }
    }
}

static void RecomputeCompletionFromChildren(IReadOnlyList<TodoItem> todos)
{
    var childrenLookup = todos
        .Where(todo => todo.ParentId.HasValue)
        .GroupBy(todo => todo.ParentId!.Value)
        .ToDictionary(group => group.Key, group => group.ToList());

    var visited = new HashSet<Guid>();
    var postOrder = new List<TodoItem>(todos.Count);

    foreach (var todo in todos)
    {
        if (visited.Contains(todo.Id))
        {
            continue;
        }

        var stack = new Stack<(TodoItem Item, bool Expanded)>();
        stack.Push((todo, false));

        while (stack.Count > 0)
        {
            var (current, expanded) = stack.Pop();
            if (expanded)
            {
                postOrder.Add(current);
                continue;
            }

            if (!visited.Add(current.Id))
            {
                continue;
            }

            stack.Push((current, true));

            if (childrenLookup.TryGetValue(current.Id, out var children))
            {
                foreach (var child in children)
                {
                    if (!visited.Contains(child.Id))
                    {
                        stack.Push((child, false));
                    }
                }
            }
        }
    }

    foreach (var todo in postOrder)
    {
        if (childrenLookup.TryGetValue(todo.Id, out var children) && children.Count > 0)
        {
            todo.IsCompleted = children.All(child => child.IsCompleted);
        }
    }
}

static void CompleteDescendants(
    TodoItem root,
    IReadOnlyDictionary<Guid, List<TodoItem>> childrenLookup)
{
    var stack = new Stack<TodoItem>();
    stack.Push(root);

    while (stack.Count > 0)
    {
        var current = stack.Pop();
        if (!childrenLookup.TryGetValue(current.Id, out var children))
        {
            continue;
        }

        foreach (var child in children)
        {
            child.IsCompleted = true;
            stack.Push(child);
        }
    }
}

static void MarkAncestorsIncomplete(
    TodoItem todo,
    IReadOnlyDictionary<Guid, TodoItem> lookup)
{
    var parentId = todo.ParentId;
    while (parentId.HasValue && lookup.TryGetValue(parentId.Value, out var parent))
    {
        parent.IsCompleted = false;
        parentId = parent.ParentId;
    }
}

static void CompleteAncestorsWhenAllChildrenComplete(
    TodoItem todo,
    IReadOnlyDictionary<Guid, TodoItem> lookup,
    IReadOnlyDictionary<Guid, List<TodoItem>> childrenLookup)
{
    var parentId = todo.ParentId;
    while (parentId.HasValue && lookup.TryGetValue(parentId.Value, out var parent))
    {
        if (!childrenLookup.TryGetValue(parent.Id, out var siblings) || siblings.Count == 0)
        {
            break;
        }

        if (siblings.All(child => child.IsCompleted))
        {
            parent.IsCompleted = true;
            parentId = parent.ParentId;
            continue;
        }

        break;
    }
}

static bool HasParentCycles(IReadOnlyList<ReorderTodoItem> items)
{
    var parentLookup = items.ToDictionary(item => item.Id, item => item.ParentId);

    foreach (var item in items)
    {
        var seen = new HashSet<Guid> { item.Id };
        var parentId = item.ParentId;

        while (parentId.HasValue)
        {
            if (!seen.Add(parentId.Value))
            {
                return true;
            }

            if (!parentLookup.TryGetValue(parentId.Value, out var nextParentId))
            {
                break;
            }

            parentId = nextParentId;
        }
    }

    return false;
}

static string ResolveDeviceId(HttpContext context, string? fallback = null)
{
    if (context.Request.Headers.TryGetValue("X-Device-Id", out var deviceHeader) &&
        !string.IsNullOrWhiteSpace(deviceHeader))
    {
        return deviceHeader.ToString();
    }

    if (!string.IsNullOrWhiteSpace(fallback))
    {
        return fallback;
    }

    return $"device-{Guid.NewGuid():N}";
}
