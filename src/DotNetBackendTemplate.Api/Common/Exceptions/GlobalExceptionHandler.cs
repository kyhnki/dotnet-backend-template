using System.Text.Json;
using DotNetBackendTemplate.Api.Infrastructure.Localization;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace DotNetBackendTemplate.Api.Common.Exceptions;

internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IStringLocalizer<ApiResources> localizer,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException
            && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestCanceled(logger);
            return true;
        }

        ProblemDetails problemDetails;

        if (exception is ValidationException validationException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            problemDetails = new ValidationProblemDetails(ToErrorDictionary(validationException))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = localizer["ValidationErrorTitle"],
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            };
        }
        else
        {
            LogUnhandledException(logger, exception);
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = localizer["UnexpectedErrorTitle"],
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            };
        }

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });

        return true;
    }

    private static Dictionary<string, string[]> ToErrorDictionary(
        ValidationException validationException) => validationException.Errors
        .GroupBy(error => JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName))
        .ToDictionary(
            group => group.Key,
            group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Error,
        Message = "An unhandled exception occurred")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "The request was canceled by the client")]
    private static partial void LogRequestCanceled(ILogger logger);
}
