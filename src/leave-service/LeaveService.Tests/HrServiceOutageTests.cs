using System.Net;
using System.Text.Json;
using LeaveService.Api.Middleware;
using LeaveService.Application.Common;
using LeaveService.Infrastructure.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeaveService.Tests;

public sealed class HrServiceOutageTests
{
    [Fact]
    public async Task HrServiceClient_MapsConnectionFailureToDependencyUnavailable()
    {
        using var client = new HttpClient(new FailingHttpMessageHandler())
        {
            BaseAddress = new Uri("http://hr-service")
        };
        var hrClient = new HrServiceClient(client);

        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            hrClient.GetEmployeeAsync(Guid.NewGuid(), "Bearer test"));

        Assert.Equal("HR service is temporarily unavailable.", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task HrServiceClient_MapsHttpServiceUnavailableToDependencyUnavailable()
    {
        using var client = new HttpClient(new RespondingHttpMessageHandler(
            HttpStatusCode.ServiceUnavailable))
        {
            BaseAddress = new Uri("http://hr-service")
        };
        var hrClient = new HrServiceClient(client);

        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            hrClient.GetEmployeeAsync(Guid.NewGuid(), "Bearer test"));

        Assert.Equal("HR service is temporarily unavailable.", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_ReturnsProblemDetails503ForDependencyOutage()
    {
        using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = responseBody;
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DependencyUnavailableException("HR service"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            context.Response.ContentType,
            StringComparison.OrdinalIgnoreCase);
        responseBody.Position = 0;
        using var problem = await JsonDocument.ParseAsync(responseBody);
        Assert.Equal("Dependency unavailable", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            problem.RootElement.GetProperty("status").GetInt32());
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }

    private sealed class RespondingHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
