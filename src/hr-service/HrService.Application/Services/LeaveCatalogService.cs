using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Domain.Entities;

namespace HrService.Application.Services;

public sealed class LeaveCatalogService(
    ILeaveCatalogRepository repository,
    ILeaveCatalogCache cache,
    IUnitOfWork unitOfWork)
{
    private const string LeaveTypesCacheKey = "hr:catalog:leave-types";
    private const string LeavePoliciesCacheKey = "hr:catalog:leave-policies";
    private const string HolidaysCacheKey = "hr:catalog:holidays";

    public async Task<IReadOnlyList<LeaveTypeResponse>> GetLeaveTypesAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var leaveTypes = await GetCachedListAsync(
            LeaveTypesCacheKey,
            async () =>
            {
                var entities = await repository.ListLeaveTypesAsync(
                    includeInactive: true,
                    cancellationToken);
                return entities.Select(ToResponse).ToArray();
            },
            cancellationToken);

        return includeInactive
            ? leaveTypes
            : leaveTypes.Where(leaveType => leaveType.IsActive).ToArray();
    }

    public async Task<LeaveTypeResponse> GetLeaveTypeByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var leaveType = (await GetCachedListAsync(
            LeaveTypesCacheKey,
            async () =>
            {
                var entities = await repository.ListLeaveTypesAsync(
                    includeInactive: true,
                    cancellationToken);
                return entities.Select(ToResponse).ToArray();
            },
            cancellationToken)).FirstOrDefault(item => item.Id == id)
            ?? throw new NotFoundException($"Leave type with id '{id}' was not found.");
        return leaveType;
    }

    public async Task<LeaveTypeResponse> CreateLeaveTypeAsync(
        CreateLeaveTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var (code, name) = ValidateLeaveType(request.Code, request.Name);
        if (code == "UNPAID" && request.IsPaid)
        {
            throw Problems.Invalid("isPaid", "The UNPAID leave type cannot be paid.");
        }

        if (await repository.ExistsLeaveTypeCodeAsync(code, cancellationToken: cancellationToken))
        {
            throw new ConflictException($"Leave type code '{code}' is already in use.");
        }

        var leaveType = new LeaveType
        {
            Code = code,
            Name = name,
            IsPaid = request.IsPaid,
            DeductsBalance = request.DeductsBalance,
            IsActive = true
        };
        repository.Add(leaveType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(LeaveTypesCacheKey, cancellationToken);
        return ToResponse(leaveType);
    }

    public async Task<LeaveTypeResponse> UpdateLeaveTypeAsync(
        int id,
        UpdateLeaveTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var leaveType = await repository.GetLeaveTypeForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Leave type with id '{id}' was not found.");
        var name = ValidateLeaveTypeName(request.Name);
        if (leaveType.Code == "UNPAID" && request.IsPaid)
        {
            throw Problems.Invalid("isPaid", "The UNPAID leave type cannot be paid.");
        }

        leaveType.Name = name;
        leaveType.IsPaid = request.IsPaid;
        leaveType.DeductsBalance = request.DeductsBalance;
        leaveType.IsActive = request.IsActive;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(LeaveTypesCacheKey, cancellationToken);
        await cache.RemoveAsync(LeavePoliciesCacheKey, cancellationToken);
        return ToResponse(leaveType);
    }

    public async Task<IReadOnlyList<LeavePolicyResponse>> GetLeavePoliciesAsync(
        int? year,
        CancellationToken cancellationToken = default)
    {
        ValidateYear(year);
        var policies = await GetCachedListAsync(
            LeavePoliciesCacheKey,
            async () =>
            {
                var entities = await repository.ListLeavePoliciesAsync(
                    year: null,
                    cancellationToken);
                return entities.Select(ToResponse).ToArray();
            },
            cancellationToken);

        return year.HasValue
            ? policies.Where(policy => policy.Year == year.Value).ToArray()
            : policies;
    }

    public async Task<LeavePolicyResponse> GetLeavePolicyByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var policy = (await GetCachedListAsync(
            LeavePoliciesCacheKey,
            async () =>
            {
                var entities = await repository.ListLeavePoliciesAsync(
                    year: null,
                    cancellationToken);
                return entities.Select(ToResponse).ToArray();
            },
            cancellationToken)).FirstOrDefault(item => item.Id == id)
            ?? throw new NotFoundException($"Leave policy with id '{id}' was not found.");
        return policy;
    }

    public async Task<LeavePolicyResponse> CreateLeavePolicyAsync(
        LeavePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePolicy(request);
        var leaveType = await repository.GetLeaveTypeForUpdateByIdAsync(
            request.LeaveTypeId,
            cancellationToken)
            ?? throw new NotFoundException($"Leave type with id '{request.LeaveTypeId}' was not found.");

        if (!leaveType.IsActive)
        {
            throw Problems.Invalid("leaveTypeId", "A policy cannot be created for an inactive leave type.");
        }

        if (await repository.ExistsLeavePolicyAsync(
                request.LeaveTypeId,
                request.Year,
                cancellationToken: cancellationToken))
        {
            throw new ConflictException(
                $"A policy for leave type '{request.LeaveTypeId}' and year '{request.Year}' already exists.");
        }

        var policy = new LeavePolicy
        {
            LeaveType = leaveType,
            LeaveTypeId = leaveType.Id,
            Year = request.Year,
            DaysPerYear = request.DaysPerYear
        };
        repository.Add(policy);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(LeavePoliciesCacheKey, cancellationToken);
        return ToResponse(policy);
    }

    public async Task<LeavePolicyResponse> UpdateLeavePolicyAsync(
        int id,
        LeavePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePolicy(request);
        var policy = await repository.GetLeavePolicyForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Leave policy with id '{id}' was not found.");
        var leaveType = await repository.GetLeaveTypeForUpdateByIdAsync(
            request.LeaveTypeId,
            cancellationToken)
            ?? throw new NotFoundException($"Leave type with id '{request.LeaveTypeId}' was not found.");

        if (!leaveType.IsActive)
        {
            throw Problems.Invalid("leaveTypeId", "A policy cannot be assigned to an inactive leave type.");
        }

        if (await repository.ExistsLeavePolicyAsync(
                request.LeaveTypeId,
                request.Year,
                id,
                cancellationToken))
        {
            throw new ConflictException(
                $"A policy for leave type '{request.LeaveTypeId}' and year '{request.Year}' already exists.");
        }

        policy.LeaveType = leaveType;
        policy.LeaveTypeId = leaveType.Id;
        policy.Year = request.Year;
        policy.DaysPerYear = request.DaysPerYear;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(LeavePoliciesCacheKey, cancellationToken);
        return ToResponse(policy);
    }

    public async Task<IReadOnlyList<HolidayResponse>> GetHolidaysAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            throw Problems.Invalid("from", "The start date must not be later than the end date.");
        }

        var holidays = await GetCachedListAsync(
            HolidaysCacheKey,
            async () =>
            {
                var entities = await repository.ListHolidaysAsync(
                    from: null,
                    to: null,
                    cancellationToken);
                return entities.Select(ToResponse).ToArray();
            },
            cancellationToken);

        return holidays
            .Where(holiday =>
                (!from.HasValue || holiday.HolidayDate >= from.Value) &&
                (!to.HasValue || holiday.HolidayDate <= to.Value))
            .ToArray();
    }

    public async Task<HolidayResponse> GetHolidayByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var holiday = (await GetCachedListAsync(
            HolidaysCacheKey,
            async () =>
            {
                var entities = await repository.ListHolidaysAsync(
                    from: null,
                    to: null,
                    cancellationToken);
                return entities.Select(ToResponse).ToArray();
            },
            cancellationToken)).FirstOrDefault(item => item.Id == id)
            ?? throw new NotFoundException($"Holiday with id '{id}' was not found.");
        return holiday;
    }

    public async Task<HolidayResponse> CreateHolidayAsync(
        HolidayRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = ValidateHoliday(request);
        if (await repository.ExistsHolidayDateAsync(
                request.HolidayDate,
                cancellationToken: cancellationToken))
        {
            throw new ConflictException($"A holiday on '{request.HolidayDate}' already exists.");
        }

        var holiday = new Holiday { HolidayDate = request.HolidayDate, Name = name };
        repository.Add(holiday);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(HolidaysCacheKey, cancellationToken);
        return ToResponse(holiday);
    }

    public async Task<HolidayResponse> UpdateHolidayAsync(
        int id,
        HolidayRequest request,
        CancellationToken cancellationToken = default)
    {
        var holiday = await repository.GetHolidayForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Holiday with id '{id}' was not found.");
        var name = ValidateHoliday(request);
        if (await repository.ExistsHolidayDateAsync(request.HolidayDate, id, cancellationToken))
        {
            throw new ConflictException($"A holiday on '{request.HolidayDate}' already exists.");
        }

        holiday.HolidayDate = request.HolidayDate;
        holiday.Name = name;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(HolidaysCacheKey, cancellationToken);
        return ToResponse(holiday);
    }

    public async Task DeleteHolidayAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var holiday = await repository.GetHolidayForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Holiday with id '{id}' was not found.");
        repository.Remove(holiday);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(HolidaysCacheKey, cancellationToken);
    }

    private async Task<IReadOnlyList<T>> GetCachedListAsync<T>(
        string key,
        Func<Task<T[]>> loadFromDatabase,
        CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<T[]>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var items = await loadFromDatabase();
        await cache.SetAsync(key, items, cancellationToken);
        return items;
    }

    private static (string Code, string Name) ValidateLeaveType(string codeValue, string nameValue)
    {
        var code = codeValue?.Trim().ToUpperInvariant();
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(code) ||
            code.Length > 20 ||
            code.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            errors.Add("code", "Code is required, must not exceed 20 characters, and may contain only letters, digits, or underscores.");
        }

        errors.ThrowIfAny();
        return (code!, ValidateLeaveTypeName(nameValue));
    }

    private static string ValidateLeaveTypeName(string value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw Problems.Invalid("name", "Name is required and cannot exceed 100 characters.");
        }

        return name;
    }

    private static void ValidatePolicy(LeavePolicyRequest request)
    {
        var errors = new ValidationErrors();
        if (request.LeaveTypeId <= 0)
        {
            errors.Add("leaveTypeId", "A valid leave type is required.");
        }

        if (request.Year is < 1 or > 9999)
        {
            errors.Add("year", "Year must be between 1 and 9999.");
        }

        if (request.DaysPerYear is < 0 or > 366)
        {
            errors.Add("daysPerYear", "Days per year must be between 0 and 366.");
        }
        else if (decimal.Round(request.DaysPerYear, 1) != request.DaysPerYear)
        {
            errors.Add("daysPerYear", "Days per year can have at most one decimal place.");
        }

        errors.ThrowIfAny();
    }

    private static void ValidateYear(int? year)
    {
        if (year.HasValue && year.Value is < 1 or > 9999)
        {
            throw Problems.Invalid("year", "Year must be between 1 and 9999.");
        }
    }

    private static string ValidateHoliday(HolidayRequest request)
    {
        var name = request.Name?.Trim();
        var errors = new ValidationErrors();
        if (request.HolidayDate == default)
        {
            errors.Add("holidayDate", "Holiday date is required.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
        {
            errors.Add("name", "Name is required and cannot exceed 150 characters.");
        }

        errors.ThrowIfAny();
        return name!;
    }

    private static LeaveTypeResponse ToResponse(LeaveType leaveType) =>
        new(
            leaveType.Id,
            leaveType.Code,
            leaveType.Name,
            leaveType.IsPaid,
            leaveType.DeductsBalance,
            leaveType.IsActive);

    private static LeavePolicyResponse ToResponse(LeavePolicy policy) =>
        new(
            policy.Id,
            policy.LeaveTypeId,
            policy.LeaveType!.Code,
            policy.LeaveType.Name,
            policy.Year,
            policy.DaysPerYear);

    private static HolidayResponse ToResponse(Holiday holiday) =>
        new(holiday.Id, holiday.HolidayDate, holiday.Name);
}
