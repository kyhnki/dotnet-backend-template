using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DotNetBackendTemplate.IntegrationTests.Documentation;

public sealed class SwaggerUiTests
{
    [Fact]
    public async Task DevelopmentExposesSwaggerUiForBuiltInOpenApiDocument()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Development");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage indexResponse = await client.GetAsync("/docs/index.html");
        string index = await indexResponse.Content.ReadAsStringAsync();
        HttpResponseMessage configurationResponse = await client.GetAsync("/docs/index.js");
        string configuration = await configurationResponse.Content.ReadAsStringAsync();

        indexResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        indexResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        index.Should().Contain("<title>.NET Backend Template API</title>");
        index.Should().Contain("index.js");
        configurationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        configuration.Should().Contain("/openapi/v1.json");
    }

    [Fact]
    public async Task ProductionDoesNotExposeSwaggerUiOrOpenApiDocument()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Production");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage uiResponse = await client.GetAsync("/docs/index.html");
        HttpResponseMessage documentResponse = await client.GetAsync("/openapi/v1.json");

        uiResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        documentResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(
                "ConnectionStrings:Database",
                "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("Authentication:Authority", "https://identity.test");
            builder.UseSetting("Authentication:Audience", "dotnet-backend-template");
        });
}
