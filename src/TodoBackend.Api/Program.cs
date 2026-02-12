// ABOUTME: Entry point wiring together the Minimal API, auth services, and persistence.
// ABOUTME: Hosts JWT-secured endpoints for Google sign-in, refresh, logout, and `/me`.

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TodoBackend.Api.Auth.Contracts;
using TodoBackend.Api.Auth.Entities;
using TodoBackend.Api.Auth.Options;
using TodoBackend.Api.Auth.Services;
using TodoBackend.Api.Data;
using TodoBackend.Api.Security;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
    var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                      principal.FindFirstValue("sub");

    if (!Guid.TryParse(userIdValue, out var userId))
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
