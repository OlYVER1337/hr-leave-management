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
        CancellationToken cancellationToken = default) =>
        (await SynchronizeMineWithMissingPoliciesAsync(
            employeeId,
            requestedYear,
            authorization,
            cancellationToken)).Balances;

    // Loại nghỉ đang hoạt động nhưng thiếu chính sách hiệu lực sẽ được bỏ qua và báo lại,
    // không chặn số dư của các loại còn lại.
    public async Task<MineBalancesResult> SynchronizeMineWithMissingPoliciesAsync(
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

        var today = DateOnly.FromDateTime(
            timeProvider.GetUtcNow().ToOffset(LeaveRequestService.BusinessOffset).DateTime);
        var firstPolicyDate = new DateOnly(year, 1, 1);
        var effectiveOn = today > firstPolicyDate ? today : firstPolicyDate;
        var policiesByType = catalog.LeavePolicies
            .Where(item =>
                item.Year == year &&
                item.EffectiveFrom >= firstPolicyDate &&
                item.EffectiveFrom <= effectiveOn)
            .GroupBy(item => item.LeaveTypeId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.EffectiveFrom)
                    .ThenByDescending(item => item.CreatedAtUtc)
                    .ThenByDescending(item => item.Id)
                    .First());

        var entitlements = new List<LeaveBalanceEntitlement>(activeDeductingTypes.Length);
        var missingPolicyTypes = new List<string>();
        foreach (var leaveType in activeDeductingTypes)
        {
            if (!policiesByType.TryGetValue(leaveType.Id, out var policy))
            {
                missingPolicyTypes.Add(leaveType.Code);
                continue;
            }

            var days = policy.DaysPerYear;
            if (days < 0m || days > 366m || decimal.Round(days, 1) != days)
            {
                throw new LeaveConflictException(
                    $"The leave policy for '{leaveType.Code}' in {year} has an invalid entitlement.");
            }

            entitlements.Add(new LeaveBalanceEntitlement(leaveType.Id, days, year));
        }

        // HR catalog đã được lấy và kiểm tra đầy đủ trước khi chạm vào Leave DB.
        // Không có loại nào đủ chính sách để đồng bộ thì không ghi gì.
        var balances = await repository.SynchronizeBalancesAsync(
            employeeId,
            year,
            entitlements,
            cancellationToken);

        var responses = balances
            .OrderBy(item => item.LeaveTypeId)
            .Select(item => new LeaveBalanceResponse(
                item.LeaveTypeId,
                item.Year,
                item.Entitled,
                item.Used,
                item.Pending,
                item.Entitled - item.Used - item.Pending,
                item.PolicyDays))
            .ToArray();

        return new MineBalancesResult(responses, missingPolicyTypes);
    }

    public int GetCurrentBusinessYear() =>
        DateOnly.FromDateTime(
            timeProvider.GetUtcNow().ToOffset(LeaveRequestService.BusinessOffset).DateTime).Year;
}

// Kết quả đồng bộ số dư của nhân viên: số dư các loại đã có chính sách và danh sách loại thiếu chính sách.
public sealed record MineBalancesResult(
    IReadOnlyList<LeaveBalanceResponse> Balances,
    IReadOnlyList<string> MissingPolicyLeaveTypes);
