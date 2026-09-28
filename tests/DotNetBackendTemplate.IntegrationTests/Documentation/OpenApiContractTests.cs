using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetBackendTemplate.IntegrationTests.Documentation;

/// <summary>
/// Each API operation's request and response schemas must describe the types the endpoint actually binds and returns,
/// and distinct contract types must not share one schema. Every slice nests types named Request, Response, or Item,
/// and a schema naming scheme based on the simple type name collapses them into one schema, so an operation ends up
/// documented with another operation's payload and generated clients are wrong.
/// </summary>
public sealed partial class OpenApiContractTests
{
    private static readonly string RootNamespace = typeof(Program).Assembly.GetName().Name!;

    [Fact]
    public async Task EveryOperationDocumentsItsOwnContractTypes()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));
        JsonElement paths = document.RootElement.GetProperty("paths");
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        List<string> mismatches = [];
        Dictionary<string, HashSet<Type>> typesBySchema = new(StringComparer.Ordinal);
        int compared = 0;

        foreach (RouteEndpoint endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            string route = endpoint.RoutePattern.RawText ?? string.Empty;
            if (!route.StartsWith("/api/", StringComparison.Ordinal))
            {
                continue;
            }

            // OpenAPI paths drop route constraints ({id:guid} -> {id}) and trailing slashes.
            string path = RouteConstraint().Replace(route, "{${name}}").TrimEnd('/');
            foreach (string method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
            {
                if (!paths.TryGetProperty(path, out JsonElement pathItem)
                    || !pathItem.TryGetProperty(method.ToLowerInvariant(), out JsonElement operation))
                {
                    mismatches.Add($"{method} {route} is not in the OpenAPI document");
                    continue;
                }

                // UseStatusCodePages and the exception handler turn every error into Problem Details, so the
                // document must say so; a bare Produces(404) would describe an empty body the API never sends.
                foreach (JsonProperty response in operation.GetProperty("responses").EnumerateObject())
                {
                    bool isError = int.TryParse(response.Name, CultureInfo.InvariantCulture, out int code) && code >= 400;
                    bool isProblem = response.Value.TryGetProperty("content", out JsonElement content)
                        && content.TryGetProperty("application/problem+json", out _);

                    if (isError && !isProblem)
                    {
                        mismatches.Add($"{method} {path} {response.Name}: error responses must be documented as application/problem+json");
                    }
                }

                // Produces<T>() and a typed result such as Ok<T> both declare the same response, so compare each once.
                foreach (IProducesResponseTypeMetadata produces in endpoint.Metadata
                    .GetOrderedMetadata<IProducesResponseTypeMetadata>()
                    .DistinctBy(produces => (produces.StatusCode, produces.Type)))
                {
                    if (produces is { StatusCode: >= 200 and < 300, Type: { } responseType } && IsContract(responseType))
                    {
                        JsonElement schema = operation.GetProperty("responses")
                            .GetProperty(produces.StatusCode.ToString(CultureInfo.InvariantCulture))
                            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
                        Compare($"{method} {path} {produces.StatusCode}", responseType, schema, schemas, mismatches, typesBySchema, []);
                        compared++;
                    }
                }

                if (endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType is { } requestType && IsContract(requestType))
                {
                    JsonElement schema = operation.GetProperty("requestBody")
                        .GetProperty("content").GetProperty("application/json").GetProperty("schema");
                    Compare($"{method} {path} request body", requestType, schema, schemas, mismatches, typesBySchema, []);
                    compared++;
                }
            }
        }

        foreach ((string schema, HashSet<Type> types) in typesBySchema.Where(pair => pair.Value.Count > 1))
        {
            mismatches.Add($"schema '{schema}' is shared by {string.Join(", ", types.Select(type => type.FullName).Order(StringComparer.Ordinal))}");
        }

        compared.Should().BePositive("the check would pass vacuously if no operation had a documented contract type");
        if (mismatches.Count > 0)
        {
            Assert.Fail(
                "The OpenAPI document does not describe the contract types the endpoints use:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, mismatches.Select(mismatch => $"  {mismatch}")));
        }
    }

    private static void Compare(
        string location,
        Type type,
        JsonElement schema,
        JsonElement components,
        List<string> mismatches,
        Dictionary<string, HashSet<Type>> typesBySchema,
        HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            string name = reference.GetString()!.Split('/')[^1];
            if (!typesBySchema.TryGetValue(name, out HashSet<Type>? types))
            {
                typesBySchema[name] = types = [];
            }

            types.Add(type);
            schema = components.GetProperty(name);
        }

        Dictionary<string, PropertyInfo> actual = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name), StringComparer.Ordinal);

        HashSet<string> documented = schema.TryGetProperty("properties", out JsonElement properties)
            ? properties.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            : [];

        if (!documented.SetEquals(actual.Keys))
        {
            mismatches.Add(
                $"{location}: documented as {{{string.Join(", ", documented.Order(StringComparer.Ordinal))}}} "
                + $"but {type.FullName} has {{{string.Join(", ", actual.Keys.Order(StringComparer.Ordinal))}}}");
            return;
        }

        foreach ((string name, PropertyInfo property) in actual)
        {
            if (ElementContract(property.PropertyType) is { } nested)
            {
                JsonElement propertySchema = properties.GetProperty(name);
                JsonElement itemSchema = propertySchema.TryGetProperty("items", out JsonElement items) ? items : propertySchema;
                Compare($"{location}.{name}", nested, itemSchema, components, mismatches, typesBySchema, visited);
            }
        }
    }

    private static Type? ElementContract(Type type)
    {
        Type candidate = Nullable.GetUnderlyingType(type)
            ?? type.GetElementType()
            ?? (type.IsGenericType && type.GetGenericArguments() is [Type single] && type != typeof(string) ? single : type);

        return IsContract(candidate) ? candidate : null;
    }

    private static bool IsContract(Type type) =>
        !type.IsEnum
        && !type.IsPrimitive
        && type.Namespace is { } ns
        && (ns == RootNamespace || ns.StartsWith(RootNamespace + ".", StringComparison.Ordinal));

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(
                "ConnectionStrings:Database",
                "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("Authentication:Authority", "https://identity.test");
            builder.UseSetting("Authentication:Audience", "dotnet-backend-template");
        });

    [GeneratedRegex(@"\{(?<name>\w+)(?::[^}]*)?\}")]
    private static partial Regex RouteConstraint();
}
