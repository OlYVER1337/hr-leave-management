using System.Net;
using LeaveService.Infrastructure.Clients;
using Xunit;

namespace LeaveService.Tests;

public sealed class HrServiceClientTests
{
    // Các lookup leave type, policy và holiday dùng authoritative=true để HR đọc SQL thay vì Redis cache.
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
        await client.GetLeavePoliciesAsync(2026, new DateOnly(2026, 12, 31), "Bearer test-token");
        await client.GetHolidaysAsync(
            new DateOnly(2026, 12, 1),
            new DateOnly(2026, 12, 31),
            "Bearer test-token");

        Assert.Equal(
            [
                "/api/v1/leave-types/4?authoritative=true",
                "/api/v1/leave-policies?year=2026&asOfDate=2026-12-31&authoritative=true",
                "/api/v1/holidays?from=2026-12-01&to=2026-12-31&authoritative=true"
            ],
            handler.RequestedUris);
    }

    // Kiểm tra Leave gọi đúng API catalog gộp của HR và yêu cầu authoritative để bỏ qua Redis cache.
    [Fact]
    public async Task GetLeaveCatalog_UsesAuthoritativeCatalogEndpoint()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://hr-service")
        };
        var client = new HrServiceClient(httpClient);

        // Handler giả trả JSON catalog; test tập trung vào URL và khả năng deserialize response.
        var catalog = await client.GetLeaveCatalogAsync(2027, "Bearer test-token");

        Assert.Equal("ANNUAL", Assert.Single(catalog.LeaveTypes).Code);
        Assert.Equal(15m, Assert.Single(catalog.LeavePolicies).DaysPerYear);
        // Xác nhận request có đúng year và authoritative=true, tránh gọi nhầm endpoint dùng cache.
        Assert.Equal(
            "/api/v1/leave-catalog?year=2027&authoritative=true",
            Assert.Single(handler.RequestedUris));
    }

    // Leave chỉ cần danh sách employee ID của team nên gọi leave-scope endpoint, không lấy toàn bộ hồ sơ nhân viên.
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
                "/api/v1/leave-catalog" =>
                    """{"leaveTypes":[{"id":1,"code":"ANNUAL","name":"Annual","isPaid":true,"deductsBalance":true,"isActive":true}],"leavePolicies":[{"id":12,"leaveTypeId":1,"leaveTypeCode":"ANNUAL","leaveTypeName":"Annual","year":2027,"daysPerYear":15}]}""",
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
