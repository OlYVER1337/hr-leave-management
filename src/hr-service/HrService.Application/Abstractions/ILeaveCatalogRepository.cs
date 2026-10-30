using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface ILeaveCatalogRepository
{
    Task<IReadOnlyList<LeaveType>> ListLeaveTypesAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default);
    Task<LeaveType?> GetLeaveTypeByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<LeaveType?> GetLeaveTypeForUpdateByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsLeaveTypeCodeAsync(
        string code,
        int? excludeId = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeavePolicy>> ListLeavePoliciesAsync(
        int? year,
        CancellationToken cancellationToken = default);
    Task<LeavePolicy?> GetLeavePolicyForUpdateByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsLeavePolicyAsync(
        int leaveTypeId,
        int year,
        int? excludeId = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Holiday>> ListHolidaysAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);
    Task<Holiday?> GetHolidayForUpdateByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsHolidayDateAsync(
        DateOnly holidayDate,
        int? excludeId = null,
        CancellationToken cancellationToken = default);
    void Add(LeaveType leaveType);
    void Add(LeavePolicy leavePolicy);
    void Add(Holiday holiday);
    void Remove(Holiday holiday);
}
