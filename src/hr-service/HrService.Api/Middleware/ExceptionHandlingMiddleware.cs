using System.Globalization;
using HrService.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request was cancelled by the client.");
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            var (statusCode, problem) = CreateProblem(exception);

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                logger.LogError(exception, "An unhandled exception occurred while processing the request.");
            }
            else
            {
                logger.LogWarning(
                    exception,
                    "Request failed with status code {StatusCode}.",
                    statusCode);
            }

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                problem,
                cancellationToken: context.RequestAborted);
        }
    }

    private static (int StatusCode, ProblemDetails Problem) CreateProblem(Exception exception)
    {
        if (exception is RequestValidationException validationException)
        {
            var statusCode = GetStatusCode(validationException, StatusCodes.Status422UnprocessableEntity);
            return (statusCode, new ValidationProblemDetails(
                validationException.Errors.ToDictionary(error => error.Key, error => error.Value))
            {
                Title = validationException.Code,
                Status = statusCode
            });
        }

        if (exception is AppException appException)
        {
            var statusCode = GetStatusCode(appException, StatusCodes.Status500InternalServerError);
            return (statusCode, new ProblemDetails
            {
                Title = appException.Code,
                Detail = appException.Message,
                Status = statusCode
            });
        }

        return (StatusCodes.Status500InternalServerError, new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError
        });
    }

    private static int GetStatusCode(AppException exception, int fallbackStatusCode) =>
        int.TryParse(
            exception.StatusCode,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var statusCode)
            ? statusCode
            : fallbackStatusCode;
}
