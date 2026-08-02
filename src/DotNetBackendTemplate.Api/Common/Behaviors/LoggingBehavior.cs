using System.Diagnostics;
using MediatR;

namespace DotNetBackendTemplate.Api.Common.Behaviors;

internal sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            TResponse response = await next(cancellationToken);
            stopwatch.Stop();

            LogHandled(
                logger,
                requestName,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            LogFailed(
                logger,
                exception,
                requestName,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Handled {RequestName} in {ElapsedMilliseconds} ms")]
    private static partial void LogHandled(
        ILogger logger,
        string requestName,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Failed {RequestName} after {ElapsedMilliseconds} ms")]
    private static partial void LogFailed(
        ILogger logger,
        Exception exception,
        string requestName,
        long elapsedMilliseconds);
}
