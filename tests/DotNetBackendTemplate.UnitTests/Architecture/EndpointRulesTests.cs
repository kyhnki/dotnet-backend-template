using System.Reflection;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Features.Orders;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence;
using DotNetBackendTemplate.UnitTests.Architecture.Fixtures.Violations.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetBackendTemplate.UnitTests.Architecture;

/// <summary>SEC-01, SLICE-02, SLICE-04 and SLICE-07: what every endpoint declares, accepts, and returns.</summary>
public sealed class EndpointRulesTests(ProductionEndpoints production) : IClassFixture<ProductionEndpoints>
{
    [Fact]
    public void ProductionEndpointsFollowEndpointRules()
    {
        List<RouteEndpoint> endpoints = [.. production.Endpoints.OfType<RouteEndpoint>()];

        // An empty list, or handlers that cannot be inspected, would make these rules pass vacuously.
        endpoints.Should().NotBeEmpty();
        endpoints.Where(EndpointRules.IsBusinessEndpoint).Should().OnlyContain(
            endpoint => endpoint.Metadata.GetMetadata<MethodInfo>() != null,
            "SLICE-02 and SLICE-04 inspect each endpoint's handler signature");

        ArchitectureAssert.NoViolations(EndpointRules.Check(endpoints, ProductionCode.RootNamespace));
    }

    [Theory]
    [InlineData("SEC-01", "/api/unprotected")]
    [InlineData("SLICE-07", "/api/unprotected")]
    [InlineData("SLICE-04", "/api/single-order")]
    [InlineData("SLICE-04", "/api/order-page")]
    [InlineData("SLICE-04", "/api/order-status")]
    [InlineData("SLICE-04", "/api/order-import")]
    [InlineData("SLICE-02", "/api/database")]
    public async Task EndpointRulesCatchDeliberateViolations(string ruleId, string route)
    {
        await using WebApplication app = ViolatingEndpoints();
        IEnumerable<Endpoint> endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints);

        ArchitectureAssert.Reports(EndpointRules.Check(endpoints, FixtureRoot.Namespace), ruleId, route);
    }

    private static WebApplication ViolatingEndpoints()
    {
        WebApplication app = WebApplication.CreateSlimBuilder().Build();

        app.MapGet("/api/unprotected", () => "no authorization decision and no metadata");
        app.MapGet("/api/single-order", () => TypedResults.Ok(new OrderResponse(new Order()))).RequireAuthorization();
        app.MapGet("/api/order-page", () => TypedResults.Ok(new OrderPage(Array.Empty<Order>()))).RequireAuthorization();
        app.MapGet("/api/order-status", () => TypedResults.Ok(new OrderStatusResponse(OrderStatus.Open))).RequireAuthorization();
        app.MapPost("/api/order-import", (Order order) => TypedResults.Ok(order.Id)).RequireAuthorization();
        app.MapGet("/api/database", ([FromServices] ViolatingDbContext database) => TypedResults.Ok(database.ContextId.InstanceId))
            .RequireAuthorization();

        return app;
    }
}

/// <summary>
/// Every endpoint the API maps. Development maps the superset, including the OpenAPI document. Nothing here opens
/// a database connection or needs Docker.
/// </summary>
public sealed class ProductionEndpoints : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Database", "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true");
        builder.UseSetting("Authentication:Authority", "https://identity.test");
        builder.UseSetting("Authentication:Audience", "architecture-tests");
    });

    public IReadOnlyList<Endpoint> Endpoints => _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

    public void Dispose() => _factory.Dispose();
}

