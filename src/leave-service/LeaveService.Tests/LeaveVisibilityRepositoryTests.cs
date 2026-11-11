using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Domain.Entities;
using LeaveService.Infrastructure.Persistence;
using Xunit;

namespace LeaveService.Tests;

// Kiểm tra trực tiếp WHERE của LeaveRequestRepository.ListVisibleAsync trên SQL Server thật.
// Mỗi test dùng employee/manager GUID riêng; khi là HR thì chỉ khẳng định các id của test, không đếm tổng.
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
        Guid actor, bool isHr, bool calendarOnly = false, DateOnly? from = null, DateOnly? to = null)
    {
        await using var context = _fixture.CreateContext();
        var rows = await new LeaveRequestRepository(context)
            .ListVisibleAsync(actor, isHr, calendarOnly, from, to);
        return rows.Select(row => row.Request.Id).ToHashSet();
    }

    [LeaveSqlFact]
    public async Task NonHr_SeesOwnRequestsAndRequestsAssignedToThem_NotOthers()
    {
        var me = Guid.NewGuid();
        var otherManager = Guid.NewGuid();
        var otherEmployee = Guid.NewGuid();

        var own = await CreateAsync(me, otherManager, new DateOnly(2030, 3, 4), new DateOnly(2030, 3, 4));
        var assignedToMe = await CreateAsync(otherEmployee, me, new DateOnly(2030, 3, 5), new DateOnly(2030, 3, 5));
        var notMine = await CreateAsync(otherEmployee, otherManager, new DateOnly(2030, 3, 6), new DateOnly(2030, 3, 6));
        var ofAnotherOwnerWithMe = await CreateAsync(otherEmployee, null, new DateOnly(2030, 3, 7), new DateOnly(2030, 3, 7));

        var visible = await VisibleAsync(me, isHr: false);

        Assert.Contains(own.Id, visible);
        Assert.Contains(assignedToMe.Id, visible);
        Assert.DoesNotContain(notMine.Id, visible);
        Assert.DoesNotContain(ofAnotherOwnerWithMe.Id, visible);
    }

    [LeaveSqlFact]
    public async Task Hr_SeesRequestsOfAnyEmployeeAndApprover()
    {
        var a = await CreateAsync(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2030, 4, 1), new DateOnly(2030, 4, 1));
        var b = await CreateAsync(Guid.NewGuid(), null, new DateOnly(2030, 4, 2), new DateOnly(2030, 4, 2));

        var visible = await VisibleAsync(Guid.NewGuid(), isHr: true);

        Assert.Contains(a.Id, visible);
        Assert.Contains(b.Id, visible);
    }

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
            await repo.TransitionAsync(approved.Id, manager, false, LeaveRequestStatus.Approved, null, DateTime.UtcNow, manager);
        }
        await using (var context = _fixture.CreateContext())
        {
            var repo = new LeaveRequestRepository(context);
            await repo.TransitionAsync(rejected.Id, manager, false, LeaveRequestStatus.Rejected, null, DateTime.UtcNow, manager);
        }
        await using (var context = _fixture.CreateContext())
        {
            var repo = new LeaveRequestRepository(context);
            await repo.CancelAsync(employee, cancelled.Id, null, DateTime.UtcNow);
        }

        var calendar = await VisibleAsync(employee, isHr: false, calendarOnly: true);

        Assert.Contains(pending.Id, calendar);
        Assert.Contains(approved.Id, calendar);
        Assert.DoesNotContain(rejected.Id, calendar);
        Assert.DoesNotContain(cancelled.Id, calendar);
    }

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

    // Dùng HR để đọc toàn bộ, rồi chỉ khẳng định các id của test này.
    var visible = await VisibleAsync(Guid.NewGuid(), isHr: true,
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