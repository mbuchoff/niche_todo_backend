// ABOUTME: Provides a configurable WebApplicationFactory for exercising the API end-to-end.
// ABOUTME: Injects fake dependencies and in-memory storage so tests stay hermetic.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodoBackend.Api;
using TodoBackend.Api.Auth.Contracts;
using TodoBackend.Api.Auth.Services;
using TodoBackend.Api.Data;

namespace TodoBackend.Tests;

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _configuration;
    private readonly string _databaseName = $"todo-backend-tests-{Guid.NewGuid()}";

    public TestAppFactory(Dictionary<string, string?>? overrides = null)
    {
        _configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = string.Empty,
            ["Auth:Google:ClientId"] = "test.client",
            ["Auth:Jwt:Issuer"] = "todo-backend-tests",
            ["Auth:Jwt:Audience"] = "todo-backend-tests",
            ["Auth:Jwt:RefreshTokenSalt"] = "tests-salt",
            ["Auth:Jwt:AccessTokenMinutes"] = "15",
            ["Auth:Jwt:RefreshTokenDays"] = "30"
        };

        if (overrides != null)
        {
            foreach (var pair in overrides)
            {
                _configuration[pair.Key] = pair.Value;
            }
        }
    }

    public FakeGoogleTokenVerifier GoogleTokenVerifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(_configuration);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<TodoDbContext>));
            services.AddDbContext<TodoDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll(typeof(IGoogleTokenVerifier));
            services.AddSingleton<IGoogleTokenVerifier>(GoogleTokenVerifier);
        });
    }
}

public sealed class FakeGoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly ConcurrentDictionary<string, GoogleUserInfo> _tokens = new();

    public void Register(string token, GoogleUserInfo user) => _tokens[token] = user;

    public Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        _tokens.TryGetValue(idToken, out var user);
        return Task.FromResult(user);
    }
}
