using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DotNetBackendTemplate.Api.Infrastructure.OpenApi;

internal static class OpenApiExtensions
{
    private const string BearerSchemeName = "Bearer";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            // Every slice nests types named Request, Response, or Item. The default schema ID is the simple type
            // name, which collapses those into one schema and documents operations with another slice's payload.
            // Prefixing nested types with their slice gives each its own schema, e.g. GetCampaignResponse.
            options.CreateSchemaReferenceId = typeInfo =>
            {
                string? id = OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo);
                return id is not null && typeInfo.Type.DeclaringType is { } slice ? slice.Name + id : id;
            };

            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = ".NET Backend Template API",
                    Version = "v1",
                    Description =
                        "A .NET 10 Minimal API starter. Protected operations require a JWT bearer access token, and all timestamps use UTC.",
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??=
                    new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Enter a JWT bearer access token issued for this API.",
                };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
                bool requiresAuthorization = metadata.OfType<IAuthorizeData>().Any()
                    && !metadata.OfType<IAllowAnonymous>().Any();

                if (requiresAuthorization)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(
                            BearerSchemeName,
                            context.Document,
                            externalResource: null)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
