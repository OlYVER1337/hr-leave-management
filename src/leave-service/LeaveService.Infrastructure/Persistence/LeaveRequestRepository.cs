using System.Data;
using LeaveService.Application.Abstractions;
using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LeaveService.Infrastructure.Persistence;

public sealed class LeaveRequestRepository(LeaveDbContext context) : ILeaveRequestRepository
{
    private async Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> CreateOnceAsync(
        LeaveRequest request,
        IReadOnlyList<AnnualLeaveAllocation> allocations,
        LeaveRequestHistory history,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var overlaps = await context.LeaveRequests.AnyAsync(
            item => item.EmployeeId == request.EmployeeId &&
                    (item.Status == LeaveRequestStatus.Pending ||
                     item.Status == LeaveRequestStatus.Approved) &&
                    item.StartDate <= request.EndDate &&
                    item.EndDate >= request.StartDate,
            cancellationToken);
        if (overlaps)
        {
            throw new LeaveConflictException("The requested dates overlap another pending or approved leave request.");
        }

        // A retry re-adds the same entity graph; make sure allocations are not duplicated.
        request.YearAllocations.Clear();

        var balances = new List<LeaveBalance>();
        foreach (var allocation in allocations)
        {
            var balance = await context.LeaveBalances.SingleOrDefaultAsync(
                item => item.EmployeeId == request.EmployeeId &&
                        item.LeaveTypeId == request.LeaveTypeId &&
                        item.Year == allocation.Year,
                cancellationToken);

            if (balance is null)
            {
                balance = new LeaveBalance
                {
                    EmployeeId = request.EmployeeId,
                    LeaveTypeId = request.LeaveTypeId,
                    Year = allocation.Year,
                    Entitled = allocation.Entitled
                };
                context.LeaveBalances.Add(balance);
            }

            var available = balance.Entitled - balance.Used - balance.Pending;
            if (allocation.Days > available)
            {
                throw new LeaveConflictException(
                    $"Insufficient leave balance for {allocation.Year}. Available: {available:0.0}; requested: {allocation.Days:0.0}.");
            }

            balance.Pending += allocation.Days;
            balances.Add(balance);
        }

        context.LeaveRequests.Add(request);
        foreach (var allocation in allocations)
        {
            request.YearAllocations.Add(new LeaveRequestYearAllocation
            {
                LeaveRequestId = request.Id,
                Year = allocation.Year,
                Days = allocation.Days
            });
        }

        history.LeaveRequestId = request.Id;
        context.LeaveRequestHistories.Add(history);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (request, balances);
    }

