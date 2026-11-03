using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class LeaveCatalogServiceTests
{
    [Fact]
    public async Task GetLeaveTypesAsync_ReturnsOnlyBuiltInTypesAndFiltersInactiveForRegularList()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        repository
            .Setup(item => item.ListLeaveTypesAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new LeaveType { Id = 1, Code = "ANNUAL", Name = "Annual", IsActive = true },
                new LeaveType { Id = 2, Code = "SICK", Name = "Sick", IsActive = false },
                new LeaveType { Id = 2, Code = "OLD", Name = "Old", IsActive = false }
            ]);
        var service = CreateService(repository, cache);

        var activeTypes = await service.GetLeaveTypesAsync(includeInactive: false);
        var allTypes = await service.GetLeaveTypesAsync(includeInactive: true);

        Assert.Single(activeTypes);
        Assert.Equal("ANNUAL", activeTypes[0].Code);
        Assert.Equal(2, allTypes.Count);
        Assert.DoesNotContain(allTypes, item => item.Code == "OLD");
        repository.Verify(
            item => item.ListLeaveTypesAsync(true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateLeaveTypeAsync_UsesBuiltInDefinitionAndInvalidatesCacheAfterSave()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        var unitOfWork = new Mock<IUnitOfWork>();
        var events = new List<string>();
        LeaveType? addedLeaveType = null;
        repository
            .Setup(item => item.ExistsLeaveTypeCodeAsync(
                "ANNUAL",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(item => item.Add(It.IsAny<LeaveType>()))
            .Callback<LeaveType>(leaveType =>
            {
                addedLeaveType = leaveType;
                events.Add("add");
            });
        unitOfWork
            .Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("save"))
            .ReturnsAsync(1);
        cache.Removed += key => events.Add($"remove:{key}");
        var service = CreateService(repository, cache, unitOfWork);

        var result = await service.CreateLeaveTypeAsync(
            new CreateLeaveTypeRequest(" annual ", "Custom name", true, true));

        Assert.Equal("ANNUAL", result.Code);
        Assert.Equal("Nghỉ phép năm", result.Name);
        Assert.True(result.IsActive);
        Assert.NotNull(addedLeaveType);
        Assert.Equal("Nghỉ phép năm", addedLeaveType.Name);
        Assert.Equal(
            new[] { "add", "save", "remove:hr:catalog:leave-types" },
            events);
    }

    [Fact]
    public async Task CreateLeaveTypeAsync_RejectsPaidUnpaidType()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(repository, new InMemoryLeaveCatalogCache(), unitOfWork);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateLeaveTypeAsync(
                new CreateLeaveTypeRequest("unpaid", "Unpaid leave", true, false)));

        Assert.Contains("code", exception.Errors.Keys);
        repository.Verify(item => item.Add(It.IsAny<LeaveType>()), Times.Never);
        unitOfWork.Verify(
            item => item.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateLeaveTypeAsync_RejectsCustomTypes()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(repository, new InMemoryLeaveCatalogCache(), unitOfWork);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateLeaveTypeAsync(
                new CreateLeaveTypeRequest("PARENTAL", "Nghỉ chăm con", true, true)));

        Assert.Contains("code", exception.Errors.Keys);
        repository.Verify(item => item.Add(It.IsAny<LeaveType>()), Times.Never);
        unitOfWork.Verify(
            item => item.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateLeaveTypeAsync_PreservesBuiltInDefinitionAndInvalidatesRelatedCachesAfterSave()
    {
        var leaveType = new LeaveType
        {
            Id = 3,
            Code = "ANNUAL",
            Name = "Annual leave",
            IsPaid = true,
            DeductsBalance = true,
            IsActive = true
        };
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        var unitOfWork = new Mock<IUnitOfWork>();
        var events = new List<string>();
        repository
            .Setup(item => item.GetLeaveTypeForUpdateByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaveType);
        unitOfWork
            .Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("save"))
            .ReturnsAsync(1);
        cache.Removed += key => events.Add($"remove:{key}");
        var service = CreateService(repository, cache, unitOfWork);

        var result = await service.UpdateLeaveTypeAsync(
            3,
            new UpdateLeaveTypeRequest("Annual", true, true, false));

        Assert.Equal("ANNUAL", result.Code);
        Assert.False(result.IsActive);
        Assert.Equal("Nghỉ phép năm", result.Name);
        Assert.True(result.IsPaid);
        Assert.True(result.DeductsBalance);
        Assert.Equal(
            new[]
            {
                "save",
                "remove:hr:catalog:leave-types",
                "remove:hr:catalog:leave-policies"
            },
            events);
    }

    [Fact]
    public async Task UpdateLeaveTypeAsync_PreservesBuiltInPropertiesWhenDeletingUnpaidType()
    {
        var leaveType = new LeaveType
        {
            Id = 3,
            Code = "UNPAID",
            Name = "Unpaid leave",
            IsPaid = false,
            DeductsBalance = false,
            IsActive = true
        };
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetLeaveTypeForUpdateByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaveType);
        var service = CreateService(
            repository,
            new InMemoryLeaveCatalogCache(),
            unitOfWork);

        var result = await service.UpdateLeaveTypeAsync(
            3,
            new UpdateLeaveTypeRequest("Custom name", true, true, false));

        Assert.Equal("Nghỉ không lương", result.Name);
        Assert.False(leaveType.IsPaid);
        Assert.False(result.DeductsBalance);
        Assert.False(result.IsActive);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(366.2)]
    [InlineData(1.25)]
    [InlineData(-0.1)]
    public async Task CreateLeavePolicyAsync_RejectsInvalidDays(decimal days)
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(repository, new InMemoryLeaveCatalogCache(), unitOfWork);

        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateLeavePolicyAsync(new LeavePolicyRequest(1, 2026, days)));
        repository.Verify(
            item => item.GetLeaveTypeForUpdateByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        unitOfWork.Verify(
            item => item.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateLeavePolicyAsync_AddsPolicyForActiveTypeAndInvalidatesCache()
    {
        var leaveType = new LeaveType
        {
            Id = 4,
            Code = "ANNUAL",
            Name = "Annual leave",
            IsActive = true
        };
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        var unitOfWork = new Mock<IUnitOfWork>();
        var events = new List<string>();
        LeavePolicy? addedPolicy = null;
        repository
            .Setup(item => item.GetLeaveTypeForUpdateByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaveType);
        repository
            .Setup(item => item.ExistsLeavePolicyAsync(
                4,
                2026,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(item => item.Add(It.IsAny<LeavePolicy>()))
            .Callback<LeavePolicy>(policy =>
            {
                addedPolicy = policy;
                events.Add("add");
            });
        unitOfWork
            .Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("save"))
            .ReturnsAsync(1);
        cache.Removed += key => events.Add($"remove:{key}");
        var service = CreateService(repository, cache, unitOfWork);

        var result = await service.CreateLeavePolicyAsync(new LeavePolicyRequest(4, 2026, 12m));

        Assert.NotNull(addedPolicy);
        Assert.Equal(4, result.LeaveTypeId);
        Assert.Equal("ANNUAL", result.LeaveTypeCode);
        Assert.Equal(12m, result.DaysPerYear);
        Assert.Equal(
            new[] { "add", "save", "remove:hr:catalog:leave-policies" },
            events);
    }

    [Fact]
    public async Task CreateLeavePolicyAsync_RejectsInactiveLeaveTypeWithoutSaving()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetLeaveTypeForUpdateByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveType { Id = 4, Code = "ANNUAL", IsActive = false });
        var service = CreateService(repository, new InMemoryLeaveCatalogCache(), unitOfWork);

        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateLeavePolicyAsync(new LeavePolicyRequest(4, 2026, 12m)));

        repository.Verify(
            item => item.Add(It.IsAny<LeavePolicy>()),
            Times.Never);
        unitOfWork.Verify(
            item => item.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateLeavePolicyAsync_RejectsUnpaidLeaveTypeWithoutSaving()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetLeaveTypeForUpdateByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveType
            {
                Id = 4,
                Code = "UNPAID",
                IsActive = true,
                DeductsBalance = false
            });
        var service = CreateService(repository, new InMemoryLeaveCatalogCache(), unitOfWork);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateLeavePolicyAsync(new LeavePolicyRequest(4, 2026, 12m)));

        Assert.Contains("leaveTypeId", exception.Errors.Keys);
        repository.Verify(item => item.Add(It.IsAny<LeavePolicy>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateLeavePolicyAsync_RejectsPolicyForCustomLeaveType()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetLeaveTypeForUpdateByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveType { Id = 4, Code = "PARENTAL", IsActive = true });
        var service = CreateService(repository, new InMemoryLeaveCatalogCache(), unitOfWork);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateLeavePolicyAsync(new LeavePolicyRequest(4, 2026, 12m)));

        Assert.Contains("leaveTypeId", exception.Errors.Keys);
        repository.Verify(item => item.Add(It.IsAny<LeavePolicy>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateLeavePolicyAsync_RejectsUnpaidLeaveTypeWithoutSaving()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetLeavePolicyForUpdateByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeavePolicy
            {
                Id = 9,
                LeaveTypeId = 1,
                LeaveType = new LeaveType { Id = 1, Code = "ANNUAL", DeductsBalance = true },
                Year = 2026,
                DaysPerYear = 12m
            });
        repository
            .Setup(item => item.GetLeaveTypeForUpdateByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveType
            {
                Id = 3,
                Code = "UNPAID",
                IsActive = true,
                DeductsBalance = false
            });
        var service = CreateService(repository, new InMemoryLeaveCatalogCache(), unitOfWork);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => service.UpdateLeavePolicyAsync(9, new LeavePolicyRequest(3, 2026, 12m)));

        Assert.Contains("leaveTypeId", exception.Errors.Keys);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateLeavePolicyAsync_RejectsCustomPolicy()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetLeavePolicyForUpdateByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeavePolicy
            {
                Id = 9,
                LeaveTypeId = 4,
                LeaveType = new LeaveType { Id = 4, Code = "PARENTAL" },
                Year = 2026,
                DaysPerYear = 12m
            });
        var service = CreateService(repository, new InMemoryLeaveCatalogCache(), unitOfWork);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.UpdateLeavePolicyAsync(9, new LeavePolicyRequest(1, 2026, 12m)));

        repository.Verify(
            item => item.GetLeaveTypeForUpdateByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetHolidaysAsync_FiltersCachedResultsByRequestedRange()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        repository
            .Setup(item => item.ListHolidaysAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Holiday { Id = 1, HolidayDate = new DateOnly(2026, 1, 1), Name = "New Year" },
                new Holiday { Id = 2, HolidayDate = new DateOnly(2026, 12, 25), Name = "Holiday" }
            ]);
        var service = CreateService(repository, cache);

        var result = await service.GetHolidaysAsync(
            new DateOnly(2026, 12, 1),
            new DateOnly(2026, 12, 31));

        var holiday = Assert.Single(result);
        Assert.Equal(2, holiday.Id);
        repository.Verify(
            item => item.ListHolidaysAsync(null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAuthoritativeLeavePoliciesAsync_UsesSqlInsteadOfCachedPolicy()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new Mock<ILeaveCatalogCache>();
        repository
            .Setup(item => item.ListLeavePoliciesAsync(2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new LeavePolicy
                {
                    Id = 42,
                    LeaveTypeId = 3,
                    LeaveType = new LeaveType { Id = 3, Code = "ANNUAL", Name = "Annual", IsActive = true },
                    Year = 2026,
                    DaysPerYear = 18m
                }
            ]);
        var service = CreateService(repository, cache.Object);

        var policy = Assert.Single(await service.GetAuthoritativeLeavePoliciesAsync(2026));

        Assert.Equal(18m, policy.DaysPerYear);
        repository.Verify(
            item => item.ListLeavePoliciesAsync(2026, It.IsAny<CancellationToken>()),
            Times.Once);
        cache.Verify(
            item => item.GetAsync<LeavePolicyResponse[]>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAuthoritativeHolidaysAsync_QueriesSqlForRequestedRangeWithoutCache()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new Mock<ILeaveCatalogCache>();
        repository
            .Setup(item => item.ListHolidaysAsync(
                new DateOnly(2026, 12, 1),
                new DateOnly(2026, 12, 31),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Holiday { Id = 8, HolidayDate = new DateOnly(2026, 12, 25), Name = "Holiday" }
            ]);
        var service = CreateService(repository, cache.Object);

        var holiday = Assert.Single(await service.GetAuthoritativeHolidaysAsync(
            new DateOnly(2026, 12, 1),
            new DateOnly(2026, 12, 31)));

        Assert.Equal(8, holiday.Id);
        cache.Verify(
            item => item.GetAsync<HolidayResponse[]>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateHolidayAsync_UpdatesEntryAndInvalidatesCacheAfterSave()
    {
        var holiday = new Holiday
        {
            Id = 2,
            HolidayDate = new DateOnly(2026, 1, 1),
            Name = "Old holiday"
        };
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        var unitOfWork = new Mock<IUnitOfWork>();
        var events = new List<string>();
        repository
            .Setup(item => item.GetHolidayForUpdateByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(holiday);
        repository
            .Setup(item => item.ExistsHolidayDateAsync(
                new DateOnly(2026, 2, 1),
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        unitOfWork
            .Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("save"))
            .ReturnsAsync(1);
        cache.Removed += key => events.Add($"remove:{key}");
        var service = CreateService(repository, cache, unitOfWork);

        var result = await service.UpdateHolidayAsync(
            2,
            new HolidayRequest(new DateOnly(2026, 2, 1), "Updated holiday"));

        Assert.Equal(new DateOnly(2026, 2, 1), result.HolidayDate);
        Assert.Equal("Updated holiday", result.Name);
        Assert.Equal(
            new[] { "save", "remove:hr:catalog:holidays" },
            events);
    }

    [Fact]
    public async Task DeleteHolidayAsync_RemovesEntryAndInvalidatesCacheAfterSave()
    {
        var holiday = new Holiday { Id = 3, HolidayDate = new DateOnly(2026, 12, 25), Name = "Holiday" };
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        var unitOfWork = new Mock<IUnitOfWork>();
        var events = new List<string>();
        repository
            .Setup(item => item.GetHolidayForUpdateByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(holiday);
        repository
            .Setup(item => item.Remove(holiday))
            .Callback(() => events.Add("remove"));
        unitOfWork
            .Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("save"))
            .ReturnsAsync(1);
        cache.Removed += key => events.Add($"cache:{key}");
        var service = CreateService(repository, cache, unitOfWork);

        await service.DeleteHolidayAsync(3);

        Assert.Equal(
            new[] { "remove", "save", "cache:hr:catalog:holidays" },
            events);
    }

    private static LeaveCatalogService CreateService(
        Mock<ILeaveCatalogRepository> repository,
        ILeaveCatalogCache cache,
        Mock<IUnitOfWork>? unitOfWork = null) =>
        new(repository.Object, cache, (unitOfWork ?? new Mock<IUnitOfWork>()).Object);

    private sealed class InMemoryLeaveCatalogCache : ILeaveCatalogCache
    {
        private readonly Dictionary<string, object> _values = new();

        public event Action<string>? Removed;

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _values.TryGetValue(key, out var value) && value is T typed
                    ? (T?)typed
                    : default);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            if (value is not null)
            {
                _values[key] = value;
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            Removed?.Invoke(key);
            return Task.CompletedTask;
        }
    }
}
