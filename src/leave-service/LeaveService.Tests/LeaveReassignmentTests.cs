using LeaveService.Application.Abstractions;
using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using LeaveService.Domain.Entities;
using LeaveService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
namespace LeaveService.Tests;

public sealed class LeaveReassignmentTests : IClassFixture<LeaveSqlIntegrationFixture>
{
    private const string Auth = "Bearer token";
    private readonly LeaveSqlIntegrationFixture _fixture;

    public LeaveReassignmentTests(LeaveSqlIntegrationFixture fixture) => _fixture = fixture;

    private async Task<LeaveRequest> PendingAsync(Guid employee, Guid approver)
    {
        var request = new LeaveRequest
        {
            EmployeeId = employee, ApproverId = approver, DepartmentId = 3, LeaveTypeId = 2,
            StartDate = new DateOnly(2030, 7, 1), EndDate = new DateOnly(2030, 7, 1), TotalDays = 1m,
            Reason = "reassignment", CreatedAtUtc = DateTime.UtcNow
        };
        await using var context = _fixture.CreateContext();
        await new LeaveRequestRepository(context).CreateAsync(
            request,
            [new AnnualLeaveAllocation(2030, 1m, 20m)],
            new LeaveRequestHistory
            {
                LeaveRequestId = request.Id, ActorId = employee, Action = "Created",
                ToStatus = LeaveRequestStatus.Pending, CreatedAtUtc = request.CreatedAtUtc
            });
        return request;
    }

    // HR báo quản lý hiện tại là `currentManager`.
    private static Mock<ILeaveHrClient> HrSays(Guid employee, Guid? currentManager)
    {
        var hr = new Mock<ILeaveHrClient>();
        hr.Setup(h => h.GetEmployeeAsync(employee, Auth, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new HrEmployeeSnapshot(employee, "E", "Employee", "Employee", true, 3, currentManager));
        return hr;
    }

    private async Task<LeaveRequestResponse> DecideAsync(Guid actor, Guid requestId, bool approve, Mock<ILeaveHrClient> hr)
    {
        await using var context = _fixture.CreateContext();
        var service = new LeaveRequestService(new LeaveRequestRepository(context), hr.Object, TimeProvider.System);
        return await service.DecideAsync(actor, Auth, requestId, approve, false, null);
    }

    private async Task<LeaveRequest> StoredAsync(Guid requestId)
    {
        await using var context = _fixture.CreateContext();
        return await context.LeaveRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
    }

    [LeaveSqlFact]
    public async Task NewManager_CanDecideOldRequest_AndApproverIdIsUpdatedOnSuccess()
    {
        var employee = Guid.NewGuid();
        var oldManager = Guid.NewGuid();
        var newManager = Guid.NewGuid();
        var request = await PendingAsync(employee, oldManager);

        await DecideAsync(newManager, request.Id, approve: true, HrSays(employee, newManager));

        var stored = await StoredAsync(request.Id);
        Assert.Equal(LeaveRequestStatus.Approved, stored.Status);
        Assert.Equal(newManager, stored.ApproverId);
    }

    [LeaveSqlFact]
    public async Task OldManager_IsForbidden_AndApproverIdIsUnchanged()
    {
        var employee = Guid.NewGuid();
        var oldManager = Guid.NewGuid();
        var newManager = Guid.NewGuid();
        var request = await PendingAsync(employee, oldManager);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            DecideAsync(oldManager, request.Id, approve: true, HrSays(employee, newManager)));

        var stored = await StoredAsync(request.Id);
        Assert.Equal(LeaveRequestStatus.Pending, stored.Status);
        Assert.Equal(oldManager, stored.ApproverId);
    }

    [LeaveSqlFact]
    public async Task FailedDecision_DoesNotChangeApproverId()
    {
        var employee = Guid.NewGuid();
        var oldManager = Guid.NewGuid();
        var newManager = Guid.NewGuid();
        var request = await PendingAsync(employee, oldManager);
        await DecideAsync(newManager, request.Id, approve: true, HrSays(employee, newManager));

        // Đơn đã Approved: quyết định lần nữa phải thất bại và không đổi ApproverId.
        await Assert.ThrowsAsync<LeaveConflictException>(() =>
            DecideAsync(oldManager, request.Id, approve: false, HrSays(employee, newManager)));

        var stored = await StoredAsync(request.Id);
        Assert.Equal(LeaveRequestStatus.Approved, stored.Status);
        Assert.Equal(newManager, stored.ApproverId);
    }
}