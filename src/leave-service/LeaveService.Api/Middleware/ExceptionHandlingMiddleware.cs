using LeaveService.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace LeaveService.Api.Middleware;

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
        if (exception is LeaveValidationException validationException)
        {
            var problem = new ProblemDetails
            {
                Title = validationException.Code,
                Detail = validationException.Message,
                Status = validationException.StatusCode
            };
            problem.Extensions["errors"] = validationException.Errors;
            return (validationException.StatusCode, problem);
        }

        if (exception is LeaveAppException appException)
        {
            return (appException.StatusCode, new ProblemDetails
            {
                Title = appException.Code,
                Detail = appException.Message,
                Status = appException.StatusCode
            });
        }

        if (exception is DependencyUnavailableException)
        {
            return (StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Dependency unavailable",
                Detail = "A required service is temporarily unavailable.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }

        return (StatusCodes.Status500InternalServerError, new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError
        });
    }
}