internal static class EndpointRules
{
    public static bool IsBusinessEndpoint(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true;

    public static IEnumerable<ArchitectureViolation> Check(IEnumerable<Endpoint> endpoints, string root)
    {
        string persistence = $"{root}.Persistence";

        foreach (RouteEndpoint endpoint in endpoints.OfType<RouteEndpoint>())
        {
            string subject = Describe(endpoint);
            EndpointMetadataCollection metadata = endpoint.Metadata;

            if (metadata.GetMetadata<IAuthorizeData>() is null && metadata.GetMetadata<IAllowAnonymous>() is null)
            {
                yield return new("SEC-01", subject,
                    "declares neither RequireAuthorization nor AllowAnonymous. Minimal API endpoints are anonymous by default, so the decision must be explicit.");
            }

            if (IsBusinessEndpoint(endpoint) && MissingDocumentation(metadata) is { Count: > 0 } missing)
            {
                yield return new("SLICE-07", subject, $"is missing {string.Join(", ", missing)}.");
            }

            MethodInfo? handler = metadata.GetMetadata<MethodInfo>();
            foreach (ParameterInfo parameter in handler?.GetParameters() ?? [])
            {
                if (IsDatabaseAccess(parameter.ParameterType))
                {
                    yield return new("SLICE-02", subject,
                        $"takes {parameter.ParameterType.Name} '{parameter.Name}'. Endpoints must not query or change the database; dispatch a request and let its handler use the DbContext.");
                    continue;
                }

                foreach (Type leaked in PersistenceTypesIn(parameter.ParameterType, persistence, root))
                {
                    yield return new("SLICE-04", subject,
                        $"binds {leaked.FullName} from the request through '{parameter.Name}'. Bind an API-owned request type instead; binding persistence types allows mass assignment.");
                }
            }

            IEnumerable<Type> results = metadata
                .GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Select(produces => produces.Type)
                .OfType<Type>();

            if (handler is not null)
            {
                results = results.Append(handler.ReturnType);
            }

            foreach (Type leaked in results.SelectMany(result => PersistenceTypesIn(result, persistence, root)).Distinct())
            {
                yield return new("SLICE-04", subject,
                    $"returns {leaked.FullName}. Responses must use API-owned types; map persistence types in the handler.");
            }
        }
    }

    private static List<string> MissingDocumentation(EndpointMetadataCollection metadata)
    {
        List<string> missing = [];
        if (metadata.GetMetadata<IEndpointNameMetadata>() is null)
        {
            missing.Add("a stable name (WithName)");
        }

        if (metadata.GetMetadata<ITagsMetadata>() is null)
        {
            missing.Add("a tag (WithTags)");
        }

        if (metadata.GetMetadata<IEndpointSummaryMetadata>() is null)
        {
            missing.Add("a summary (WithSummary)");
        }

        if (metadata.GetMetadata<IEndpointDescriptionMetadata>() is null)
        {
            missing.Add("a description (WithDescription)");
        }

        if (!metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Any())
        {
            missing.Add("response metadata (Produces)");
        }

        return missing;
    }

    /// <summary>
    /// Walks a contract type through generics, arrays, and the properties of the application's own types, and returns
    /// every type declared under Persistence. Framework types are not descended into except through their generic
    /// arguments, so <c>Task&lt;Ok&lt;Response&gt;&gt;</c> reaches <c>Response</c>.
    /// </summary>
    private static IEnumerable<Type> PersistenceTypesIn(Type contract, string persistence, string root)
    {
        HashSet<Type> visited = [];
        Stack<Type> pending = new([contract]);

        while (pending.TryPop(out Type? type))
        {
            if (type.IsGenericParameter || !visited.Add(type))
            {
                continue;
            }

            if (Namespaces.IsWithin(type.Namespace, persistence))
            {
                yield return type;
                continue;
            }

            if (type.GetElementType() is { } element)
            {
                pending.Push(element);
            }

            foreach (Type argument in type.IsGenericType ? type.GetGenericArguments() : [])
            {
                pending.Push(argument);
            }

            if (Namespaces.IsWithin(type.Namespace, root) && !type.IsEnum)
            {
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    pending.Push(property.PropertyType);
                }
            }
        }
    }

    private static bool IsDatabaseAccess(Type type) =>
        typeof(DbContext).IsAssignableFrom(type)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbSet<>));

    private static string Describe(RouteEndpoint endpoint)
    {
        string methods = string.Join(",", endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["ANY"]);
        return $"{methods} {endpoint.RoutePattern.RawText}";
    }
}
