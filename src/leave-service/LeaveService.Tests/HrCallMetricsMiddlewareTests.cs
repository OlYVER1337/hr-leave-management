using LeaveService.Api.Middleware;
using LeaveService.Infrastructure.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
        var responseFeature = new TestHttpResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);

        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var middleware = new HrCallMetricsMiddleware(async httpContext =>
        {
            metrics.Record(12.5);
            metrics.Record(25.75);

            // Simulate the server firing OnStarting immediately before committing headers.
            await responseFeature.StartResponseAsync();
            await httpContext.Response.WriteAsync("ok");
        });

        await middleware.InvokeAsync(context, metrics);

        Assert.Equal("2", context.Response.Headers["X-HR-Call-Count"].ToString());
        Assert.Equal("12.50,25.75", context.Response.Headers["X-HR-Call-Durations-Ms"].ToString());

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        Assert.Equal("ok", await reader.ReadToEndAsync());
    }

    private sealed class TestHttpResponseFeature : HttpResponseFeature
    {
        private readonly Stack<(Func<object, Task> Callback, object State)> _onStarting = new();
        private bool _hasStarted;

        public override bool HasStarted => _hasStarted;

        public override void OnStarting(Func<object, Task> callback, object state)
        {
            ArgumentNullException.ThrowIfNull(callback);

            if (_hasStarted)
            {
                throw new InvalidOperationException("The response has already started.");
            }

            _onStarting.Push((callback, state));
        }

        public async Task StartResponseAsync()
        {
            if (_hasStarted)
            {
                return;
            }

            while (_onStarting.TryPop(out var callback))
            {
                await callback.Callback(callback.State);
            }

            if (Headers is HeaderDictionary headers)
            {
                headers.IsReadOnly = true;
            }

            _hasStarted = true;
        }
    }
}
