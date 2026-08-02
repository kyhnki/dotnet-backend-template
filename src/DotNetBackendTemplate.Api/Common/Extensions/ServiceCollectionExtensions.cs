using System.Globalization;
using DotNetBackendTemplate.Api.Common.Behaviors;
using DotNetBackendTemplate.Api.Common.Exceptions;
using DotNetBackendTemplate.Api.Infrastructure.Authentication;
using DotNetBackendTemplate.Api.Infrastructure.Observability;
using DotNetBackendTemplate.Api.Infrastructure.OpenApi;
using DotNetBackendTemplate.Api.Persistence;
using DotNetBackendTemplate.Api.Persistence.Auditing;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

namespace DotNetBackendTemplate.Api.Common.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        string connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Use user secrets or ConnectionStrings__Database.");

        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options
                .UseSqlServer(connectionString, sqlOptions =>
                    sqlOptions.EnableRetryOnFailure())
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssemblyContaining<Program>();
            options.AddOpenBehavior(typeof(ValidationBehavior<,>));
            options.AddOpenBehavior(typeof(LoggingBehavior<,>));
        });

        services.AddValidatorsFromAssemblyContaining<Program>();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(options =>
        {
            CultureInfo[] supportedCultures =
            [
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("en"),
                CultureInfo.GetCultureInfo("tr-TR"),
                CultureInfo.GetCultureInfo("tr"),
            ];

            options.DefaultRequestCulture = new RequestCulture("en-US");
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
            options.RequestCultureProviders =
            [
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });
        services.AddApiAuthentication(configuration, environment);
        services.AddApiObservability(configuration);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] =
                    System.Diagnostics.Activity.Current?.Id
                    ?? context.HttpContext.TraceIdentifier;
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddApiOpenApi();
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("sqlserver", tags: ["ready"]);

        return services;
    }
}
