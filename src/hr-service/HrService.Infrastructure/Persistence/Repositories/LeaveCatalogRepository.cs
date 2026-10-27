using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrService.Infrastructure.Persistence.Repositories;

public sealed class LeaveCatalogRepository(HrDbContext dbContext) : ILeaveCatalogRepository
{
    public async Task<IReadOnlyList<LeaveType>> ListLeaveTypesAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.LeaveTypes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(leaveType => leaveType.IsActive);
        }

        return await query
            .OrderBy(leaveType => leaveType.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<LeaveType?> GetLeaveTypeByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return dbContext.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(leaveType => leaveType.Id == id, cancellationToken);
    }

    public Task<LeaveType?> GetLeaveTypeForUpdateByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return dbContext.LeaveTypes
            .FirstOrDefaultAsync(leaveType => leaveType.Id == id, cancellationToken);
    }

    public Task<bool> ExistsLeaveTypeCodeAsync(
        string code,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.LeaveTypes
            .AsNoTracking()
            .Where(leaveType => leaveType.Code == code);

        if (excludeId.HasValue)
        {
            query = query.Where(leaveType => leaveType.Id != excludeId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeavePolicy>> ListLeavePoliciesAsync(
        int? year,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.LeavePolicies
            .AsNoTracking()
            .Include(policy => policy.LeaveType)
            .AsQueryable();

        if (year.HasValue)
        {
            query = query.Where(policy => policy.Year == year.Value);
        }

        return await query
            .OrderBy(policy => policy.Year)
            .ThenBy(policy => policy.LeaveType!.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<LeavePolicy?> GetLeavePolicyForUpdateByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return dbContext.LeavePolicies
            .Include(policy => policy.LeaveType)
            .FirstOrDefaultAsync(policy => policy.Id == id, cancellationToken);
    }

    public Task<bool> ExistsLeavePolicyAsync(
        int leaveTypeId,
        int year,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.LeavePolicies
            .AsNoTracking()
            .Where(policy => policy.LeaveTypeId == leaveTypeId && policy.Year == year);

        if (excludeId.HasValue)
        {
            query = query.Where(policy => policy.Id != excludeId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Holiday>> ListHolidaysAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Holidays.AsNoTracking();
        if (from.HasValue)
        {
            query = query.Where(holiday => holiday.HolidayDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(holiday => holiday.HolidayDate <= to.Value);
        }

        return await query
            .OrderBy(holiday => holiday.HolidayDate)
            .ToListAsync(cancellationToken);
    }

    public Task<Holiday?> GetHolidayForUpdateByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Holidays
            .FirstOrDefaultAsync(holiday => holiday.Id == id, cancellationToken);
    }

    public Task<bool> ExistsHolidayDateAsync(
        DateOnly holidayDate,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Holidays
            .AsNoTracking()
            .Where(holiday => holiday.HolidayDate == holidayDate);

        if (excludeId.HasValue)
        {
            query = query.Where(holiday => holiday.Id != excludeId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public void Add(LeaveType leaveType) => dbContext.LeaveTypes.Add(leaveType);

    public void Add(LeavePolicy leavePolicy) => dbContext.LeavePolicies.Add(leavePolicy);

    public void Add(Holiday holiday) => dbContext.Holidays.Add(holiday);
}
