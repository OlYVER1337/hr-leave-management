using LeaveService.Application.Abstractions;
using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using LeaveService.Domain.Entities;
using Moq;
using Xunit;

namespace LeaveService.Tests;

public sealed class LeaveRequestHistoryServiceTests
{
    // Người chỉ có Self đọc được lịch sử của đơn mình mà không cần gọi HR.
    [Fact]
    public async Task GetHistoryAsync_ForSelfScopeReturnsChronologicalHistory()
    {
        var employeeId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var history = new LeaveRequestHistory
        {
            Id = Guid.NewGuid(),
            LeaveRequestId = requestId,
            ActorId = employeeId,
            Action = "Created",
            ToStatus = LeaveRequestStatus.Pending,
            Note = null,
            CreatedAtUtc = new DateTime(2026, 11, 26, 1, 0, 0, DateTimeKind.Utc)
        };
        var access = SelfAccess();
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetVisibleHistoryAsync(
                employeeId,
                requestId,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LeaveRequestHistory>)[history]);

        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetHistoryAsync(employeeId, "Bearer employee", requestId, access);

        var response = Assert.Single(result);
        Assert.Equal(history.Id, response.Id);
        Assert.Equal(employeeId, response.ActorId);
        Assert.Equal("Created", response.Action);
        Assert.Null(response.FromStatus);
        Assert.Equal("Pending", response.ToStatus);
        Assert.Null(response.Note);
        Assert.Equal(history.CreatedAtUtc, response.CreatedAtUtc);
        hrClient.VerifyNoOtherCalls();
    }

    // Team scope phải dùng membership hiện tại do HR trả về, không dùng ApproverId snapshot.
    [Fact]
    public async Task GetHistoryAsync_ForTeamScopeUsesCurrentTeamFromHr()
    {
        var managerId = Guid.NewGuid();
        var teamEmployeeId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var access = ManagerAccess();
        var history = new LeaveRequestHistory
        {
            Id = Guid.NewGuid(),
            LeaveRequestId = requestId,
            ActorId = managerId,
            Action = "Approved",
            FromStatus = LeaveRequestStatus.Pending,
            ToStatus = LeaveRequestStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetVisibleHistoryAsync(
                managerId,
                requestId,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamEmployeeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LeaveRequestHistory>)[history]);

        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetTeamEmployeeIdsAsync(
                managerId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync([teamEmployeeId]);
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetHistoryAsync(managerId, "Bearer manager", requestId, access);

        Assert.Equal("Approved", Assert.Single(result).ToStatus);
        hrClient.Verify(item => item.GetTeamEmployeeIdsAsync(
            managerId, "Bearer manager", It.IsAny<CancellationToken>()), Times.Once);
    }

    // Không tìm thấy hoặc không thuộc visibility đều trả NotFound để tránh lộ sự tồn tại của đơn.
    [Fact]
    public async Task GetHistoryAsync_WhenRequestIsOutsideScopeThrowsNotFound()
    {
        var employeeId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var access = SelfAccess();
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetVisibleHistoryAsync(
                employeeId,
                requestId,
                access,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LeaveRequestHistory>?)null);
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveNotFoundException>(() =>
            service.GetHistoryAsync(employeeId, "Bearer employee", requestId, access));

        hrClient.VerifyNoOtherCalls();
    }

    // Không có leaves.read phải bị chặn trước khi gọi HR hoặc repository.
    [Fact]
    public async Task GetHistoryAsync_RejectsCallerWithoutReadPermission()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.GetHistoryAsync(Guid.NewGuid(), "Bearer token", Guid.NewGuid(), LeaveAccess.Empty));

        repository.VerifyNoOtherCalls();
        hrClient.VerifyNoOtherCalls();
    }

    private static LeaveAccess SelfAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["Self"])
        ]);

    private static LeaveAccess ManagerAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["Self", "Team"]),
            new LeavePermissionGrant("leaves.decide", ["Team"])
        ]);
}
