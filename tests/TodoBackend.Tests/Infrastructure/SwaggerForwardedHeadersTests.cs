// ABOUTME: Ensures Swagger UI is reachable behind a reverse proxy that terminates TLS.
// ABOUTME: Guards against redirect loops when the app is deployed behind CloudFront/ALB over HTTP to the origin.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodoBackend.Api;
using TodoBackend.Api.Data;

namespace TodoBackend.Tests.Infrastructure;

public sealed class SwaggerForwardedHeadersTests
{
    [Fact]
    public async Task DevelopmentHost_WithForwardedProtoHttps_DoesNotRedirectSwagger()
    {
        await using var factory = new DevelopmentAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
