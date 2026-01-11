// ABOUTME: Swagger operation filter that adds bearer auth requirements to secured endpoints.
// ABOUTME: Ensures the OpenAPI document reflects endpoints protected by authorization.

using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TodoBackend.Api.Security;

public sealed class AuthorizedEndpointsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiresAuth = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();
        if (!requiresAuth)
        {
            return;
        }

        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Google"
                }
            }] = Array.Empty<string>()
        });
    }
}
