using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Domain.Entities;
using LeaveService.Infrastructure.Persistence;
using Xunit;

namespace LeaveService.Tests;

// Kiểm tra trực tiếp WHERE của LeaveRequestRepository.ListVisibleAsync trên SQL Server thật.
// Mỗi test dùng employee/manager GUID riêng; khi đọc Scope All chỉ khẳng định các id của test, không đếm tổng.
public sealed class LeaveVisibilityRepositoryTests : IClassFixture<LeaveSqlIntegrationFixture>
{
    private readonly LeaveSqlIntegrationFixture _fixture;

    public LeaveVisibilityRepositoryTests(LeaveSqlIntegrationFixture fixture) => _fixture = fixture;

    private async Task<LeaveRequest> CreateAsync(
        Guid employeeId, Guid? approverId, DateOnly start, DateOnly end)
    {
        var request = new LeaveRequest
        {
            EmployeeId = employeeId, ApproverId = approverId, DepartmentId = 3, LeaveTypeId = 2,
            StartDate = start, EndDate = end, TotalDays = 1m,
            Reason = "visibility", CreatedAtUtc = DateTime.UtcNow
        };
        await using var context = _fixture.CreateContext();
        await new LeaveRequestRepository(context).CreateAsync(
            request,
            [new AnnualLeaveAllocation(2030, 1m, 20m)],
            new LeaveRequestHistory
            {
                LeaveRequestId = request.Id, ActorId = employeeId, Action = "Created",
                ToStatus = LeaveRequestStatus.Pending, CreatedAtUtc = request.CreatedAtUtc
            });
        return request;
    }

    private async Task<HashSet<Guid>> VisibleAsync(
        Guid actor, LeaveAccess access, bool calendarOnly = false, DateOnly? from = null, DateOnly? to = null,
        IReadOnlyCollection<Guid>? team = null)
    {
        await using var context = _fixture.CreateContext();
        var rows = await new LeaveRequestRepository(context)
            .ListVisibleAsync(actor, access, team ?? Array.Empty<Guid>(), calendarOnly, from, to);
        return rows.Select(row => row.Request.Id).ToHashSet();
    }

    private async Task<bool> CanReadByIdAsync(
        Guid actor,
        Guid requestId,
        LeaveAccess access,
        IReadOnlyCollection<Guid>? team = null)
    {
        await using var context = _fixture.CreateContext();
        var result = await new LeaveRequestRepository(context).GetVisibleByIdAsync(
            actor, requestId, access, team ?? Array.Empty<Guid>());
        return result is not null;
    }

    // Truy vấn SQL phải lọc đơn theo Self, Team hoặc All đúng với quyền được truyền vào.
    [LeaveSqlFact]
    public async Task GetVisibleById_EnforcesSelfTeamAndAllScopes()
    {
        var manager = Guid.NewGuid();
        var teamMember = Guid.NewGuid();
        var otherEmployee = Guid.NewGuid();
        var ownRequest = await CreateAsync(manager, null, new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 1));
        var teamRequest = await CreateAsync(teamMember, Guid.NewGuid(), new DateOnly(2030, 1, 2), new DateOnly(2030, 1, 2));
        var movedRequest = await CreateAsync(otherEmployee, manager, new DateOnly(2030, 1, 3), new DateOnly(2030, 1, 3));

