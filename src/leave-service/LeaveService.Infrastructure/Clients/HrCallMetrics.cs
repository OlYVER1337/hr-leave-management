using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace LeaveService.Infrastructure.Clients;

/// <summary>
/// Request-scoped telemetry for outbound calls from Leave Service to HR Service.
/// It is enabled only in an explicitly configured load-test environment.
/// </summary>
public sealed class HrCallMetrics
{
    private readonly ConcurrentQueue<double> _durationsMs = new();

    public bool Enabled { get; }

    public HrCallMetrics(IConfiguration configuration)
    {
        Enabled = configuration.GetValue<bool>("LoadTest:EnableHrCallMetrics");
    }

    public void Record(double elapsedMilliseconds)
    {
        if (Enabled && double.IsFinite(elapsedMilliseconds) && elapsedMilliseconds >= 0)
        {
            _durationsMs.Enqueue(elapsedMilliseconds);
        }
    }

    public IReadOnlyList<double> GetDurations() => _durationsMs.ToArray();

    public string GetDurationsHeader() =>
        string.Join(
            ",",
            _durationsMs.Select(duration =>
                duration.ToString("F2", CultureInfo.InvariantCulture)));
}
