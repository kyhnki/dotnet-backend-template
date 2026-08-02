using DotNetBackendTemplate.Api.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace DotNetBackendTemplate.Api.Infrastructure.Authentication;

internal static class AuthenticationExtensions
{
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        IConfigurationSection section = configuration.GetSection("Authentication");
        string? authority = section["Authority"];
        string? audience = section["Audience"];

        if (!environment.IsDevelopment()
            && (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience)))
        {
            throw new InvalidOperationException(
                "Authentication:Authority and Authentication:Audience must be configured outside Development.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = string.IsNullOrWhiteSpace(authority) ? null : authority;
                options.Audience = string.IsNullOrWhiteSpace(audience) ? null : audience;
                options.RequireHttpsMetadata = section.GetValue("RequireHttpsMetadata", true);
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType =
                    section["NameClaimType"] ?? "name";
                options.TokenValidationParameters.RoleClaimType =
                    section["RoleClaimType"] ?? "role";
            });

        services
            .AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.CampaignWrite, policy =>
                policy.RequireRole(ApiRoles.Administrator, ApiRoles.CampaignManager));

        return services;
    }
}
