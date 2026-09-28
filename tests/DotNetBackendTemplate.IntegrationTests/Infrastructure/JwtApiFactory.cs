using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DotNetBackendTemplate.IntegrationTests.Infrastructure;

public sealed class JwtApiFactory : IAsyncLifetime, IAsyncDisposable
{
    private const string Issuer = "https://jwt.integration.test";
    private const string Audience = "dotnet-backend-template";
    private readonly ApiFactory _database = new();
    private readonly RSA _rsa = RSA.Create(2048);
    private WebApplicationFactory<Program>? _application;
    private bool _disposed;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        RsaSecurityKey verificationKey = new(_rsa.ExportParameters(false)) { KeyId = "integration-key" };
        OpenIdConnectConfiguration metadata = new() { Issuer = Issuer };
        metadata.SigningKeys.Add(verificationKey);
        _application = _database.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
            });
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Only discovery/key transport is replaced. The real handler performs signature, issuer,
                // audience, lifetime and role validation; no custom signature/lifetime validators are used.
                options.Authority = Issuer;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
        }));
    }

    public HttpClient CreateClient() => _application!.CreateClient();

    public string CreateToken(string scenario)
    {
        using RSA wrongKey = RSA.Create(2048);
        RsaSecurityKey key = new(scenario == "signature" ? wrongKey : _rsa) { KeyId = "integration-key" };
        // JwtBearer validates real wall-clock lifetime. Wide margins avoid timing-dependent expiration tests.
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        DateTimeOffset expires = scenario == "expired" ? now.AddHours(-1) : now.AddHours(1);
        JwtSecurityToken token = new(
            issuer: scenario == "issuer" ? "https://wrong.integration.test" : Issuer,
            audience: scenario == "audience" ? "wrong-audience" : Audience,
            claims: [new Claim("sub", "jwt-test-user"), new Claim("role", scenario == "role" ? "Reader" : "CampaignManager")],
            notBefore: now.AddHours(-2).UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }

        await ((IAsyncLifetime)_database).DisposeAsync();
        _rsa.Dispose();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());
}
