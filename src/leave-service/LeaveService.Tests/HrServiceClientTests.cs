using System.Net;
using LeaveService.Infrastructure.Clients;
using Xunit;

namespace LeaveService.Tests;

public sealed class HrServiceClientTests
{
    [Fact]
    public async Task CatalogLookups_RequestAuthoritativeSqlReads()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://hr-service")
        };
        var client = new HrServiceClient(httpClient);

        await client.GetLeaveTypeAsync(4, "Bearer test-token");
        await client.GetLeavePoliciesAsync(2026, "Bearer test-token");
        await client.GetHolidaysAsync(
            new DateOnly(2026, 12, 1),
            new DateOnly(2026, 12, 31),
            "Bearer test-token");

        Assert.Equal(
            [
                "/api/v1/leave-types/4?authoritative=true",
                "/api/v1/leave-policies?year=2026&authoritative=true",
                "/api/v1/holidays?from=2026-12-01&to=2026-12-31&authoritative=true"
            ],
            handler.RequestedUris);
    }

    [Fact]
    public async Task GetTeamEmployeeIds_UsesLeaveScopeEndpointAndReturnsOnlyIds()
    {
        var managerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var expectedEmployeeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://hr-service")
        };
        var client = new HrServiceClient(httpClient);

        var employeeIds = await client.GetTeamEmployeeIdsAsync(managerId, "Bearer test-token");

        Assert.Equal(new[] { expectedEmployeeId }, employeeIds);
        Assert.Equal(
            $"/api/v1/leave-scope/managers/{managerId}/employee-ids",
            Assert.Single(handler.RequestedUris));
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<string> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!.PathAndQuery);
            var responseBody = request.RequestUri.AbsolutePath switch
            {
                "/api/v1/leave-types/4" =>
                    """{"id":4,"code":"ANNUAL","name":"Annual","isPaid":true,"deductsBalance":true,"isActive":true}""",
                "/api/v1/leave-policies" => "[]",
                "/api/v1/holidays" => "[]",
                _ when request.RequestUri.AbsolutePath.StartsWith(
                    "/api/v1/leave-scope/managers/",
                    StringComparison.Ordinal) =>
                    """["22222222-2222-2222-2222-222222222222"]""",
                _ => throw new InvalidOperationException(
                    $"Unexpected HR API request: {request.RequestUri.PathAndQuery}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });
        }
    }
}