        var managerAccess = ManagerAccess();
        Assert.True(await CanReadByIdAsync(manager, ownRequest.Id, managerAccess, [teamMember]));
        Assert.True(await CanReadByIdAsync(manager, teamRequest.Id, managerAccess, [teamMember]));
        Assert.False(await CanReadByIdAsync(manager, movedRequest.Id, managerAccess, [teamMember]));
        Assert.True(await CanReadByIdAsync(manager, movedRequest.Id, GlobalAccess()));
        Assert.False(await CanReadByIdAsync(manager, teamRequest.Id, TeamOnlyAccess(), []));
    }

    // Lịch sử chỉ được trả về khi request nằm trong Self/Team/All và luôn có thứ tự ổn định.
    [LeaveSqlFact]
    public async Task GetVisibleHistory_EnforcesRequestVisibilityAndOrdersByTimestampThenId()
    {
        var managerId = Guid.NewGuid();
        var teamEmployeeId = Guid.NewGuid();
        var outsideEmployeeId = Guid.NewGuid();
        var teamRequest = await CreateAsync(
            teamEmployeeId,
            approverId: Guid.NewGuid(),
            start: new DateOnly(2030, 1, 10),
            end: new DateOnly(2030, 1, 10));
        var outsideRequest = await CreateAsync(
            outsideEmployeeId,
            approverId: managerId,
            start: new DateOnly(2030, 1, 11),
            end: new DateOnly(2030, 1, 11));
        var emptyHistoryRequest = new LeaveRequest
        {
            EmployeeId = teamEmployeeId,
            DepartmentId = 3,
            LeaveTypeId = 2,
            StartDate = new DateOnly(2030, 1, 12),
            EndDate = new DateOnly(2030, 1, 12),
            TotalDays = 1m,
            Reason = "empty history",
            CreatedAtUtc = DateTime.UtcNow
        };
        await using (var context = _fixture.CreateContext())
        {
            context.LeaveRequests.Add(emptyHistoryRequest);
            await context.SaveChangesAsync();
        }

        var sameTimestamp = teamRequest.CreatedAtUtc.AddHours(-2);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await using (var context = _fixture.CreateContext())
        {
            context.LeaveRequestHistories.AddRange(
                new LeaveRequestHistory
                {
                    Id = secondId,
                    LeaveRequestId = teamRequest.Id,
                    ActorId = managerId,
                    Action = "Approved",
                    FromStatus = LeaveRequestStatus.Pending,
                    ToStatus = LeaveRequestStatus.Approved,
                    CreatedAtUtc = sameTimestamp
                },
                new LeaveRequestHistory
                {
                    Id = firstId,
                    LeaveRequestId = teamRequest.Id,
                    ActorId = managerId,
                    Action = "Reviewed",
                    FromStatus = LeaveRequestStatus.Pending,
                    ToStatus = LeaveRequestStatus.Pending,
                    CreatedAtUtc = sameTimestamp
                },
                new LeaveRequestHistory
                {
                    LeaveRequestId = outsideRequest.Id,
                    ActorId = outsideEmployeeId,
                    Action = "Private",
                    ToStatus = LeaveRequestStatus.Pending,
                    CreatedAtUtc = sameTimestamp
                });
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var repository = new LeaveRequestRepository(context);
            var teamHistory = await repository.GetVisibleHistoryAsync(
                managerId,
                teamRequest.Id,
                ManagerAccess(),
                [teamEmployeeId]);

            Assert.NotNull(teamHistory);
            var emptyHistory = await repository.GetVisibleHistoryAsync(
                managerId,
                emptyHistoryRequest.Id,
                ManagerAccess(),
                [teamEmployeeId]);
            Assert.NotNull(emptyHistory);
            Assert.Empty(emptyHistory);

            Assert.Equal(
                new[] { firstId, secondId },
                teamHistory.Where(item => item.Id == firstId || item.Id == secondId)
                    .Select(item => item.Id)
                    .ToArray());
            Assert.Equal(
                teamHistory.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
                    .Select(item => item.Id),
                teamHistory.Select(item => item.Id));

            // ApproverId còn trỏ tới manager không làm đơn ngoài Team hiện tại trở nên visible.
            var outsideHistory = await repository.GetVisibleHistoryAsync(
                managerId,
                outsideRequest.Id,
                ManagerAccess(),
                [teamEmployeeId]);
            Assert.Null(outsideHistory);

            // Self scope không thể đọc lịch sử của nhân viên khác.
            var selfHistory = await repository.GetVisibleHistoryAsync(
                managerId,
                teamRequest.Id,
                SelfAccess(),
                []);
            Assert.Null(selfHistory);

            // All scope đọc được lịch sử của request ngoài Team.
            var allHistory = await repository.GetVisibleHistoryAsync(
                managerId,
                outsideRequest.Id,
                GlobalAccess(),
                []);
            Assert.NotNull(allHistory);
            Assert.Single(allHistory);
            Assert.Equal("Private", allHistory[0].Action);
        }
    }

    private static LeaveAccess ManagerAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["Self", "Team"]),
            new LeavePermissionGrant("leaves.decide", ["Team"])
        ]);

    private static LeaveAccess SelfAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["Self"])
        ]);

    private static LeaveAccess TeamOnlyAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["Team"])
        ]);

    private static LeaveAccess GlobalAccess() =>
        new([
            new LeavePermissionGrant("leaves.read", ["All"]),
            new LeavePermissionGrant("leaves.decide", ["All"])
        ]);

    // Team scope dựa trên membership hiện tại từ HR, không dựa ApproverId snapshot cũ.
    [LeaveSqlFact]
    public async Task TeamScope_UsesCurrentHrTeamMembership_NotApproverSnapshot()
    {
        var me = Guid.NewGuid();
        var inTeam = Guid.NewGuid();      // HR hiện báo là cấp dưới của me
        var movedAway = Guid.NewGuid();   // đã chuyển sang quản lý khác; ApproverId snapshot vẫn là me

        var own = await CreateAsync(me, Guid.NewGuid(), new DateOnly(2030, 3, 4), new DateOnly(2030, 3, 4));
        var teamRequest = await CreateAsync(inTeam, Guid.NewGuid(), new DateOnly(2030, 3, 5), new DateOnly(2030, 3, 5));
        var movedRequest = await CreateAsync(movedAway, me, new DateOnly(2030, 3, 6), new DateOnly(2030, 3, 6));

        var visible = await VisibleAsync(me, ManagerAccess(), team: new[] { inTeam });

        Assert.Contains(own.Id, visible);
        Assert.Contains(teamRequest.Id, visible);
        Assert.DoesNotContain(movedRequest.Id, visible);
    }

    // Scope All được xem đơn của mọi nhân viên và không phụ thuộc người duyệt.
    [LeaveSqlFact]
    public async Task AllScope_SeesRequestsOfAnyEmployeeAndApprover()
    {
        var a = await CreateAsync(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2030, 4, 1), new DateOnly(2030, 4, 1));
        var b = await CreateAsync(Guid.NewGuid(), null, new DateOnly(2030, 4, 2), new DateOnly(2030, 4, 2));

        var visible = await VisibleAsync(Guid.NewGuid(), GlobalAccess());

        Assert.Contains(a.Id, visible);
        Assert.Contains(b.Id, visible);
    }

    // Scope Self chỉ trả về đơn do chính actor tạo.
    [LeaveSqlFact]
    public async Task SelfScope_OnlyReturnsRequestsOwnedByActor()
    {
        var actor = Guid.NewGuid();
        var ownerRequest = await CreateAsync(
            actor, Guid.NewGuid(), new DateOnly(2030, 7, 1), new DateOnly(2030, 7, 1));
        var assignedRequest = await CreateAsync(
            Guid.NewGuid(), actor, new DateOnly(2030, 7, 2), new DateOnly(2030, 7, 2));

        var visible = await VisibleAsync(actor, SelfAccess());

        Assert.Contains(ownerRequest.Id, visible);
        Assert.DoesNotContain(assignedRequest.Id, visible);
    }

    // Team rỗng phải trả kết quả rỗng, không được hiểu nhầm thành bỏ lọc.
    [LeaveSqlFact]
    public async Task TeamScope_EmptyTeamDoesNotWidenAccess()
    {
        var me = Guid.NewGuid();
        var request = await CreateAsync(Guid.NewGuid(), me, new DateOnly(2030, 8, 1), new DateOnly(2030, 8, 1));

        var visible = await VisibleAsync(me, TeamOnlyAccess(), team: Array.Empty<Guid>());

        Assert.DoesNotContain(request.Id, visible);
    }

    // Khi chỉ được cấp Team, dữ liệu trả về phải giới hạn trong danh sách thành viên team.
    [LeaveSqlFact]
    public async Task TeamOnly_ReturnsRequestsOfTeamMembersAndNotOwnRequests()
    {
        var actor = Guid.NewGuid();
        var member = Guid.NewGuid();
        var ownRequest = await CreateAsync(actor, Guid.NewGuid(), new DateOnly(2030, 8, 2), new DateOnly(2030, 8, 2));
        var memberRequest = await CreateAsync(member, Guid.NewGuid(), new DateOnly(2030, 8, 3), new DateOnly(2030, 8, 3));

        var visible = await VisibleAsync(actor, TeamOnlyAccess(), team: new[] { member });

        Assert.DoesNotContain(ownRequest.Id, visible);
        Assert.Contains(memberRequest.Id, visible);
    }

    // Lịch chỉ hiển thị các đơn Pending hoặc Approved theo quy tắc nghiệp vụ.
    [LeaveSqlFact]
    public async Task Calendar_ReturnsOnlyPendingAndApproved()
    {
        var employee = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var pending = await CreateAsync(employee, manager, new DateOnly(2030, 5, 1), new DateOnly(2030, 5, 1));
        var approved = await CreateAsync(employee, manager, new DateOnly(2030, 5, 2), new DateOnly(2030, 5, 2));
        var rejected = await CreateAsync(employee, manager, new DateOnly(2030, 5, 3), new DateOnly(2030, 5, 3));
        var cancelled = await CreateAsync(employee, manager, new DateOnly(2030, 5, 4), new DateOnly(2030, 5, 4));

        await using (var context = _fixture.CreateContext())
        {
            var repo = new LeaveRequestRepository(context);
            await repo.TransitionAsync(approved.Id, manager, ManagerAccess(), LeaveRequestStatus.Approved, null, DateTime.UtcNow, manager);
        }
        await using (var context = _fixture.CreateContext())
        {
            var repo = new LeaveRequestRepository(context);
            await repo.TransitionAsync(rejected.Id, manager, ManagerAccess(), LeaveRequestStatus.Rejected, null, DateTime.UtcNow, manager);
        }
        await using (var context = _fixture.CreateContext())
        {
            var repo = new LeaveRequestRepository(context);
            await repo.CancelAsync(employee, cancelled.Id, null, DateTime.UtcNow);
        }

        var calendar = await VisibleAsync(employee, ManagerAccess(), calendarOnly: true);

        Assert.Contains(pending.Id, calendar);
        Assert.Contains(approved.Id, calendar);
        Assert.DoesNotContain(rejected.Id, calendar);
        Assert.DoesNotContain(cancelled.Id, calendar);
    }

    // Pending lấy theo decision scope; Approved/Total lấy theo read scope, hai quyền không được nhập làm một.
    [LeaveSqlFact]
    public async Task GetSummaryAsync_AppliesReadAndDecisionScopesIndependently()
    {
        var actor = Guid.NewGuid();
        var teamMember = Guid.NewGuid();
        var outsider = Guid.NewGuid();

        await CreateAsync(
            actor, null, new DateOnly(2030, 9, 1), new DateOnly(2030, 9, 1));
        var ownApproved = await CreateAsync(
            actor, null, new DateOnly(2030, 9, 2), new DateOnly(2030, 9, 2));
        await CreateAsync(
            teamMember, actor, new DateOnly(2030, 9, 3), new DateOnly(2030, 9, 3));
        var teamApproved = await CreateAsync(
            teamMember, actor, new DateOnly(2030, 9, 4), new DateOnly(2030, 9, 4));
        await CreateAsync(
            outsider, actor, new DateOnly(2030, 9, 5), new DateOnly(2030, 9, 5));

        await using (var context = _fixture.CreateContext())
        {
            var repository = new LeaveRequestRepository(context);
            await repository.TransitionAsync(
                ownApproved.Id, actor, GlobalAccess(), LeaveRequestStatus.Approved,
                null, DateTime.UtcNow);
            await repository.TransitionAsync(
                teamApproved.Id, actor, GlobalAccess(), LeaveRequestStatus.Approved,
                null, DateTime.UtcNow);
        }

        var readSelfButDecideTeam = new LeaveAccess([
            new LeavePermissionGrant("leaves.read", ["Self"]),
            new LeavePermissionGrant("leaves.decide", ["Team"])
        ]);

        await using var summaryContext = _fixture.CreateContext();
        var summary = await new LeaveRequestRepository(summaryContext)
            .GetSummaryAsync(actor, readSelfButDecideTeam, [teamMember]);

        // Pending comes only from Team decision scope; Approved and Total come only from Self read scope.
        Assert.Equal((Pending: 1, Approved: 1, Total: 2), summary);
    }

    // Quyết định trong Team phải xác minh quan hệ Manager hiện tại và không dùng ApproverId snapshot làm căn cứ.
    [LeaveSqlFact]
    public async Task TeamDecision_RequiresLiveManagerVerificationAndNeverUsesApproverSnapshot()
    {
        var manager = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var request = await CreateAsync(
            employee, manager, new DateOnly(2030, 2, 1), new DateOnly(2030, 2, 1));

        await using (var context = _fixture.CreateContext())
        {
            var repository = new LeaveRequestRepository(context);
            await Assert.ThrowsAsync<LeaveForbiddenException>(() => repository.TransitionAsync(
                request.Id,
                manager,
                ManagerAccess(),
                LeaveRequestStatus.Approved,
                null,
                DateTime.UtcNow));
        }

        await using var verifyContext = _fixture.CreateContext();
        var persisted = await verifyContext.LeaveRequests.FindAsync(request.Id);
        Assert.NotNull(persisted);
        Assert.Equal(LeaveRequestStatus.Pending, persisted!.Status);
        Assert.Equal(manager, persisted.ApproverId);
    }

    // Kiểm tra điều kiện overlap theo khoảng ngày: bao gồm hai đầu mút giao nhau và loại các request nằm hoàn toàn bên ngoài.
    [LeaveSqlFact]
