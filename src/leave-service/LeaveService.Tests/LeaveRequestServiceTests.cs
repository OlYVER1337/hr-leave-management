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
    // Tính số ngày làm việc sau khi loại cuối tuần/ngày lễ, sau đó chia allocation theo năm và reserve balance.
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
            .ReturnsAsync([
                new HrLeavePolicy(1, 4, "ANNUAL", "Annual leave", 2026, 12m, new DateOnly(2026, 1, 1)),
                new HrLeavePolicy(3, 4, "ANNUAL", "Annual leave", 2026, 13m, new DateOnly(2026, 10, 28))
            ]);
        hrClient
            .Setup(item => item.GetLeavePoliciesAsync(2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new HrLeavePolicy(2, 4, "ANNUAL", "Annual leave", 2027, 15m, new DateOnly(2026, 1, 1)),
                new HrLeavePolicy(4, 4, "ANNUAL", "Annual leave", 2027, 18m, new DateOnly(2026, 12, 1))
            ]);
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
                    allocations[0].Entitled == 12m &&
                    allocations[0].PolicyVersionId == 1 &&
                    allocations[1].Days == 1m &&
                    allocations[1].Entitled == 15m &&
                    allocations[1].PolicyVersionId == 2),
                It.IsAny<LeaveRequestHistory>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Khoảng ngày không hợp lệ phải bị chặn trước khi gọi HR để tránh request dependency thừa.
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

    // Ngày bắt đầu đã qua theo business date Việt Nam phải bị từ chối trước khi gọi HR.
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

    // Khoảng ngày lọc calendar không hợp lệ phải bị chặn trước khi truy vấn repository.
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
                "Bearer test",
                ManagerAccess(),
                calendarOnly: true,
                from: new DateOnly(2026, 2, 2),
                to: new DateOnly(2026, 2, 1)));

        repository.VerifyNoOtherCalls();
    }

    // Calendar yêu cầu cả from và to; thiếu một trong hai không được tự suy diễn khoảng ngày.
    [Fact]
    public async Task GetVisibleAsync_CalendarRequiresBothDateBounds()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveValidationException>(() =>
            service.GetVisibleAsync(
                Guid.NewGuid(),
                "Bearer test",
                ManagerAccess(),
                calendarOnly: true,
                from: null,
                to: new DateOnly(2026, 2, 1)));

        await Assert.ThrowsAsync<LeaveValidationException>(() =>
            service.GetVisibleAsync(
                Guid.NewGuid(),
                "Bearer test",
                ManagerAccess(),
                calendarOnly: true,
                from: new DateOnly(2026, 2, 1),
                to: null));

        repository.VerifyNoOtherCalls();
        hrClient.VerifyNoOtherCalls();
    }

    // Giới hạn calendar tối đa 62 ngày để tránh truy vấn/phản hồi quá rộng.
    [Fact]
    public async Task GetVisibleAsync_CalendarRejectsRangesLongerThan62Days()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveValidationException>(() =>
            service.GetVisibleAsync(
                Guid.NewGuid(),
                "Bearer test",
                ManagerAccess(),
                calendarOnly: true,
                from: new DateOnly(2026, 1, 1),
                to: new DateOnly(2026, 3, 4)));

        repository.VerifyNoOtherCalls();
        hrClient.VerifyNoOtherCalls();
    }

    // Nhân viên inactive không được tạo đơn nghỉ.
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

    // HR không khả dụng thì Leave phải fail closed và không lưu request/balance/history.
    [Fact]
    public async Task CreateAsync_DoesNotPersistWhenHrServiceIsUnavailable()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetEmployeeAsync(employeeId, "Bearer test", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DependencyUnavailableException("HR service"));
        var service = new LeaveRequestService(
            repository.Object,
            hrClient.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            service.CreateAsync(
                employeeId,
                "Bearer test",
                new CreateLeaveRequest(4, new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 28), "reason")));

        repository.Verify(
            item => item.CreateAsync(
                It.IsAny<LeaveRequest>(),
                It.IsAny<IReadOnlyList<AnnualLeaveAllocation>>(),
                It.IsAny<LeaveRequestHistory>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Chỉ đúng người duyệt được xác minh từ HR mới được approve/reject request.
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
                It.IsAny<LeaveAccess>(),
                expectedStatus,
                "Reviewed",
                It.IsAny<DateTime>(),
                (Guid?)actorId,
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
            ManagerAccess(),
            " Reviewed ");

        Assert.Equal(expectedStatus, result.Status);
        repository.Verify(
            item => item.TransitionAsync(
                request.Id,
                actorId,
                It.IsAny<LeaveAccess>(),
                expectedStatus,
                "Reviewed",
                It.IsAny<DateTime>(),
                (Guid?)actorId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        hrClient.Verify(
            item => item.GetEmployeeAsync(employeeId, "Bearer manager", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Manager không còn là quản lý hiện tại của nhân viên phải bị từ chối dù request lưu snapshot cũ.
    [Fact]
    public async Task DecideAsync_RejectsManagerWhoIsNoLongerTheEmployeesManager()
    {
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ApproverId = actorId, // stale snapshot: the employee has since been reassigned
            Status = LeaveRequestStatus.Pending
        };
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.GetForDecisionAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetEmployeeAsync(employeeId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrEmployeeSnapshot(employeeId, "E1", "Employee", "Employee", true, 1, Guid.NewGuid()));
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.DecideAsync(actorId, "Bearer manager", request.Id, approve: true, ManagerAccess(), note: null));

        AssertNoTransition(repository);
    }

    // Nhân viên inactive không thể được xử lý đơn chỉ dựa trên approver snapshot còn khớp.
    [Fact]
    public async Task DecideAsync_RejectsInactiveEmployeeEvenWhenManagerSnapshotMatches()
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
        repository.Setup(item => item.GetForDecisionAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetEmployeeAsync(employeeId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrEmployeeSnapshot(employeeId, "E1", "Employee", "Employee", false, 1, actorId));
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.DecideAsync(actorId, "Bearer manager", request.Id, approve: true, ManagerAccess(), note: null));

        AssertNoTransition(repository);
    }

    // Manager mới theo reporting line hiện tại vẫn được quyết định dù ApproverId lúc tạo đơn đã cũ.
    [Fact]
    public async Task DecideAsync_AllowsNewManagerAfterReassignmentEvenWhenSnapshotIsStale()
    {
        var newManagerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ApproverId = Guid.NewGuid(), // previous manager
            Status = LeaveRequestStatus.Pending
        };
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.GetForDecisionAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        repository
            .Setup(item => item.TransitionAsync(
                request.Id,
                newManagerId,
                It.IsAny<LeaveAccess>(),
                LeaveRequestStatus.Approved,
                null,
                It.IsAny<DateTime>(),
                (Guid?)newManagerId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                request.Status = LeaveRequestStatus.Approved;
                return (request, (IReadOnlyList<LeaveBalance>)[]);
            });
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetEmployeeAsync(employeeId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrEmployeeSnapshot(employeeId, "E1", "Employee", "Employee", true, 1, newManagerId));
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.DecideAsync(
            newManagerId, "Bearer manager", request.Id, approve: true, ManagerAccess(), note: null);

        Assert.Equal(LeaveRequestStatus.Approved, result.Status);
    }

    // Không cho phép employee/manager tự phê duyệt đơn của chính mình, kể cả có global scope.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DecideAsync_RejectsDecidingYourOwnRequest(bool hasGlobalAccess)
    {
        var actorId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = actorId,
            ApproverId = actorId,
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
                actorId,
                "Bearer token",
                request.Id,
                approve: true,
                hasGlobalAccess ? GlobalAccess() : ManagerAccess(),
                note: null));

        hrClient.VerifyNoOtherCalls();
        AssertNoTransition(repository);
    }

    // Khoảng nghỉ quá một năm bị chặn sớm để giới hạn số policy/holiday cần truy vấn.
    [Fact]
    public async Task CreateAsync_RejectsRangeLongerThanOneYearBeforeCallingHr()
    {
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(
            new Mock<ILeaveRequestRepository>().Object,
            hrClient.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Assert.ThrowsAsync<LeaveValidationException>(() =>
            service.CreateAsync(
                Guid.NewGuid(),
                "Bearer token",
                new CreateLeaveRequest(4, new DateOnly(2026, 11, 1), new DateOnly(9999, 12, 31), "reason")));

        Assert.True(exception.Errors.ContainsKey("endDate"));
        hrClient.VerifyNoOtherCalls();
    }

    // Scope Self chỉ cần kiểm tra ownership; không cần gọi HR để xác định team.
    [Fact]
    public async Task GetVisibleByIdAsync_SelfScopeReturnsOwnRequestWithoutCallingHr()
    {
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveTypeId = 2,
            StartDate = new DateOnly(2026, 12, 1),
            EndDate = new DateOnly(2026, 12, 1)
        };
        var access = new LeaveAccess([
            new LeavePermissionGrant("leaves.read", ["Self"])
        ]);
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

        var result = await service.GetVisibleByIdAsync(
            employeeId, "Bearer employee", request.Id, access);

        Assert.Equal(request.Id, result.Id);
        hrClient.VerifyNoOtherCalls();
        repository.Verify(item => item.GetVisibleByIdAsync(
            employeeId, request.Id, access,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Scope Team phải lấy danh sách thành viên hiện tại từ HR trước khi truy vấn đơn.
    [Fact]
    public async Task GetVisibleByIdAsync_TeamScopeResolvesCurrentTeamFromHr()
    {
        var managerId = Guid.NewGuid();
        var teamEmployeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = teamEmployeeId,
            LeaveTypeId = 2,
            StartDate = new DateOnly(2026, 12, 1),
            EndDate = new DateOnly(2026, 12, 1)
        };
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

        var result = await service.GetVisibleByIdAsync(
            managerId, "Bearer manager", request.Id, access);

        Assert.Equal(request.Id, result.Id);
        hrClient.Verify(item => item.GetTeamEmployeeIdsAsync(
            managerId, "Bearer manager", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.GetVisibleByIdAsync(
            managerId, request.Id, access,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamEmployeeId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Scope All không cần tra cứu reporting line ở HR.
    [Fact]
    public async Task GetVisibleByIdAsync_AllScopeDoesNotCallHr()
    {
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeaveTypeId = 2,
            StartDate = new DateOnly(2026, 12, 1),
            EndDate = new DateOnly(2026, 12, 1)
        };
        var access = GlobalAccess();
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

        var result = await service.GetVisibleByIdAsync(
            employeeId, "Bearer hr", request.Id, access);

        Assert.Equal(request.Id, result.Id);
        hrClient.VerifyNoOtherCalls();
    }

    // Thiếu permission thì từ chối ngay, không gọi HR hoặc database.
    [Fact]
    public async Task GetVisibleByIdAsync_RejectsMissingPermissionBeforeCallingHrOrRepository()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() => service.GetVisibleByIdAsync(
            Guid.NewGuid(), "Bearer no-permission", Guid.NewGuid(), LeaveAccess.Empty));

        hrClient.VerifyNoOtherCalls();
        repository.VerifyNoOtherCalls();
    }

    // List/calendar phải xác minh read permission trước khi gọi repository.
    [Fact]
    public async Task GetVisibleAsync_RejectsMissingReadPermissionBeforeRepository()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var service = new LeaveRequestService(
            repository.Object,
            new Mock<ILeaveHrClient>().Object,
            TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.GetVisibleAsync(
                Guid.NewGuid(),
                "Bearer test",
                LeaveAccess.Empty,
                calendarOnly: false,
                from: null,
                to: null));

        repository.VerifyNoOtherCalls();
    }

    // Team filter dùng employee IDs HR trả về ở thời điểm truy vấn.
    [Fact]
    public async Task GetVisibleAsync_UsesCurrentTeamIdsFromHrForTeamScope()
    {
        var managerId = Guid.NewGuid();
        var teamEmployeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.ListVisibleAsync(
                managerId,
                It.Is<LeaveAccess>(access => access.CanReadSelf && access.CanReadTeam),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamEmployeeId)),
                false,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetTeamEmployeeIdsAsync(
                managerId,
                "Bearer manager",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([teamEmployeeId]);
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetVisibleAsync(
            managerId,
            "Bearer manager",
            ManagerAccess(),
            calendarOnly: false,
            from: null,
            to: null);

        Assert.Empty(result);
        hrClient.Verify(
            item => item.GetTeamEmployeeIdsAsync(
                managerId,
                "Bearer manager",
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            item => item.ListVisibleAsync(
                managerId,
                It.IsAny<LeaveAccess>(),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamEmployeeId)),
                false,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Scope Self không được gọi HR dư thừa để lấy team membership.
    [Fact]
    public async Task GetVisibleAsync_SelfScopeDoesNotCallHr()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.ListVisibleAsync(
                employeeId,
                It.IsAny<LeaveAccess>(),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                false,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);
        var selfAccess = new LeaveAccess([
            new LeavePermissionGrant("leaves.read", ["Self"])
        ]);

        var result = await service.GetVisibleAsync(
            employeeId,
            "Bearer employee",
            selfAccess,
            calendarOnly: false,
            from: null,
            to: null);

        Assert.Empty(result);
        hrClient.VerifyNoOtherCalls();
        repository.Verify(
            item => item.ListVisibleAsync(
                employeeId,
                selfAccess,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                false,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Scope All truy vấn trực tiếp theo phạm vi toàn cục, không cần HR team lookup.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetVisibleAsync_AllScopeDoesNotCallHr(bool alsoHasTeamScope)
    {
        var employeeId = Guid.NewGuid();
        var readScopes = alsoHasTeamScope ? new[] { "All", "Team" } : new[] { "All" };
        var access = new LeaveAccess([
            new LeavePermissionGrant("leaves.read", readScopes)
        ]);
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.ListVisibleAsync(
                employeeId,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                false,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetVisibleAsync(
            employeeId,
            "Bearer global",
            access,
            calendarOnly: false,
            from: null,
            to: null);

        Assert.Empty(result);
        hrClient.VerifyNoOtherCalls();
        repository.Verify(
            item => item.ListVisibleAsync(
                employeeId,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                false,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // HR team lookup lỗi thì fail closed, không chạy truy vấn với phạm vi thiếu.
    [Fact]
    public async Task GetVisibleAsync_HrTeamLookupFailureDoesNotQueryRepository()
    {
        var managerId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetTeamEmployeeIdsAsync(
                managerId,
                "Bearer manager",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DependencyUnavailableException("HR service"));
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            service.GetVisibleAsync(
                managerId,
                "Bearer manager",
                ManagerAccess(),
                calendarOnly: false,
                from: null,
                to: null));

        repository.VerifyNoOtherCalls();
    }

    // Team không có thành viên truyền danh sách rỗng xuống repository, không được bỏ điều kiện lọc.
    [Fact]
    public async Task GetVisibleAsync_TeamLookupWithNoMembersPassesEmptyScopeToRepository()
    {
        var managerId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.ListVisibleAsync(
                managerId,
                It.IsAny<LeaveAccess>(),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                false,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetTeamEmployeeIdsAsync(
                managerId,
                "Bearer manager",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetVisibleAsync(
            managerId,
            "Bearer manager",
            ManagerAccess(),
            calendarOnly: false,
            from: null,
            to: null);

        Assert.Empty(result);
        repository.Verify(
            item => item.ListVisibleAsync(
                managerId,
                It.IsAny<LeaveAccess>(),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                false,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Người có quyền decide All lấy inbox toàn cục mà không cần gọi HR.
    [Fact]
    public async Task GetPendingAsync_ForGlobalDeciderDoesNotCallHr()
    {
        var actorId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.ListPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetPendingAsync(actorId, "Bearer global", GlobalAccess());

        Assert.Empty(result);
        repository.Verify(item => item.ListPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(
            item => item.ListPendingByEmployeesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        hrClient.VerifyNoOtherCalls();
    }

    // Không có leaves.decide thì không được truy cập inbox chờ duyệt.
    [Fact]
    public async Task GetPendingAsync_RejectsCallerWithoutDecisionPermission()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.GetPendingAsync(Guid.NewGuid(), "Bearer token", LeaveAccess.Empty));

        repository.VerifyNoOtherCalls();
        hrClient.VerifyNoOtherCalls();
    }

    // HR/global decider được xử lý theo All scope mà không cần manager membership lookup.
    [Fact]
    public async Task DecideAsync_ForGlobalDeciderDoesNotCallHr()
    {
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ApproverId = Guid.NewGuid(),
            Status = LeaveRequestStatus.Pending
        };
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetForDecisionAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        repository.Setup(item => item.TransitionAsync(
                request.Id,
                actorId,
                It.IsAny<LeaveAccess>(),
                LeaveRequestStatus.Approved,
                null,
                It.IsAny<DateTime>(),
                (Guid?)null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                request.Status = LeaveRequestStatus.Approved;
                return (request, (IReadOnlyList<LeaveBalance>)[]);
            });
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.DecideAsync(
            actorId, "Bearer global", request.Id, approve: true, GlobalAccess(), note: null);

        Assert.Equal(LeaveRequestStatus.Approved, result.Status);
        repository.Verify(item => item.TransitionAsync(
            request.Id,
            actorId,
            It.Is<LeaveAccess>(access => access.CanDecideAll),
            LeaveRequestStatus.Approved,
            null,
            It.IsAny<DateTime>(),
            (Guid?)null,
            It.IsAny<CancellationToken>()), Times.Once);
        hrClient.VerifyNoOtherCalls();
    }

    // Inbox của Manager phải dựa vào team membership hiện tại từ HR.
    [Fact]
    public async Task GetPendingAsync_ForManagerUsesCurrentTeamFromHr()
    {
        var managerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.ListPendingByEmployeesAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(memberId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient
            .Setup(item => item.GetTeamEmployeeIdsAsync(managerId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync([memberId]);
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetPendingAsync(managerId, "Bearer manager", ManagerAccess());

        Assert.Empty(result);
        repository.Verify(
            item => item.ListPendingByEmployeesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            item => item.ListPendingAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        hrClient.Verify(
            item => item.GetTeamEmployeeIdsAsync(managerId, "Bearer manager", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Thiếu decision permission thì summary không gọi HR/repository.
    [Fact]
    public async Task GetSummaryAsync_RejectsCallerWithoutDecisionPermissionBeforeCallingDependencies()
    {
        var actorId = Guid.NewGuid();
        var readOnly = new LeaveAccess([
            new LeavePermissionGrant("leaves.read", ["Self"])
        ]);

        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.GetSummaryAsync(actorId, "Bearer token", readOnly));

        repository.VerifyNoOtherCalls();
        hrClient.VerifyNoOtherCalls();
    }

    // Caller chỉ có quyền decide nhận Pending count; các thống kê cần read scope phải để null.
    [Fact]
    public async Task GetSummaryAsync_DecisionOnlyCallerReceivesPendingCountAndNoReadMetrics()
    {
        var actorId = Guid.NewGuid();
        var teamMemberId = Guid.NewGuid();
        var access = new LeaveAccess([
            new LeavePermissionGrant("leaves.decide", ["Team"])
        ]);
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetSummaryAsync(
                actorId,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamMemberId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pending: 2, Approved: 0, Total: 0));

        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetTeamEmployeeIdsAsync(
                actorId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync([teamMemberId]);

        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetSummaryAsync(actorId, "Bearer manager", access);

        Assert.Equal(new LeaveRequestSummaryResponse(2, null, null), result);
        hrClient.Verify(item => item.GetTeamEmployeeIdsAsync(
            actorId, "Bearer manager", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.GetSummaryAsync(
            actorId,
            access,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamMemberId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Team membership được lấy từ HR; Pending và Approved/Total phải áp dụng đúng scope riêng.
    [Fact]
    public async Task GetSummaryAsync_UsesCurrentTeamAndKeepsReadAndDecisionScopesSeparate()
    {
        var actorId = Guid.NewGuid();
        var teamMemberId = Guid.NewGuid();
        var access = new LeaveAccess([
            new LeavePermissionGrant("leaves.read", ["All"]),
            new LeavePermissionGrant("leaves.decide", ["Team"])
        ]);
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetSummaryAsync(
                actorId,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamMemberId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pending: 2, Approved: 5, Total: 11));

        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetTeamEmployeeIdsAsync(
                actorId, "Bearer manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync([teamMemberId]);

        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetSummaryAsync(actorId, "Bearer manager", access);

        Assert.Equal(new LeaveRequestSummaryResponse(2, 5, 11), result);
        hrClient.Verify(item => item.GetTeamEmployeeIdsAsync(
            actorId, "Bearer manager", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.GetSummaryAsync(
            actorId,
            access,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teamMemberId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Các global scope xử lý trực tiếp mà không cần truy vấn HR team.
    [Fact]
    public async Task GetSummaryAsync_GlobalScopesDoNotCallHr()
    {
        var actorId = Guid.NewGuid();
        var access = GlobalAccess();
        var repository = new Mock<ILeaveRequestRepository>();
        repository.Setup(item => item.GetSummaryAsync(
                actorId,
                access,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pending: 3, Approved: 7, Total: 15));

        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveRequestService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.GetSummaryAsync(actorId, "Bearer hr", access);

        Assert.Equal(new LeaveRequestSummaryResponse(3, 7, 15), result);
        hrClient.VerifyNoOtherCalls();
    }

    private static void AssertNoTransition(Mock<ILeaveRequestRepository> repository) =>
        repository.Verify(
            item => item.TransitionAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<LeaveAccess>(),
                It.IsAny<LeaveRequestStatus>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

    // Pending cancellation refreshes the current HR quota and passes it to the balance transaction.
    [Fact]
    public async Task CancelAsync_RecordsEmployeeCancellationAndRefreshesPolicyWhenNeeded()
    {
        var employeeId = Guid.NewGuid();
        var pendingRequest = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveTypeId = 4,
            Status = LeaveRequestStatus.Pending,
            YearAllocations =
            [
                new LeaveRequestYearAllocation
                {
                    LeaveRequestId = Guid.NewGuid(),
                    Year = 2027,
                    Days = 2m
                }
            ]
        };
        var cancelledRequest = new LeaveRequest
        {
            Id = pendingRequest.Id,
            EmployeeId = employeeId,
            LeaveTypeId = 4,
            Status = LeaveRequestStatus.Cancelled
        };
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        repository
            .Setup(item => item.GetForDecisionAsync(pendingRequest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingRequest);
        hrClient
            .Setup(item => item.GetLeavePoliciesAsync(2027, "Bearer employee", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new HrLeavePolicy(41, 4, "ANNUAL", "Annual leave", 2027, 12m, new DateOnly(2026, 1, 1)),
                new HrLeavePolicy(42, 4, "ANNUAL", "Annual leave", 2027, 8m, new DateOnly(2026, 11, 17)),
                new HrLeavePolicy(43, 4, "ANNUAL", "Annual leave", 2027, 20m, new DateOnly(2026, 11, 20))
            ]);
        repository
            .Setup(item => item.CancelAsync(
                employeeId,
                pendingRequest.Id,
                "Changed plans",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>(),
                It.Is<IReadOnlyList<LeaveBalanceEntitlement>>(values =>
                    values.Count == 1 &&
                    values[0] == new LeaveBalanceEntitlement(4, 8m, 2027))))
            .ReturnsAsync((cancelledRequest, (IReadOnlyList<LeaveBalance>)[]));
        var service = new LeaveRequestService(
            repository.Object,
            hrClient.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 18, 2, 0, 0, TimeSpan.Zero)));

        var result = await service.CancelAsync(
            employeeId,
            "Bearer employee",
            pendingRequest.Id,
            " Changed plans ");

        Assert.Equal(LeaveRequestStatus.Cancelled, result.Status);
        repository.Verify(
            item => item.CancelAsync(
                employeeId,
                pendingRequest.Id,
                "Changed plans",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>(),
                It.Is<IReadOnlyList<LeaveBalanceEntitlement>>(values =>
                    values.Count == 1 &&
                    values[0] == new LeaveBalanceEntitlement(4, 8m, 2027))),
            Times.Once);
    }

    private static LeaveAccess ManagerAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["Self", "Team"]),
            new LeavePermissionGrant("leaves.decide", ["Team"])
        ]);

    private static LeaveAccess GlobalAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["All"]),
            new LeavePermissionGrant("leaves.decide", ["All"])
        ]);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
