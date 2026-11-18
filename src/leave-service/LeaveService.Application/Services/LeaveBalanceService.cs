using LeaveService.Application.Abstractions;
using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Domain.Entities;

namespace LeaveService.Application.Services;

/// <summary>
/// Initializes and reconciles an employee's leave balances from the authoritative HR catalog.
/// Existing usage is never overwritten.
/// </summary>
public sealed class LeaveBalanceService(
    ILeaveRequestRepository repository,
    ILeaveHrClient hrClient,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<LeaveBalanceResponse>> SynchronizeMineAsync(
        Guid employeeId,
        int? requestedYear,
        string authorization,
        CancellationToken cancellationToken = default)
    {
        if (employeeId == Guid.Empty)
        {
            throw new LeaveForbiddenException("A valid employee identity is required.");
        }

        var year = requestedYear ?? GetCurrentBusinessYear();
        if (year is < 1900 or > 9998)
        {
            throw new LeaveValidationException(new Dictionary<string, string[]>
            {
                ["year"] = ["Year must be between 1900 and 9998."]
            });
        }

        var catalog = await hrClient.GetLeaveCatalogAsync(year, authorization, cancellationToken);
        var activeDeductingTypes = catalog.LeaveTypes
            .Where(item => item.IsActive && item.DeductsBalance)
            .ToArray();

        var effectiveOn = DateOnly.FromDateTime(
            timeProvider.GetUtcNow().ToOffset(LeaveRequestService.BusinessOffset).DateTime);
        var policiesByType = catalog.LeavePolicies
            .Where(item => item.Year == year && item.EffectiveFrom <= effectiveOn)
            .GroupBy(item => item.LeaveTypeId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.EffectiveFrom)
                    .ThenByDescending(item => item.CreatedAtUtc)
                    .ThenByDescending(item => item.Id)
                    .First());

        var entitlements = new List<LeaveBalanceEntitlement>(activeDeductingTypes.Length);
        foreach (var leaveType in activeDeductingTypes)
        {
            if (!policiesByType.TryGetValue(leaveType.Id, out var policy))
            {
                throw new LeaveConflictException(
                    $"No effective leave policy exists for active leave type '{leaveType.Code}' in {year} on {effectiveOn}.");
            }

            var days = policy.DaysPerYear;
            if (days < 0m || days > 366m || decimal.Round(days, 1) != days)
            {
                throw new LeaveConflictException(
                    $"The leave policy for '{leaveType.Code}' in {year} has an invalid entitlement.");
            }

            entitlements.Add(new LeaveBalanceEntitlement(leaveType.Id, days, year));
        }

        // HR catalog is fetched and validated completely before touching Leave DB.
        var balances = await repository.SynchronizeBalancesAsync(
            employeeId,
            year,
            entitlements,
            cancellationToken);

        return balances
            .OrderBy(item => item.LeaveTypeId)
            .Select(item => new LeaveBalanceResponse(
                item.LeaveTypeId,
                item.Year,
                item.Entitled,
                item.Used,
                item.Pending,
                item.Entitled - item.Used - item.Pending))
            .ToArray();
    }

    public int GetCurrentBusinessYear() =>
        DateOnly.FromDateTime(
            timeProvider.GetUtcNow().ToOffset(LeaveRequestService.BusinessOffset).DateTime).Year;
}
