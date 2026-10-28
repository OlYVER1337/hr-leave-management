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
    public async Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> CreateAsync(
        LeaveRequest request,
        IReadOnlyList<AnnualLeaveAllocation> allocations,
        LeaveRequestHistory history,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (DbUpdateConcurrencyException)
        {
            throw new LeaveConflictException("Leave balance changed concurrently; retry the request.");
        }
        catch (Exception exception) when (IsConcurrentWriteConflict(exception))
        {
            throw new LeaveConflictException("Leave request conflicts with another concurrent update; retry the request.");
        }
    }

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

    public async Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListPendingAsync(Guid? approverId, CancellationToken cancellationToken = default)
    {
        var requests = await context.LeaveRequests
            .AsNoTracking()
            .Include(item => item.YearAllocations)
            .Where(item => item.Status == LeaveRequestStatus.Pending &&
                           (!approverId.HasValue || item.ApproverId == approverId.Value))
            .OrderBy(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return await WithBalancesAsync(requests, cancellationToken);
    }

    public async Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListVisibleAsync(
            Guid employeeId,
            bool isHr,
            bool calendarOnly,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default)
    {
        var query = context.LeaveRequests
            .AsNoTracking()
            .Include(item => item.YearAllocations)
            .AsQueryable();

        if (!isHr)
        {
            query = query.Where(item => item.EmployeeId == employeeId || item.ApproverId == employeeId);
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

    public Task<LeaveRequest?> GetForDecisionAsync(
        Guid requestId,
        CancellationToken cancellationToken = default) =>
        context.LeaveRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);

    public async Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> TransitionAsync(
        Guid requestId,
        Guid actorId,
        bool isHr,
        LeaveRequestStatus status,
        string? note,
        DateTime decidedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (status is not (LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A decision must approve or reject the request.");
        }

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var request = await context.LeaveRequests
                .Include(item => item.YearAllocations)
                .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken)
                ?? throw new LeaveNotFoundException($"Leave request with id '{requestId}' was not found.");
            EnsurePending(request);
            if (!isHr && request.ApproverId != actorId)
            {
                throw new LeaveForbiddenException("Only the assigned manager or HR can decide this request.");
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
        catch (DbUpdateConcurrencyException)
        {
            throw new LeaveConflictException("Leave request or balance changed concurrently; retry the decision.");
        }
        catch (Exception exception) when (IsConcurrentWriteConflict(exception))
        {
            throw new LeaveConflictException("Leave request changed concurrently; retry the decision.");
        }
    }

    public async Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> CancelAsync(
        Guid employeeId,
        Guid requestId,
        string? note,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (DbUpdateConcurrencyException)
        {
            throw new LeaveConflictException("Leave request or balance changed concurrently; retry the cancellation.");
        }
        catch (Exception exception) when (IsConcurrentWriteConflict(exception))
        {
            throw new LeaveConflictException("Leave request changed concurrently; retry the cancellation.");
        }
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

        return requests.Select(request => (
            request,
            (IReadOnlyList<LeaveBalance>)balances
                .Where(balance => balance.EmployeeId == request.EmployeeId &&
                                  balance.LeaveTypeId == request.LeaveTypeId &&
                                  request.YearAllocations.Any(allocation => allocation.Year == balance.Year))
                .ToArray())).ToArray();
    }

    private static void EnsurePending(LeaveRequest request)
    {
        if (request.Status != LeaveRequestStatus.Pending)
        {
            throw new LeaveConflictException("Only pending leave requests can be changed.");
        }
    }

    private static bool IsConcurrentWriteConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
