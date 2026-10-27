using LeaveService.Application.Dtos;
using LeaveService.Domain.Entities;

namespace LeaveService.Application.Abstractions;

public interface ILeaveRequestRepository
{
    Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> CreateAsync(
        LeaveRequest request,
        IReadOnlyList<AnnualLeaveAllocation> allocations,
        LeaveRequestHistory history,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)?> GetByIdAsync(
        Guid employeeId,
        Guid requestId,
        CancellationToken cancellationToken = default);
}

public interface ILeaveHrClient
{
    Task<HrEmployeeSnapshot> GetEmployeeAsync(
        Guid employeeId,
        string authorization,
        CancellationToken cancellationToken = default);

    Task<HrLeaveType> GetLeaveTypeAsync(
        int leaveTypeId,
        string authorization,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HrLeavePolicy>> GetLeavePoliciesAsync(
        int year,
        string authorization,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HrHoliday>> GetHolidaysAsync(
        DateOnly from,
        DateOnly to,
        string authorization,
        CancellationToken cancellationToken = default);
}
