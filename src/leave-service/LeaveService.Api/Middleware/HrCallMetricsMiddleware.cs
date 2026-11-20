using System.Globalization;
using LeaveService.Infrastructure.Clients;

namespace LeaveService.Api.Middleware;

/// <summary>
/// Exposes outbound HR call metrics as response headers when load-test metrics are enabled.
/// The headers are registered before the response starts so they are observable by k6.
/// </summary>
public sealed class HrCallMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, HrCallMetrics metrics)
    {
        if (!metrics.Enabled)
        {
            await next(context);
            return;
        }

        context.Response.OnStarting(static state =>
        {
            var (httpContext, requestMetrics) = ((HttpContext Context, HrCallMetrics Metrics))state;
            var durations = requestMetrics.GetDurations();

            httpContext.Response.Headers["X-HR-Call-Count"] =
                durations.Count.ToString(CultureInfo.InvariantCulture);
            httpContext.Response.Headers["X-HR-Call-Durations-Ms"] =
                string.Join(",", durations.Select(duration =>
                    duration.ToString("F2", CultureInfo.InvariantCulture)));

            return Task.CompletedTask;
        }, (context, metrics));

        await next(context);
    }
}
