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