public async Task DateRange_IncludesOverlapIncludingBoundaries_ExcludesOutside()
{
    // Mỗi dòng dùng nhân viên riêng: test này kiểm tra lọc khoảng ngày,
    // không kiểm tra quy tắc trùng lịch (đã có test riêng).
    var manager = Guid.NewGuid();
    var beforeWindow = await CreateAsync(Guid.NewGuid(), manager, new DateOnly(2030, 2, 27), new DateOnly(2030, 2, 28));
    var endsOnFrom = await CreateAsync(Guid.NewGuid(), manager, new DateOnly(2030, 2, 27), new DateOnly(2030, 3, 1));
    var inside = await CreateAsync(Guid.NewGuid(), manager, new DateOnly(2030, 3, 10), new DateOnly(2030, 3, 11));
    var startsOnTo = await CreateAsync(Guid.NewGuid(), manager, new DateOnly(2030, 6, 11), new DateOnly(2030, 6, 12));
    var afterWindow = await CreateAsync(Guid.NewGuid(), manager, new DateOnly(2030, 6, 12), new DateOnly(2030, 6, 13));

    // Dùng Scope All để đọc toàn bộ, rồi chỉ khẳng định các id của test này.
    var visible = await VisibleAsync(Guid.NewGuid(), GlobalAccess(),
        from: new DateOnly(2030, 3, 1), to: new DateOnly(2030, 6, 11));
    var ours = new[] { beforeWindow, endsOnFrom, inside, startsOnTo, afterWindow }.Select(r => r.Id).ToHashSet();
    var matched = ours.Where(visible.Contains).ToHashSet();

    Assert.Contains(endsOnFrom.Id, matched);   // kết thúc đúng ngày from: vẫn nằm trong khoảng
    Assert.Contains(inside.Id, matched);
    Assert.Contains(startsOnTo.Id, matched);   // bắt đầu đúng ngày to: vẫn nằm trong khoảng
    Assert.DoesNotContain(beforeWindow.Id, matched);
    Assert.DoesNotContain(afterWindow.Id, matched);
}
}