    public Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> CreateAsync(
        LeaveRequest request,
        IReadOnlyList<AnnualLeaveAllocation> allocations,
        LeaveRequestHistory history,
        CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(
            () => CreateOnceAsync(request, allocations, history, cancellationToken),
            "Leave request conflicts with another concurrent update; retry the request.",
            cancellationToken);

    public Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> TransitionAsync(
        Guid requestId,
        Guid actorId,
        LeaveAccess access,
        LeaveRequestStatus status,
        string? note,
        DateTime decidedAtUtc,
        Guid? verifiedApproverId = null,
        CancellationToken cancellationToken = default)
    {
        if (status is not (LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A decision must approve or reject the request.");
        }

        return ExecuteWithRetryAsync(
            () => TransitionOnceAsync(
                requestId, actorId, access, status, note, decidedAtUtc, verifiedApproverId, cancellationToken),
            "Leave request changed concurrently; retry the decision.",
            cancellationToken);
    }

    public Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> CancelAsync(
        Guid employeeId,
        Guid requestId,
        string? note,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(
            () => CancelOnceAsync(employeeId, requestId, note, cancelledAtUtc, cancellationToken),
            "Leave request changed concurrently; retry the cancellation.",
            cancellationToken);

    public async Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var requests = await context.LeaveRequests
            .AsNoTracking()
            .Where(item => item.EmployeeId == employeeId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);

        if (requests.Length == 0)
        {
            return [];
        }

        var leaveTypeIds = requests.Select(item => item.LeaveTypeId).Distinct().ToArray();
        var years = requests
            .SelectMany(item => Enumerable.Range(item.StartDate.Year, item.EndDate.Year - item.StartDate.Year + 1))
            .Distinct()
            .ToArray();
        var balances = await context.LeaveBalances
            .AsNoTracking()
            .Where(item => item.EmployeeId == employeeId &&
                           leaveTypeIds.Contains(item.LeaveTypeId) &&
                           years.Contains(item.Year))
            .ToArrayAsync(cancellationToken);

        return requests.Select(request =>
        {
            var requestYears = Enumerable
                .Range(request.StartDate.Year, request.EndDate.Year - request.StartDate.Year + 1)
                .ToHashSet();
            return (
                request,
                (IReadOnlyList<LeaveBalance>)balances
                    .Where(balance => balance.LeaveTypeId == request.LeaveTypeId &&
                                      requestYears.Contains(balance.Year))
                    .ToArray());
        }).ToArray();
    }

    public async Task<(IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> Items, int TotalCount)>
        ListByEmployeePageAsync(
            Guid employeeId,
            LeaveRequestStatus? status,
            int? leaveTypeId,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        var query = context.LeaveRequests
            .AsNoTracking()
            .Where(item => item.EmployeeId == employeeId);

        if (status.HasValue)
            query = query.Where(item => item.Status == status.Value);
        if (leaveTypeId.HasValue)
            query = query.Where(item => item.LeaveTypeId == leaveTypeId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (Guid.TryParse(term, out var requestId))
                query = query.Where(item => item.Id == requestId || item.Reason.Contains(term));
            else
                query = query.Where(item => item.Reason.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var normalizedSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)Math.Max(1, page) - 1L) * normalizedSize;
        if (offset >= totalCount)
            return (Array.Empty<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>(), totalCount);

        var requests = await query
            .Include(item => item.YearAllocations)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Skip((int)offset)
            .Take(normalizedSize)
            .ToArrayAsync(cancellationToken);

        var rows = await WithBalancesAsync(requests, cancellationToken);
        return (rows, totalCount);
    }

    public async Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)?> GetByIdAsync(
        Guid employeeId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await context.LeaveRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == requestId && item.EmployeeId == employeeId,
                cancellationToken);
        if (request is null)
        {
            return null;
        }

        var balances = await context.LeaveBalances
            .AsNoTracking()
            .Where(item => item.EmployeeId == employeeId &&
                           item.LeaveTypeId == request.LeaveTypeId &&
                           item.Year >= request.StartDate.Year &&
                           item.Year <= request.EndDate.Year)
            .ToArrayAsync(cancellationToken);
        return (request, balances);
    }

