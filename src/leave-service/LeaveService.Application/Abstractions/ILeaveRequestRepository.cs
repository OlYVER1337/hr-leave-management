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

    Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListPendingAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListVisibleAsync(
            Guid employeeId,
            LeaveAccess access,
            IReadOnlyCollection<Guid> teamEmployeeIds,
            bool calendarOnly,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)>>
        ListPendingByEmployeesAsync(
            IReadOnlyCollection<Guid> employeeIds,
            CancellationToken cancellationToken = default);

    Task<LeaveRequest?> GetForDecisionAsync(Guid requestId, CancellationToken cancellationToken = default);

    Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> TransitionAsync(
        Guid requestId,
        Guid actorId,
        LeaveAccess access,
        LeaveRequestStatus status,
        string? note,
        DateTime decidedAtUtc,
        Guid? verifiedApproverId = null,
        CancellationToken cancellationToken = default);

    Task<(LeaveRequest Request, IReadOnlyList<LeaveBalance> Balances)> CancelAsync(
        Guid employeeId,
        Guid requestId,
        string? note,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaveBalance>> ListBalancesAsync(
        Guid employeeId,
        int year,
        CancellationToken cancellationToken = default);
}

public interface ILeaveHrClient
{
    Task<HrEmployeeSnapshot> GetEmployeeAsync(
        Guid employeeId,
        string authorization,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetTeamEmployeeIdsAsync(
        Guid managerId,
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
