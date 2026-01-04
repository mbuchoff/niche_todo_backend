// ABOUTME: Guards that Development hosts pull sensitive settings from user secrets.
// ABOUTME: Ensures Google auth configuration can be provided without committing secrets.

using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TodoBackend.Api;
using TodoBackend.Api.Auth.Options;
using TodoBackend.Api.Data;

namespace TodoBackend.Tests.Auth;

public sealed class ConfigurationTests
{
    private const string SecretsId = "8ff5376b-8759-48a3-95d7-6307585995c9";

    [Fact]
    public async Task DevelopmentHost_LoadsGoogleClientId_FromUserSecrets()
    {
        var secretsRoot = CreateTempSecretsRoot();
        try
        {
            var secretsPath = Path.Combine(secretsRoot, ".microsoft", "usersecrets", SecretsId);
            Directory.CreateDirectory(secretsPath);
            var secretsFile = Path.Combine(secretsPath, "secrets.json");
            await File.WriteAllTextAsync(secretsFile, """
            {
              "Auth:Google:ClientId": "secrets-client-id"
            }
            """);

            var prevHome = Environment.GetEnvironmentVariable("HOME");
            var prevAppData = Environment.GetEnvironmentVariable("APPDATA");
            var prevFallback = Environment.GetEnvironmentVariable("DOTNET_USER_SECRETS_FALLBACK_DIR");
            Environment.SetEnvironmentVariable("HOME", secretsRoot);
            Environment.SetEnvironmentVariable("APPDATA", null);
            Environment.SetEnvironmentVariable("DOTNET_USER_SECRETS_FALLBACK_DIR", secretsRoot);

            try
            {
                await using var factory = new DevelopmentAppFactory();
                await using var scope = factory.Services.CreateAsyncScope();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<GoogleAuthOptions>>();

                Assert.Equal("secrets-client-id", options.Value.ClientId);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HOME", prevHome);
                Environment.SetEnvironmentVariable("APPDATA", prevAppData);
                Environment.SetEnvironmentVariable("DOTNET_USER_SECRETS_FALLBACK_DIR", prevFallback);
            }
        }
        finally
        {
            Directory.Delete(secretsRoot, recursive: true);
        }
    }

    private static string CreateTempSecretsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"todo-user-secrets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class DevelopmentAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = string.Empty
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<TodoDbContext>));
                services.AddDbContext<TodoDbContext>(options =>
                    options.UseInMemoryDatabase($"todo-backend-dev-{Guid.NewGuid():N}"));
            });
        }
    }
}