    public async Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)?> GetVisibleByIdAsync(
        Guid employeeId,
        Guid requestId,
        LeaveAccess access,
        IReadOnlyCollection<Guid> teamEmployeeIds,
        CancellationToken cancellationToken = default)
    {
        IQueryable<LeaveRequest> query = context.LeaveRequests
            .AsNoTracking()
            .Where(item => item.Id == requestId);

        if (!access.CanReadAll)
        {
            var self = access.CanReadSelf;
            var teamIds = access.CanReadTeam
                ? teamEmployeeIds.Distinct().ToArray()
                : Array.Empty<Guid>();
            var team = teamIds.Length > 0;

            if (self && team)
            {
                query = query.Where(item =>
                    item.EmployeeId == employeeId || teamIds.Contains(item.EmployeeId));
            }
            else if (team)
            {
                query = query.Where(item => teamIds.Contains(item.EmployeeId));
            }
            else if (self)
            {
                query = query.Where(item => item.EmployeeId == employeeId);
            }
            else
            {
                query = query.Where(item => false);
            }
        }

        var request = await query.SingleOrDefaultAsync(cancellationToken);
        if (request is null)
        {
            return null;
        }

        // The visible request may belong to a Team member or any employee under All.
        // Load balances for the request owner, not for the caller.
        var balances = await context.LeaveBalances
            .AsNoTracking()
            .Where(item => item.EmployeeId == request.EmployeeId &&
                           item.LeaveTypeId == request.LeaveTypeId &&
                           item.Year >= request.StartDate.Year &&
                           item.Year <= request.EndDate.Year)
            .ToArrayAsync(cancellationToken);

        return (request, balances);
    }

    public async Task<(int Pending, int Approved, int Total)> GetSummaryAsync(
        Guid employeeId,
        LeaveAccess access,
        IReadOnlyCollection<Guid> teamEmployeeIds,
        CancellationToken cancellationToken = default)
    {
        var teamIds = teamEmployeeIds.Distinct().ToArray();
        var canReadAll = access.CanReadAll;
        var canReadSelf = access.CanReadSelf;
        var canReadTeam = access.CanReadTeam && teamIds.Length > 0;
        var canDecideAll = access.CanDecideAll;
        var canDecideTeam = access.CanDecideTeam && teamIds.Length > 0;

        var result = await context.LeaveRequests
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Pending = group.Count(item =>
                    item.Status == LeaveRequestStatus.Pending &&
                    (canDecideAll || (canDecideTeam && teamIds.Contains(item.EmployeeId)))),
                Approved = group.Count(item =>
                    item.Status == LeaveRequestStatus.Approved &&
                    (canReadAll ||
                     (canReadSelf && item.EmployeeId == employeeId) ||
                     (canReadTeam && teamIds.Contains(item.EmployeeId)))),
                Total = group.Count(item =>
                    canReadAll ||
                    (canReadSelf && item.EmployeeId == employeeId) ||
                    (canReadTeam && teamIds.Contains(item.EmployeeId)))
            })
            .SingleOrDefaultAsync(cancellationToken);

        return result is null
            ? (0, 0, 0)
            : (result.Pending, result.Approved, result.Total);
    }

    public async Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var requests = await context.LeaveRequests
            .AsNoTracking()
            .Include(item => item.YearAllocations)
            .Where(item => item.Status == LeaveRequestStatus.Pending)
            .OrderBy(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return await WithBalancesAsync(requests, cancellationToken);
    }

    public async Task<(IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> Items, int TotalCount)>
        ListVisiblePageAsync(
            Guid employeeId,
            LeaveAccess access,
            IReadOnlyCollection<Guid> teamEmployeeIds,
            bool calendarOnly,
            DateOnly? from,
            DateOnly? to,
            LeaveRequestStatus? status,
            int? leaveTypeId,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        var query = context.LeaveRequests.AsNoTracking().AsQueryable();

        if (!access.CanReadAll)
        {
            var self = access.CanReadSelf;
            var team = access.CanReadTeam && teamEmployeeIds.Count > 0;
            // Remove duplicate IDs before EF translates the collection into SQL parameters.
            var teamIds = team
                ? teamEmployeeIds.Distinct().ToArray()
                : Array.Empty<Guid>();

            if (self && team)
            {
                query = query.Where(item => item.EmployeeId == employeeId || teamIds.Contains(item.EmployeeId));
            }
            else if (team)
            {
                query = query.Where(item => teamIds.Contains(item.EmployeeId));
            }
            else if (self)
            {
                query = query.Where(item => item.EmployeeId == employeeId);
            }
            else
            {
                query = query.Where(item => false);
            }
        }

        if (calendarOnly)
        {
            query = query.Where(item =>
                item.Status == LeaveRequestStatus.Pending ||
                item.Status == LeaveRequestStatus.Approved);
        }

        if (from.HasValue)
        {
            query = query.Where(item => item.EndDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(item => item.StartDate <= to.Value);
        }

        if (status.HasValue)
            query = query.Where(item => item.Status == status.Value);

        if (leaveTypeId.HasValue)
            query = query.Where(item => item.LeaveTypeId == leaveTypeId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (Guid.TryParse(term, out var requestId))
                query = query.Where(item => item.Id == requestId || item.Reason.Contains(term));
            else
                query = query.Where(item => item.Reason.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Không để page lớn gây tràn số khi tính OFFSET.
        var normalizedSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)Math.Max(1, page) - 1L) * normalizedSize;
        if (offset >= totalCount)
            return (Array.Empty<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>(), totalCount);

        // Chỉ tải đúng một trang đơn và balances của các đơn trong trang đó.
        var requests = await query
            .Include(item => item.YearAllocations)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Skip((int)offset)
            .Take(normalizedSize)
            .ToArrayAsync(cancellationToken);

        var rows = await WithBalancesAsync(requests, cancellationToken);
        return (rows, totalCount);
    }

    public async Task<(IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> Items, int TotalCount)>
        ListPendingPageAsync(
            IReadOnlyCollection<Guid> employeeIds,
            bool includeAll,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        var query = context.LeaveRequests
            .AsNoTracking()
            .Where(item => item.Status == LeaveRequestStatus.Pending);

        if (!includeAll)
        {
            var teamIds = employeeIds.Distinct().ToArray();
            query = teamIds.Length == 0
                ? query.Where(item => false)
                : query.Where(item => teamIds.Contains(item.EmployeeId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (Guid.TryParse(term, out var requestId))
                query = query.Where(item => item.Id == requestId || item.Reason.Contains(term));
            else
                query = query.Where(item => item.Reason.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var normalizedSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)Math.Max(1, page) - 1L) * normalizedSize;
        if (offset >= totalCount)
            return (Array.Empty<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>(), totalCount);

        var requests = await query
            .Include(item => item.YearAllocations)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Skip((int)offset)
            .Take(normalizedSize)
            .ToArrayAsync(cancellationToken);

        var rows = await WithBalancesAsync(requests, cancellationToken);
        return (rows, totalCount);
    }

    public async Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListVisibleAsync(
            Guid employeeId,
            LeaveAccess access,
            IReadOnlyCollection<Guid> teamEmployeeIds,
            bool calendarOnly,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default)
    {
        var query = context.LeaveRequests
            .AsNoTracking()
            .Include(item => item.YearAllocations)
            .AsQueryable();

        if (!access.CanReadAll)
        {
            // Team membership comes from HR (current manager), never from the ApproverId snapshot.
            // Self and Team are OR-ed; an empty team never widens access.
            var self = access.CanReadSelf;
            var team = access.CanReadTeam && teamEmployeeIds.Count > 0;
            var teamIds = team ? teamEmployeeIds.ToArray() : Array.Empty<Guid>();

            if (self && team)
            {
                query = query.Where(item => item.EmployeeId == employeeId || teamIds.Contains(item.EmployeeId));
            }
            else if (team)
            {
                query = query.Where(item => teamIds.Contains(item.EmployeeId));
            }
            else if (self)
            {
                query = query.Where(item => item.EmployeeId == employeeId);
            }
            else
            {
                query = query.Where(item => false);
            }
        }

        if (calendarOnly)
        {
            query = query.Where(item =>
                item.Status == LeaveRequestStatus.Pending ||
                item.Status == LeaveRequestStatus.Approved);
        }

        if (from.HasValue)
        {
            query = query.Where(item => item.EndDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(item => item.StartDate <= to.Value);
        }

        var requests = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return await WithBalancesAsync(requests, cancellationToken);
    }

    public async Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListPendingByEmployeesAsync(
            IReadOnlyCollection<Guid> employeeIds,
            CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var ids = employeeIds.ToArray();
        var requests = await context.LeaveRequests
            .AsNoTracking()
            .Include(item => item.YearAllocations)
            .Where(item => item.Status == LeaveRequestStatus.Pending && ids.Contains(item.EmployeeId))
            .OrderBy(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return await WithBalancesAsync(requests, cancellationToken);
    }

    public Task<LeaveRequest?> GetForDecisionAsync(
        Guid requestId,
        CancellationToken cancellationToken = default) =>
        context.LeaveRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);

    private async Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> TransitionOnceAsync(
        Guid requestId,
        Guid actorId,
        LeaveAccess access,
        LeaveRequestStatus status,
        string? note,
        DateTime decidedAtUtc,
        Guid? verifiedApproverId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var request = await context.LeaveRequests
            .Include(item => item.YearAllocations)
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken)
            ?? throw new LeaveNotFoundException($"Leave request with id '{requestId}' was not found.");
        EnsurePending(request);
        if (!access.CanDecideAll)
        {
            // Team decisions must carry a successful live HR verification.
            // Never fall back to ApproverId: it is only a historical snapshot.
            if (!access.CanDecideTeam ||
                !verifiedApproverId.HasValue ||
                verifiedApproverId.Value != actorId)
            {
                throw new LeaveForbiddenException(
                    "A current HR manager verification is required to decide a Team request.");
            }

            request.ApproverId = verifiedApproverId.Value;
        }

        var balances = await UpdateBalancesAsync(
            request,
            approve: status == LeaveRequestStatus.Approved,
            cancellationToken);
        var previousStatus = request.Status;
        request.Status = status;
        request.DecidedAtUtc = decidedAtUtc;
        context.LeaveRequestHistories.Add(new LeaveRequestHistory
        {
            LeaveRequestId = request.Id,
            ActorId = actorId,
            Action = status == LeaveRequestStatus.Approved ? "Approved" : "Rejected",
            FromStatus = previousStatus,
            ToStatus = status,
            Note = note,
            CreatedAtUtc = decidedAtUtc
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (request, balances);
    }

    private async Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> CancelOnceAsync(
        Guid employeeId,
        Guid requestId,
        string? note,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var request = await context.LeaveRequests
            .Include(item => item.YearAllocations)
            .SingleOrDefaultAsync(
                item => item.Id == requestId && item.EmployeeId == employeeId,
                cancellationToken)
            ?? throw new LeaveNotFoundException($"Leave request with id '{requestId}' was not found.");
        EnsurePending(request);

        var balances = await UpdateBalancesAsync(request, approve: false, cancellationToken);
        var previousStatus = request.Status;
        request.Status = LeaveRequestStatus.Cancelled;
        request.DecidedAtUtc = cancelledAtUtc;
        context.LeaveRequestHistories.Add(new LeaveRequestHistory
        {
            LeaveRequestId = request.Id,
            ActorId = employeeId,
            Action = "Cancelled",
            FromStatus = previousStatus,
            ToStatus = LeaveRequestStatus.Cancelled,
            Note = note,
            CreatedAtUtc = cancelledAtUtc
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (request, balances);
    }

    public async Task<IReadOnlyList<LeaveBalance>> ListBalancesAsync(
        Guid employeeId,
        int year,
        CancellationToken cancellationToken = default)
    {
        return await context.LeaveBalances
            .AsNoTracking()
            .Where(item => item.EmployeeId == employeeId && item.Year == year)
            .OrderBy(item => item.LeaveTypeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<LeaveBalance>> UpdateBalancesAsync(
        LeaveRequest request,
        bool approve,
        CancellationToken cancellationToken)
    {
        var balances = new List<LeaveBalance>();
        foreach (var allocation in request.YearAllocations)
        {
            var balance = await context.LeaveBalances.SingleOrDefaultAsync(
                item => item.EmployeeId == request.EmployeeId &&
                        item.LeaveTypeId == request.LeaveTypeId &&
                        item.Year == allocation.Year,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Leave balance for {allocation.Year} is missing for request '{request.Id}'.");

            if (balance.Pending < allocation.Days)
            {
                throw new InvalidOperationException(
                    $"Pending leave balance is inconsistent for request '{request.Id}'.");
            }

            balance.Pending -= allocation.Days;
            if (approve)
            {
                balance.Used += allocation.Days;
            }

            balances.Add(balance);
        }

        return balances;
    }

    private async Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        WithBalancesAsync(
            IReadOnlyList<LeaveRequest> requests,
            CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var employeeIds = requests.Select(item => item.EmployeeId).Distinct().ToArray();
        var leaveTypeIds = requests.Select(item => item.LeaveTypeId).Distinct().ToArray();
        var years = requests
            .SelectMany(item => item.YearAllocations.Select(allocation => allocation.Year))
            .Distinct()
            .ToArray();
        var balances = await context.LeaveBalances
            .AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId) &&
                           leaveTypeIds.Contains(item.LeaveTypeId) &&
                           years.Contains(item.Year))
            .ToArrayAsync(cancellationToken);

        // Build a lookup once instead of scanning the whole balance result for every request.
        // This keeps in-memory mapping O(requests + balances), not O(requests × balances).
        var balancesByKey = balances.ToDictionary(
            balance => (balance.EmployeeId, balance.LeaveTypeId, balance.Year));

        return requests.Select(request =>
        {
            var requestBalances = new List<LeaveBalance>(request.YearAllocations.Count);
            foreach (var allocation in request.YearAllocations)
            {
                if (balancesByKey.TryGetValue(
                    (request.EmployeeId, request.LeaveTypeId, allocation.Year),
                    out var balance))
                {
                    requestBalances.Add(balance);
                }
            }

            return (request, (IReadOnlyList<LeaveBalance>)requestBalances.ToArray());
        }).ToArray();
    }

    private static void EnsurePending(LeaveRequest request)
    {
        if (request.Status != LeaveRequestStatus.Pending)
        {
            throw new LeaveConflictException("Only pending leave requests can be changed.");
        }
    }

    private const int MaxAttempts = 3;

    /// <summary>
    /// Runs a serializable transaction and retries it when SQL Server picks it as a deadlock
    /// victim or another writer wins a race (row-version or unique-index conflict). On retry the
    /// operation re-reads fresh data, so it either succeeds or fails with a precise business error
    /// (insufficient balance, request no longer pending) instead of a generic conflict.
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string conflictMessage,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception) when (IsRetryableConflict(exception) && attempt < MaxAttempts)
            {
                context.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(15, 60) * attempt), cancellationToken);
            }
            catch (Exception exception) when (IsRetryableConflict(exception))
            {
                throw new LeaveConflictException(conflictMessage);
            }
        }
    }

    private static bool IsRetryableConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException ||
                current is SqlException { Number: 1205 or 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
