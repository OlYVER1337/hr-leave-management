using LeaveService.Application.Abstractions;
using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using LeaveService.Domain.Entities;
using Moq;
using Xunit;

namespace LeaveService.Tests;

public sealed class LeaveRequestServiceTests
{
    [Fact]
    public async Task CreateAsync_CalculatesWorkingDaysAndCreatesAnnualBalanceAllocations()
    {
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var employee = new HrEmployeeSnapshot(employeeId, "E1", "Employee", "Employee", true, 7, managerId);
        var leaveType = new HrLeaveType(4, "ANNUAL", "Annual leave", true, true, true);
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        repository
            .Setup(item => item.CreateAsync(
                It.IsAny<LeaveRequest>(),
                It.IsAny<IReadOnlyList<AnnualLeaveAllocation>>(),
                It.IsAny<LeaveRequestHistory>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaveRequest request, IReadOnlyList<AnnualLeaveAllocation> allocations,
                LeaveRequestHistory _, CancellationToken _) =>
            {
                var balances = allocations.Select(allocation => new LeaveBalance
                {
                    EmployeeId = employeeId,
                    LeaveTypeId = leaveType.Id,
                    Year = allocation.Year,
                    Entitled = allocation.Entitled,
                    Pending = allocation.Days
                }).ToArray();
                return (request, (IReadOnlyList<LeaveBalance>)balances);
            });
        hrClient
            .Setup(item => item.GetEmployeeAsync(employeeId, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        hrClient
            .Setup(item => item.GetLeaveTypeAsync(leaveType.Id, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaveType);
        hrClient
            .Setup(item => item.GetHolidaysAsync(
                new DateOnly(2026, 12, 31),
                new DateOnly(2027, 1, 4),
                "Bearer token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HrHoliday(1, new DateOnly(2027, 1, 1), "New Year")]);
        hrClient
            .Setup(item => item.GetLeavePoliciesAsync(2026, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HrLeavePolicy(1, 4, "ANNUAL", "Annual leave", 2026, 12m)]);
        hrClient
            .Setup(item => item.GetLeavePoliciesAsync(2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HrLeavePolicy(2, 4, "ANNUAL", "Annual leave", 2027, 15m)]);
        var service = new LeaveRequestService(
            repository.Object,
            hrClient.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero)));

        var result = await service.CreateAsync(
            employeeId,
            "Bearer token",
            new CreateLeaveRequest(4, new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 4), "  Family event  "));

        Assert.Equal(2m, result.TotalDays);
        Assert.Equal("Family event", result.Reason);
        Assert.Equal(managerId, result.ApproverId);
        Assert.Equal(2, result.Balances.Count);
        Assert.Equal([2026, 2027], result.Balances.Select(balance => balance.Year));
        repository.Verify(
            item => item.CreateAsync(
                It.Is<LeaveRequest>(request =>
                    request.DepartmentId == 7 &&
                    request.EmployeeId == employeeId &&
                    request.Status == LeaveRequestStatus.Pending),
                It.Is<IReadOnlyList<AnnualLeaveAllocation>>(allocations =>
                    allocations.Count == 2 &&
                    allocations[0].Days == 1m &&
                    allocations[1].Days == 1m),
                It.IsAny<LeaveRequestHistory>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidDateRangeBeforeCallingHr()
    {
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(
            new Mock<ILeaveRequestRepository>().Object,
            hrClient.Object,
            TimeProvider.System);

        await Assert.ThrowsAsync<LeaveValidationException>(() =>
            service.CreateAsync(
                Guid.NewGuid(),
                "Bearer token",
                new CreateLeaveRequest(4, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1), "reason")));

        hrClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_RejectsPastStartDateBeforeCallingHr()
    {
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(
            new Mock<ILeaveRequestRepository>().Object,
            hrClient.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<LeaveValidationException>(() =>
            service.CreateAsync(
                Guid.NewGuid(),
                "******",
                new CreateLeaveRequest(4, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 28), "reason")));

        hrClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetVisibleAsync_RejectsInvalidDateRangeBeforeCallingRepository()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var service = new LeaveRequestService(
            repository.Object,
            new Mock<ILeaveHrClient>().Object,
            TimeProvider.System);

        await Assert.ThrowsAsync<LeaveValidationException>(() =>
            service.GetVisibleAsync(
                Guid.NewGuid(),
                isHr: false,
                calendarOnly: true,
                from: new DateOnly(2026, 2, 2),
                to: new DateOnly(2026, 2, 1)));

        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_RejectsInactiveEmployee()
    {
        var employeeId = Guid.NewGuid();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetEmployeeAsync(employeeId, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrEmployeeSnapshot(employeeId, "E1", "Employee", "Employee", false, 1, null));
        var service = new LeaveRequestService(
            new Mock<ILeaveRequestRepository>().Object,
            hrClient.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<LeaveConflictException>(() =>
            service.CreateAsync(
                employeeId,
                "Bearer token",
                new CreateLeaveRequest(4, new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 28), "reason")));

        hrClient.Verify(
            item => item.GetLeaveTypeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(true, LeaveRequestStatus.Approved)]
    [InlineData(false, LeaveRequestStatus.Rejected)]
    public async Task DecideAsync_RequiresAssignedManagerAndTransitionsRequest(
        bool approve,
        LeaveRequestStatus expectedStatus)
    {
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ApproverId = actorId,
            Status = LeaveRequestStatus.Pending
        };
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.GetForDecisionAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        repository
            .Setup(item => item.TransitionAsync(
                request.Id,
                actorId,
                false,
                expectedStatus,
                "Reviewed",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                request.Status = expectedStatus;
                return (request, (IReadOnlyList<LeaveBalance>)[]);
            });
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetEmployeeAsync(employeeId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrEmployeeSnapshot(employeeId, "E1", "Employee", "Employee", true, 1, actorId));
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.DecideAsync(
            actorId,
            "Bearer manager",
            request.Id,
            approve,
            isHr: false,
            " Reviewed ");

        Assert.Equal(expectedStatus, result.Status);
        repository.Verify(
            item => item.TransitionAsync(
                request.Id,
                actorId,
                false,
                expectedStatus,
                "Reviewed",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DecideAsync_RejectsManagerWhoIsNotAssignedWithoutCallingHr()
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ApproverId = Guid.NewGuid(),
            Status = LeaveRequestStatus.Pending
        };
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.GetForDecisionAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.DecideAsync(
                Guid.NewGuid(),
                "Bearer manager",
                request.Id,
                approve: true,
                isHr: false,
                note: null));

        hrClient.VerifyNoOtherCalls();
        repository.Verify(
            item => item.TransitionAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<LeaveRequestStatus>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelAsync_RecordsEmployeeCancellation()
    {
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Status = LeaveRequestStatus.Cancelled
        };
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.CancelAsync(
                employeeId,
                request.Id,
                "Changed plans",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((request, (IReadOnlyList<LeaveBalance>)[]));
        var service = new LeaveRequestService(
            repository.Object,
            new Mock<ILeaveHrClient>().Object,
            TimeProvider.System);

        var result = await service.CancelAsync(employeeId, request.Id, " Changed plans ");

        Assert.Equal(LeaveRequestStatus.Cancelled, result.Status);
        repository.Verify(
            item => item.CancelAsync(
                employeeId,
                request.Id,
                "Changed plans",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
