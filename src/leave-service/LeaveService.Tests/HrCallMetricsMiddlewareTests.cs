using Microsoft.AspNetCore.Http;
using LeaveService.Api.Middleware;
using LeaveService.Infrastructure.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LeaveService.Tests;

public sealed class HrCallMetricsMiddlewareTests
{
    [Fact]
    public async Task AddsHrCallMetricsBeforeResponseStarts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LoadTest:EnableHrCallMetrics"] = "true"
            })
            .Build();
        var metrics = new HrCallMetrics(configuration);
        var context = new DefaultHttpContext();
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var middleware = new HrCallMetricsMiddleware(async httpContext =>
        {
            metrics.Record(12.5);
            metrics.Record(25.75);

            // Kestrel runs OnStarting callbacks immediately before committing headers.
            await httpContext.Response.StartAsync();
            await httpContext.Response.WriteAsync("ok");
        });

        await middleware.InvokeAsync(context, metrics);

        Assert.Equal("2", context.Response.Headers["X-HR-Call-Count"].ToString());
        Assert.Equal("12.50,25.75", context.Response.Headers["X-HR-Call-Durations-Ms"].ToString());

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        Assert.Equal("ok", await reader.ReadToEndAsync());
    }
}
