using DotNetBackendTemplate.Api.Infrastructure.Authorization;
using DotNetBackendTemplate.Api.Persistence;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

namespace DotNetBackendTemplate.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _database = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-latest")
        .WithCreateParameterModifier(parameters => parameters.Platform = "linux/amd64")
        .Build();

    public TestTimeProvider Clock { get; } = new(
        new DateTimeOffset(2026, 8, 1, 12, 34, 56, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // ConfigureWebHost runs when the host is built, after InitializeAsync started the container.
        builder.UseSetting("ConnectionStrings:Database", _database.GetConnectionString());
        builder.UseSetting("Authentication:Authority", "https://identity.test");
        builder.UseSetting("Authentication:Audience", "dotnet-backend-template");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        using IServiceScope scope = Services.CreateScope();
        AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _database.DisposeAsync();
        Dispose();
    }

    public HttpClient CreateAuthenticatedClient(string? role = ApiRoles.CampaignManager)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                TestAuthenticationHandler.SchemeName,
                role);
        return client;
    }
}
