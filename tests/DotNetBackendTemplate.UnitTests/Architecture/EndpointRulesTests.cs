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

/// <summary>
/// SEC-01, SEC-02, SEC-06, SLICE-02, SLICE-04, SLICE-07, SLICE-08, and the contract half of TIME-02: what every
/// endpoint declares, accepts, and returns, and that every slice is actually mapped.
/// </summary>
public sealed class EndpointRulesTests(ProductionEndpoints production) : IClassFixture<ProductionEndpoints>
{
    [Fact]
    public async Task ProductionEndpointsFollowEndpointRules()
    {
        List<RouteEndpoint> endpoints = [.. production.Endpoints.OfType<RouteEndpoint>()];

        // An empty list, or handlers that cannot be inspected, would make these rules pass vacuously.
        endpoints.Should().NotBeEmpty();
        endpoints.Where(EndpointRules.IsBusinessEndpoint).Should().OnlyContain(
            endpoint => endpoint.Metadata.GetMetadata<MethodInfo>() != null,
            "SLICE-02, SLICE-04, SLICE-08, and TIME-02 inspect each endpoint's handler");

        IAuthorizationPolicyProvider policies = production.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        ArchitectureAssert.NoViolations(
            EndpointRules.Check(endpoints, ProductionCode.RootNamespace)
                .Concat(await EndpointRules.CheckPoliciesAsync(endpoints, policies))
                .Concat(await EndpointRules.CheckRegistrationAsync(ProductionCode.Assembly.GetTypes(), endpoints, ProductionCode.RootNamespace)));
    }

    [Theory]
    [InlineData("SEC-01", "/api/unprotected")]
    [InlineData("SEC-02", "/api/inline-role")]
    [InlineData("SEC-06", "/api/missing-policy")]
    [InlineData("SLICE-02", "/api/database")]
    [InlineData("SLICE-04", "/api/single-order")]
    [InlineData("SLICE-04", "/api/order-page")]
    [InlineData("SLICE-04", "/api/order-status")]
    [InlineData("SLICE-04", "/api/order-import")]
    [InlineData("SLICE-07", "/api/unprotected")]
    [InlineData("SLICE-08", "UnregisteredSlice")]
    [InlineData("TIME-02", "/api/dated")]
    public async Task EndpointRulesCatchDeliberateViolations(string ruleId, string subject)
    {
        await using WebApplication app = ViolatingEndpoints();
        List<Endpoint> endpoints = [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)];
        IEnumerable<Type> fixtureTypes = typeof(FixtureRoot).Assembly
            .GetTypes()
            .Where(type => Namespaces.IsWithin(type.Namespace, FixtureRoot.Namespace));

        List<ArchitectureViolation> violations =
        [
            .. EndpointRules.Check(endpoints, FixtureRoot.Namespace),
            .. await EndpointRules.CheckPoliciesAsync(endpoints, app.Services.GetRequiredService<IAuthorizationPolicyProvider>()),
            .. await EndpointRules.CheckRegistrationAsync(fixtureTypes, endpoints, FixtureRoot.Namespace),
        ];

