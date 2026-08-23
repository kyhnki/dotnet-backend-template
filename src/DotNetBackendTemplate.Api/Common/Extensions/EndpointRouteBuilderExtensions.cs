using DotNetBackendTemplate.Api.Features.Campaigns;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DotNetBackendTemplate.Api.Common.Extensions;

internal static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder campaigns = endpoints
            .MapGroup("/api/v1/campaigns")
            .WithTags("Campaigns")
            .RequireAuthorization();

        CreateCampaign.MapEndpoint(campaigns);
        GetCampaign.MapEndpoint(campaigns);
        GetCampaigns.MapEndpoint(campaigns);
        UpdateCampaign.MapEndpoint(campaigns);
        DeleteCampaign.MapEndpoint(campaigns);

        endpoints.MapGet("/health/live", CheckLiveAsync)
            .WithName("LiveHealth")
            .WithTags("Health")
            .WithSummary("Check whether the API process is live")
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/plain")
            .Produces<string>(StatusCodes.Status503ServiceUnavailable, contentType: "text/plain")
            .AllowAnonymous();

        endpoints.MapGet("/health/ready", CheckReadyAsync)
            .WithName("ReadyHealth")
            .WithTags("Health")
            .WithSummary("Check whether the API and SQL Server are ready")
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/plain")
            .Produces<string>(StatusCodes.Status503ServiceUnavailable, contentType: "text/plain")
            .AllowAnonymous();

        return endpoints;
    }

    private static Task<IResult> CheckLiveAsync(
        HttpContext httpContext,
        HealthCheckService healthCheckService,
        CancellationToken cancellationToken) =>
        CheckHealthAsync(
            httpContext,
            healthCheckService,
            _ => false,
            cancellationToken);

    private static Task<IResult> CheckReadyAsync(
        HttpContext httpContext,
        HealthCheckService healthCheckService,
        CancellationToken cancellationToken) =>
        CheckHealthAsync(
            httpContext,
            healthCheckService,
            registration => registration.Tags.Contains("ready"),
            cancellationToken);

    private static async Task<IResult> CheckHealthAsync(
        HttpContext httpContext,
        HealthCheckService healthCheckService,
        Func<HealthCheckRegistration, bool> predicate,
        CancellationToken cancellationToken)
    {
        HealthReport report = await healthCheckService.CheckHealthAsync(predicate, cancellationToken);
        int statusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        httpContext.Response.Headers.CacheControl = "no-store, no-cache";
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers.Expires = "Thu, 01 Jan 1970 00:00:00 GMT";

        return Results.Text(
            report.Status.ToString(),
            contentType: "text/plain",
            statusCode: statusCode);
    }
}
