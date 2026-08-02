using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DotNetBackendTemplate.Api.Infrastructure.Observability;

internal static class ObservabilityExtensions
{
    public static IServiceCollection AddApiObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string serviceName = configuration["OpenTelemetry:ServiceName"]
            ?? "DotNetBackendTemplate.Api";

        string? endpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        bool hasOtlpEndpoint = Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? otlpEndpoint);

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (hasOtlpEndpoint)
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = otlpEndpoint!);
                }
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (hasOtlpEndpoint)
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = otlpEndpoint!);
                }
            });

        return services;
    }
}
