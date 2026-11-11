using System.Globalization;
using LeaveService.Infrastructure.Clients;

namespace LeaveService.Api.Middleware;

/// <summary>
/// Exposes outbound HR call metrics as response headers when load-test metrics are enabled.
/// Normal environments do not emit these headers.
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

        try
        {
            await next(context);
        }
        finally
        {
            if (!context.Response.HasStarted)
            {
                var durations = metrics.GetDurations();
                context.Response.Headers["X-HR-Call-Count"] =
                    durations.Count.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-HR-Call-Durations-Ms"] = metrics.GetDurationsHeader();
            }
        }
    }
}
