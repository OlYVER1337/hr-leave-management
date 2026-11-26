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
    // API chi tiết trả thông tin đơn và lịch sử trong cùng response với quyền Self.
    [Fact]
    public async Task GetVisibleByIdAsync_ForSelfScopeReturnsRequestAndChronologicalHistory()
    {
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Status = LeaveRequestStatus.Pending
        };
        var timestamp = new DateTime(2026, 11, 26, 1, 0, 0, DateTimeKind.Utc);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        // Cố ý thêm ngược thứ tự ID; service phải trả timeline theo CreatedAtUtc rồi Id.
        request.Histories.Add(new LeaveRequestHistory
        {
            Id = secondId,
            LeaveRequestId = request.Id,
            ActorId = employeeId,
            Action = "Reviewed",
            FromStatus = LeaveRequestStatus.Pending,
            ToStatus = LeaveRequestStatus.Pending,
            Note = "Đã kiểm tra",
            CreatedAtUtc = timestamp
        });
        request.Histories.Add(new LeaveRequestHistory
        {
            Id = firstId,
            LeaveRequestId = request.Id,
            ActorId = employeeId,
            Action = "Created",
            ToStatus = LeaveRequestStatus.Pending,
            CreatedAtUtc = timestamp
        });
        var access = SelfAccess();
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetVisibleByIdAsync(
                employeeId,
                request.Id,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((request, (IReadOnlyList<LeaveBalance>)[]));

        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetVisibleByIdAsync(employeeId, "Bearer employee", request.Id, access);

        Assert.Equal(request.Id, result.Id);
        Assert.Equal(new[] { firstId, secondId }, result.History.Select(item => item.Id));
        var created = result.History[0];
        Assert.Equal(employeeId, created.ActorId);
        Assert.Equal("Created", created.Action);
        Assert.Null(created.FromStatus);
        Assert.Equal("Pending", created.ToStatus);
        Assert.Equal(timestamp, created.CreatedAtUtc);
        Assert.Equal("Đã kiểm tra", result.History[1].Note);
        hrClient.VerifyNoOtherCalls();
    }

    // Team scope kiểm tra membership hiện tại từ HR; lịch sử được trả cùng request đã được lọc.
    [Fact]
    public async Task GetVisibleByIdAsync_ForTeamScopeUsesCurrentTeamFromHr()
    {
        var managerId = Guid.NewGuid();
        var teamEmployeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = teamEmployeeId,
            Status = LeaveRequestStatus.Approved
        };
        request.Histories.Add(new LeaveRequestHistory
        {
            Id = Guid.NewGuid(),
            LeaveRequestId = request.Id,
            ActorId = managerId,
            Action = "Approved",
            FromStatus = LeaveRequestStatus.Pending,
            ToStatus = LeaveRequestStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow
        });
        var access = ManagerAccess();
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetVisibleByIdAsync(
                managerId,
                request.Id,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamEmployeeId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((request, (IReadOnlyList<LeaveBalance>)[]));

        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetTeamEmployeeIdsAsync(
                managerId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync([teamEmployeeId]);
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetVisibleByIdAsync(managerId, "Bearer manager", request.Id, access);

        Assert.Equal(request.Id, result.Id);
        Assert.Equal("Approved", Assert.Single(result.History).ToStatus);
        hrClient.Verify(item => item.GetTeamEmployeeIdsAsync(
            managerId, "Bearer manager", It.IsAny<CancellationToken>()), Times.Once);
    }

    // All scope trả về lịch sử trong cùng API mà không cần tra cứu team ở HR.
    [Fact]
    public async Task GetVisibleByIdAsync_ForAllScopeDoesNotCallHr()
    {
        var actorId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid()
        };
        var access = new LeaveAccess([
            new LeavePermissionGrant("leaves.read", ["All"])
        ]);
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetVisibleByIdAsync(
                actorId,
                request.Id,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((request, (IReadOnlyList<LeaveBalance>)[]));

        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetVisibleByIdAsync(actorId, "Bearer hr", request.Id, access);

        Assert.Equal(request.Id, result.Id);
        Assert.Empty(result.History);
        hrClient.VerifyNoOtherCalls();
    }

    // Request không tồn tại hoặc ngoài scope tiếp tục trả NotFound, không làm lộ dữ liệu lịch sử.
    [Fact]
    public async Task GetVisibleByIdAsync_WhenRequestIsOutsideScopeThrowsNotFound()
    {
        var employeeId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var access = SelfAccess();
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetVisibleByIdAsync(
                employeeId,
                requestId,
                access,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)?)null);

        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveNotFoundException>(() =>
            service.GetVisibleByIdAsync(employeeId, "Bearer employee", requestId, access));

        hrClient.VerifyNoOtherCalls();
    }

    // Không có leaves.read phải bị chặn trước khi gọi HR hoặc repository.
    [Fact]
    public async Task GetVisibleByIdAsync_RejectsCallerWithoutReadPermission()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.GetVisibleByIdAsync(Guid.NewGuid(), "Bearer token", Guid.NewGuid(), LeaveAccess.Empty));

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
