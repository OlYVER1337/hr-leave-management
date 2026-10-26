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
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = 499;
            }
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
                    "Request failed with status code {StatusCode}: {ErrorCode} - {Detail}",
                    statusCode,
                    problem.Title,
                    problem.Detail);
            }

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
        }
    }

    private static (int StatusCode, ProblemDetails Problem) CreateProblem(Exception exception)
    {
        if (exception is RequestValidationException validationException)
        {
            var statusCode = validationException.StatusCode;
            var problem = new ProblemDetails
            {
                Title = validationException.Code,
                Detail = validationException.Message,
                Status = statusCode
            };
            problem.Extensions["errors"] = validationException.Errors;

            return (statusCode, problem);
        }

        if (exception is AppException appException)
        {
            var statusCode = appException.StatusCode;
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

}
