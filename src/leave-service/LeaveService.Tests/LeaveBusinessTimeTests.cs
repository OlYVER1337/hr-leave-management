using LeaveService.Application.Abstractions;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using LeaveService.Domain.Entities;
using Moq;
using Xunit;

namespace LeaveService.Tests;

// Ngày "hôm nay" theo giờ Việt Nam: 00:00–07:00 VN vẫn là ngày trước theo UTC.
public sealed class LeaveBusinessTimeTests
{
    private const string Auth = "Bearer token";

    private static LeaveRequestService Create(DateTimeOffset utcNow, out Mock<ILeaveRequestRepository> repo)
    {
        repo = new Mock<ILeaveRequestRepository>();
        var hr = new Mock<ILeaveHrClient>();
        hr.Setup(h => h.GetEmployeeAsync(It.IsAny<Guid>(), Auth, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new HrEmployeeSnapshot(Guid.Empty, "E", "E", "Employee", true, 1, null));
        hr.Setup(h => h.GetLeaveTypeAsync(It.IsAny<int>(), Auth, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new HrLeaveType(1, "ANNUAL", "Phép năm", true, true, true));
        hr.Setup(h => h.GetHolidaysAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), Auth, It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<HrHoliday>());
        hr.Setup(h => h.GetLeavePoliciesAsync(It.IsAny<int>(), Auth, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new[] { new HrLeavePolicy(1, 1, "ANNUAL", "Phép năm", 2026, 12m) });
        return new LeaveRequestService(repo.Object, hr.Object, new FixedUtcClock(utcNow));
    }

    private static Task<LeaveRequestResponse> Submit(LeaveRequestService service, DateOnly start) =>
        service.CreateAsync(Guid.NewGuid(), Auth,
            new CreateLeaveRequest(1, start, start, "test"));

    [Theory]
    // 28/10 18:30 UTC = 29/10 01:30 VN: "hôm qua" theo VN là 28/10 -> phải bị từ chối
    [InlineData("2026-10-28T18:30:00+00:00", "2026-10-28", false)]
    // 29/10 01:30 VN: hôm nay 29/10 -> chấp nhận
    [InlineData("2026-10-28T18:30:00+00:00", "2026-10-29", true)]
    // 28/10 23:30 VN (16:30 UTC): hôm nay 28/10 -> chấp nhận
    [InlineData("2026-10-28T16:30:00+00:00", "2026-10-28", true)]
    // 28/10 06:59 VN (23:59 UTC 27/10): hôm nay 28/10 -> 27/10 bị từ chối, 28/10 chấp nhận
    [InlineData("2026-10-27T23:59:00+00:00", "2026-10-27", false)]
    [InlineData("2026-10-27T23:59:00+00:00", "2026-10-28", true)]
    // 28/10 07:00 VN (00:00 UTC 28/10): ranh giới ngày mới theo VN
    [InlineData("2026-10-28T00:00:00+00:00", "2026-10-27", false)]
    [InlineData("2026-10-28T00:00:00+00:00", "2026-10-28", true)]
    public async Task StartDate_UsesBusinessDay_NotUtcDay(string utcNow, string startDate, bool accepted)
    {
        var service = Create(DateTimeOffset.Parse(utcNow), out var repo);
        repo.Setup(r => r.CreateAsync(It.IsAny<LeaveRequest>(), It.IsAny<IReadOnlyList<AnnualLeaveAllocation>>(),
                It.IsAny<LeaveRequestHistory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((LeaveRequest)new LeaveRequest(), (IReadOnlyList<LeaveBalance>)Array.Empty<LeaveBalance>()));
        var start = DateOnly.Parse(startDate);

        if (accepted)
        {
            await Submit(service, start);
        }
        else
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Submit(service, start));
        }
    }

    private sealed class FixedUtcClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
