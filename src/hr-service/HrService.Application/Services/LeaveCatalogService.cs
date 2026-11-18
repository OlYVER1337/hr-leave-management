using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Domain.Entities;
using HrService.Domain.Rules;

namespace HrService.Application.Services;

public sealed class LeaveCatalogService(
    ILeaveCatalogRepository repository,
    ILeaveCatalogCache cache,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private static readonly IReadOnlyDictionary<string, (string Name, bool IsPaid, bool DeductsBalance)> BuiltInLeaveTypes =
        new Dictionary<string, (string Name, bool IsPaid, bool DeductsBalance)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANNUAL"] = ("Nghỉ phép năm", true, true),
            ["SICK"] = ("Nghỉ ốm", true, true),
            ["UNPAID"] = ("Nghỉ không lương", false, false)
        };

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

        var builtInTypes = leaveTypes.Where(leaveType => IsBuiltInLeaveType(leaveType.Code));
        return includeInactive
            ? builtInTypes.ToArray()
            : builtInTypes.Where(leaveType => leaveType.IsActive).ToArray();
    }

    public async Task<LeaveCatalogResponse> GetLeaveCatalogAsync(
        int? year,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        // DbContext is scoped and cannot execute concurrent operations.
        // Keep these cache/database reads sequential while exposing one HTTP call.
        var leaveTypes = await GetLeaveTypesAsync(includeInactive, cancellationToken);
        var leavePolicies = await GetLeavePoliciesAsync(year, cancellationToken);
        return new LeaveCatalogResponse(leaveTypes, leavePolicies);
    }

    public async Task<LeaveCatalogResponse> GetAuthoritativeLeaveCatalogAsync(
        int? year,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var leaveTypes = await GetAuthoritativeLeaveTypesAsync(includeInactive, cancellationToken);
        var leavePolicies = await GetAuthoritativeLeavePoliciesAsync(year, cancellationToken);
        return new LeaveCatalogResponse(leaveTypes, leavePolicies);
    }

    public async Task<IReadOnlyList<LeaveTypeResponse>> GetAuthoritativeLeaveTypesAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var entities = await repository.ListLeaveTypesAsync(includeInactive, cancellationToken);
        return entities
            .Where(leaveType => IsBuiltInLeaveType(leaveType.Code))
            .Select(ToResponse)
            .ToArray();
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
            cancellationToken))
            .FirstOrDefault(item => item.Id == id && IsBuiltInLeaveType(item.Code))
            ?? throw new NotFoundException($"Leave type with id '{id}' was not found.");
        return leaveType;
    }

    public async Task<LeaveTypeResponse> GetAuthoritativeLeaveTypeByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var leaveType = await repository.GetLeaveTypeByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Leave type with id '{id}' was not found.");
        if (!IsBuiltInLeaveType(leaveType.Code))
        {
            throw new NotFoundException($"Leave type with id '{id}' was not found.");
        }

        return ToResponse(leaveType);
    }

    public async Task<LeaveTypeResponse> CreateLeaveTypeAsync(
        CreateLeaveTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var (code, _) = ValidateLeaveType(request.Code, request.Name);
        if (!BuiltInLeaveTypes.TryGetValue(code, out var definition))
        {
            throw Problems.Invalid("code", "Only ANNUAL, SICK and UNPAID leave types are supported.");
        }

        if (request.IsPaid != definition.IsPaid || request.DeductsBalance != definition.DeductsBalance)
        {
            throw Problems.Invalid("code", "The built-in leave type properties cannot be changed.");
        }

        if (await repository.ExistsLeaveTypeCodeAsync(code, cancellationToken: cancellationToken))
        {
            throw new ConflictException($"Leave type code '{code}' is already in use.");
        }

        var leaveType = new LeaveType
        {
            Code = code,
            Name = definition.Name,
            IsPaid = definition.IsPaid,
            DeductsBalance = definition.DeductsBalance,
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
        if (!BuiltInLeaveTypes.TryGetValue(leaveType.Code, out var definition))
        {
            throw new NotFoundException($"Leave type with id '{id}' was not found.");
        }

        leaveType.Name = definition.Name;
        leaveType.IsPaid = definition.IsPaid;
        leaveType.DeductsBalance = definition.DeductsBalance;
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

        var builtInPolicies = policies.Where(policy => IsBuiltInLeaveType(policy.LeaveTypeCode));
        return year.HasValue
            ? builtInPolicies.Where(policy => policy.Year == year.Value).ToArray()
            : builtInPolicies.ToArray();
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
            cancellationToken))
            .FirstOrDefault(item => item.Id == id && IsBuiltInLeaveType(item.LeaveTypeCode))
            ?? throw new NotFoundException($"Leave policy with id '{id}' was not found.");
        return policy;
    }

    public Task<IReadOnlyList<LeavePolicyResponse>> GetAuthoritativeLeavePoliciesAsync(
        int? year,
        CancellationToken cancellationToken = default) =>
        GetAuthoritativeLeavePoliciesAsync(year, asOfDate: null, cancellationToken);

    public async Task<IReadOnlyList<LeavePolicyResponse>> GetAuthoritativeLeavePoliciesAsync(
        int? year,
        DateOnly? asOfDate,
        CancellationToken cancellationToken = default)
    {
        ValidateYear(year);
        var policies = (await repository.ListLeavePoliciesAsync(year, cancellationToken))
            .Where(policy => policy.LeaveType is not null && IsBuiltInLeaveType(policy.LeaveType.Code));

        if (!asOfDate.HasValue)
        {
            return policies.Select(ToResponse).ToArray();
        }

        return policies
            .Where(policy =>
                policy.EffectiveFrom >= new DateOnly(policy.Year, 1, 1) &&
                policy.EffectiveFrom <= asOfDate.Value)
            .GroupBy(policy => new { policy.LeaveTypeId, policy.Year })
            .Select(group => group
                .OrderByDescending(policy => policy.EffectiveFrom)
                .ThenByDescending(policy => policy.CreatedAtUtc)
                .ThenByDescending(policy => policy.Id)
                .First())
            .Select(ToResponse)
            .ToArray();
    }

    public async Task<LeavePolicyResponse> GetAuthoritativeLeavePolicyByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var policy = await repository.GetLeavePolicyForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Leave policy with id '{id}' was not found.");
        if (policy.LeaveType is null || !IsBuiltInLeaveType(policy.LeaveType.Code))
        {
            throw new NotFoundException($"Leave policy with id '{id}' was not found.");
        }

        return ToResponse(policy);
    }

    public async Task<LeavePolicyResponse> CreateLeavePolicyAsync(
        LeavePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePolicy(request);
        EnsureEditablePolicyYear(request.Year);

        var today = GetBusinessToday();
        var firstPolicyDate = new DateOnly(request.Year, 1, 1);
        var effectiveFrom = request.EffectiveFrom
            ?? (today > firstPolicyDate ? today : firstPolicyDate);
        EnsurePolicyEffectiveFrom(effectiveFrom, request.Year);

        var leaveType = await repository.GetLeaveTypeForUpdateByIdAsync(
            request.LeaveTypeId,
            cancellationToken)
            ?? throw new NotFoundException($"Leave type with id '{request.LeaveTypeId}' was not found.");

        if (!IsBuiltInLeaveType(leaveType.Code))
            throw Problems.Invalid("leaveTypeId", "Policies are limited to the built-in leave types.");
        if (!leaveType.IsActive)
            throw Problems.Invalid("leaveTypeId", "A policy cannot be created for an inactive leave type.");
        if (!leaveType.DeductsBalance)
            throw Problems.Invalid("leaveTypeId", "Policies are only available for leave types that deduct a balance.");

        if (await repository.ExistsLeavePolicyAsync(
                request.LeaveTypeId,
                request.Year,
                cancellationToken: cancellationToken))
        {
            throw new ConflictException(
                $"A policy for leave type '{request.LeaveTypeId}' and year '{request.Year}' already exists. Append a new version instead.");
        }

        var policy = new LeavePolicy
        {
            LeaveType = leaveType,
            LeaveTypeId = leaveType.Id,
            Year = request.Year,
            DaysPerYear = request.DaysPerYear,
            EffectiveFrom = effectiveFrom,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Reason = NormalizePolicyReason(request.Reason, "Khởi tạo chính sách")
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
        var original = await repository.GetLeavePolicyForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Leave policy with id '{id}' was not found.");
        if (original.LeaveType is null || !IsBuiltInLeaveType(original.LeaveType.Code))
            throw new NotFoundException($"Leave policy with id '{id}' was not found.");

        EnsureEditablePolicyYear(original.Year);
        if (request.Year != original.Year)
            throw Problems.Invalid("year", "A policy version cannot be moved to another year.");

        if (request.LeaveTypeId != original.LeaveTypeId)
        {
            var requestedType = await repository.GetLeaveTypeForUpdateByIdAsync(
                request.LeaveTypeId,
                cancellationToken)
                ?? throw new NotFoundException($"Leave type with id '{request.LeaveTypeId}' was not found.");

            if (!IsBuiltInLeaveType(requestedType.Code) ||
                !requestedType.IsActive ||
                !requestedType.DeductsBalance)
            {
                throw Problems.Invalid("leaveTypeId", "Policies require an active built-in leave type that deducts a balance.");
            }

            throw Problems.Invalid("leaveTypeId", "A policy version cannot be moved to another leave type.");
        }

        var leaveType = await repository.GetLeaveTypeForUpdateByIdAsync(
            original.LeaveTypeId,
            cancellationToken)
            ?? throw new NotFoundException($"Leave type with id '{original.LeaveTypeId}' was not found.");

        if (!leaveType.IsActive || !leaveType.DeductsBalance)
            throw Problems.Invalid("leaveTypeId", "A policy version requires an active leave type that deducts a balance.");

        var today = GetBusinessToday();
        var firstPolicyDate = new DateOnly(original.Year, 1, 1);
        var effectiveFrom = request.EffectiveFrom
            ?? (today > firstPolicyDate ? today : firstPolicyDate);
        EnsurePolicyEffectiveFrom(effectiveFrom, original.Year);

        var versions = await repository.ListLeavePoliciesAsync(original.Year, cancellationToken);
        var timeline = versions
            .Where(item => item.LeaveTypeId == original.LeaveTypeId)
            .Select(item => new PolicyVersion(item.EffectiveFrom, item.DaysPerYear));
        if (LeavePolicyTimeline.HasSameEffectiveDate(timeline, effectiveFrom))
        {
            throw new ConflictException(
                $"A policy version for leave type '{original.LeaveTypeId}' and effective date '{effectiveFrom}' already exists.");
        }

        var version = new LeavePolicy
        {
            LeaveType = leaveType,
            LeaveTypeId = leaveType.Id,
            Year = original.Year,
            DaysPerYear = request.DaysPerYear,
            EffectiveFrom = effectiveFrom,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Reason = NormalizePolicyReason(request.Reason, "Cập nhật chính sách")
        };
        repository.Add(version);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(LeavePoliciesCacheKey, cancellationToken);
        return ToResponse(version);
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

    public async Task<IReadOnlyList<HolidayResponse>> GetAuthoritativeHolidaysAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            throw Problems.Invalid("from", "The start date must not be later than the end date.");
        }

        var holidays = await repository.ListHolidaysAsync(from, to, cancellationToken);
        return holidays.Select(ToResponse).ToArray();
    }

    public async Task<HolidayResponse> GetAuthoritativeHolidayByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var holiday = await repository.GetHolidayForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Holiday with id '{id}' was not found.");
        return ToResponse(holiday);
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

    private static bool IsBuiltInLeaveType(string code) =>
        BuiltInLeaveTypes.ContainsKey(code);

    private static string ValidateLeaveTypeName(string value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw Problems.Invalid("name", "Name is required and cannot exceed 100 characters.");
        }

        return name;
    }

    private int GetCurrentBusinessYear() => GetBusinessToday().Year;

    private DateOnly GetBusinessToday() =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);

    private void EnsureEditablePolicyYear(int year)
    {
        var currentYear = GetCurrentBusinessYear();
        if (year < currentYear)
        {
            throw Problems.Invalid(
                "year",
                $"Leave policies can only be created or changed for the current or future years. Current year: {currentYear}.");
        }
    }

    private void EnsurePolicyEffectiveFrom(DateOnly effectiveFrom, int policyYear)
    {
        var today = GetBusinessToday();
        var firstPolicyDate = new DateOnly(policyYear, 1, 1);
        var earliestAllowedDate = today > firstPolicyDate ? today : firstPolicyDate;

        if (LeavePolicyTimeline.CanAddVersion(effectiveFrom, earliestAllowedDate, policyYear))
        {
            return;
        }

        if (effectiveFrom < earliestAllowedDate)
        {
            throw Problems.Invalid(
                "effectiveFrom",
                $"A policy version for {policyYear} cannot take effect before {earliestAllowedDate:yyyy-MM-dd}.");
        }

        throw Problems.Invalid(
            "effectiveFrom",
            "A policy version cannot take effect after the end of its policy year.");
    }

    private static string NormalizePolicyReason(string? value, string fallback)
    {
        var reason = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (reason.Length > 500)
            throw Problems.Invalid("reason", "Reason cannot exceed 500 characters.");
        return reason;
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

        if (request.EffectiveFrom == DateOnly.MinValue)
            errors.Add("effectiveFrom", "Effective date must be a valid calendar date.");
        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Trim().Length > 500)
            errors.Add("reason", "Reason cannot exceed 500 characters.");
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
            policy.DaysPerYear,
            policy.EffectiveFrom,
            policy.CreatedAtUtc,
            policy.Reason);

    private static HolidayResponse ToResponse(Holiday holiday) =>
        new(holiday.Id, holiday.HolidayDate, holiday.Name);
}