        ArchitectureAssert.Reports(violations, ruleId, subject);
    }

    private static WebApplication ViolatingEndpoints()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddAuthorization();
        WebApplication app = builder.Build();

        app.MapGet("/api/unprotected", () => "no authorization decision and no metadata");
        app.MapGet("/api/inline-role", () => "role string instead of a policy constant")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Administrator" });
        app.MapGet("/api/missing-policy", () => "policy name that is never registered")
            .RequireAuthorization("MissingPolicy");
        app.MapGet("/api/single-order", () => TypedResults.Ok(new OrderResponse(new Order()))).RequireAuthorization();
        app.MapGet("/api/order-page", () => TypedResults.Ok(new OrderPage(Array.Empty<Order>()))).RequireAuthorization();
        app.MapGet("/api/order-status", () => TypedResults.Ok(new OrderStatusResponse(OrderStatus.Open))).RequireAuthorization();
        app.MapPost("/api/order-import", (Order order) => TypedResults.Ok(order.Id)).RequireAuthorization();
        app.MapGet("/api/database", ([FromServices] ViolatingDbContext database) => TypedResults.Ok(database.ContextId.InstanceId))
            .RequireAuthorization();
        app.MapGet("/api/dated", () => TypedResults.Ok(new DatedResponse(DateTime.UnixEpoch))).RequireAuthorization();

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

    public IServiceProvider Services => _factory.Services;

    public IReadOnlyList<Endpoint> Endpoints => Services.GetRequiredService<EndpointDataSource>().Endpoints;

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

            foreach (IAuthorizeData authorization in metadata.GetOrderedMetadata<IAuthorizeData>().Where(data => !string.IsNullOrEmpty(data.Roles)))
            {
                yield return new("SEC-02", subject,
                    $"requires the inline role string '{authorization.Roles}'. Reference a policy constant from Infrastructure/Authorization instead.");
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

                foreach (Type type in ReachableTypes(parameter.ParameterType, persistence, root))
                {
                    if (Namespaces.IsWithin(type.Namespace, persistence))
                    {
                        yield return new("SLICE-04", subject,
                            $"binds {type.FullName} from the request through '{parameter.Name}'. Bind an API-owned request type instead; binding persistence types allows mass assignment.");
                    }
                    else if (type == typeof(DateTime))
                    {
                        yield return new("TIME-02", subject,
                            $"accepts a DateTime through '{parameter.Name}'. Contracts use UTC DateTimeOffset (ADR-0003).");
                    }
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

            foreach (Type type in results.SelectMany(result => ReachableTypes(result, persistence, root)).Distinct())
            {
                if (Namespaces.IsWithin(type.Namespace, persistence))
                {
                    yield return new("SLICE-04", subject,
                        $"returns {type.FullName}. Responses must use API-owned types; map persistence types in the handler.");
                }
                else if (type == typeof(DateTime))
                {
                    yield return new("TIME-02", subject, "returns a DateTime. Contracts use UTC DateTimeOffset (ADR-0003).");
                }
            }
        }
    }

    /// <summary>SEC-06: a misspelled policy name compiles, then throws on the first request that needs it.</summary>
    public static async Task<IReadOnlyList<ArchitectureViolation>> CheckPoliciesAsync(
        IEnumerable<Endpoint> endpoints,
        IAuthorizationPolicyProvider policies)
    {
        List<ArchitectureViolation> violations = [];

        foreach (RouteEndpoint endpoint in endpoints.OfType<RouteEndpoint>())
        {
            foreach (string policy in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).OfType<string>())
            {
                if (await policies.GetPolicyAsync(policy) is null)
                {
                    violations.Add(new("SEC-06", Describe(endpoint),
                        $"requires policy '{policy}', which is not registered, so the first request would throw. Register it in AddAuthorizationBuilder and reference its constant."));
                }
            }
        }

        return violations;
    }

    /// <summary>SLICE-08: a slice whose MapEndpoint is never called has no endpoint, and nothing else notices.</summary>
    public static async Task<IReadOnlyList<ArchitectureViolation>> CheckRegistrationAsync(IEnumerable<Type> types, IEnumerable<Endpoint> endpoints, string root)
    {
        List<ArchitectureViolation> violations = [];
        List<Endpoint> mapped = [.. endpoints];

        foreach (Type slice in types.Where(type => Namespaces.IsWithin(type.Namespace, $"{root}.Features")))
        {
            MethodInfo? mapping = slice.GetMethod("MapEndpoint", BindingFlags.Public | BindingFlags.Static);
            if (mapping is null)
            {
                continue;
            }

            if (mapping.GetParameters() is not [{ ParameterType: var parameterType }] || parameterType != typeof(RouteGroupBuilder))
            {
                violations.Add(new("SLICE-08", slice.FullName!, "MapEndpoint must take one RouteGroupBuilder; review a different mapping convention explicitly."));
                continue;
            }

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            // Endpoint construction only checks service registration; no handler or database is executed.
            builder.Services.AddSingleton<MediatR.ISender>(_ => throw new InvalidOperationException("Mapping probes never dispatch requests."));
            await using WebApplication probe = builder.Build();
            mapping.Invoke(null, [probe.MapGroup(string.Empty)]);
            List<Endpoint> expected = [.. ((IEndpointRouteBuilder)probe).DataSources.SelectMany(source => source.Endpoints)];
            if (expected.Count == 0 || expected.Any(candidate => !mapped.Any(actual => SameHandler(candidate, actual))))
            {
                violations.Add(new("SLICE-08", slice.FullName!,
                    "defines MapEndpoint, but its handler/HTTP-method identities are absent from the application. Register every mapped operation; the delegate may live in a separate top-level type."));
            }
        }

        return violations;
    }

    private static bool SameHandler(Endpoint expected, Endpoint actual) =>
        expected.Metadata.GetMetadata<MethodInfo>() is { } method
        && method == actual.Metadata.GetMetadata<MethodInfo>()
        && (expected.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
            .SequenceEqual(actual.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [], StringComparer.Ordinal);

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
    /// Every type a contract reaches through generics, arrays, and the properties of the application's own types.
    /// Framework types are not descended into except through their generic arguments, so <c>Task&lt;Ok&lt;T&gt;&gt;</c>
    /// reaches <c>T</c>, and persistence types are reported rather than descended into.
    /// </summary>
    private static IEnumerable<Type> ReachableTypes(Type contract, string persistence, string root)
    {
        HashSet<Type> visited = [];
        Stack<Type> pending = new([contract]);

        while (pending.TryPop(out Type? type))
        {
            if (type.IsGenericParameter || !visited.Add(type))
            {
                continue;
            }

            yield return type;

            if (Namespaces.IsWithin(type.Namespace, persistence))
            {
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
