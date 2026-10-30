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
    public async Task GetLeaveTypesAsync_CachesAllTypesAndFiltersInactiveForRegularList()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        repository
            .Setup(item => item.ListLeaveTypesAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new LeaveType { Id = 1, Code = "ANNUAL", Name = "Annual", IsActive = true },
                new LeaveType { Id = 2, Code = "OLD", Name = "Old", IsActive = false }
            ]);
        var service = CreateService(repository, cache);

        var activeTypes = await service.GetLeaveTypesAsync(includeInactive: false);
        var allTypes = await service.GetLeaveTypesAsync(includeInactive: true);

        Assert.Single(activeTypes);
        Assert.Equal("ANNUAL", activeTypes[0].Code);
        Assert.Equal(2, allTypes.Count);
        repository.Verify(
            item => item.ListLeaveTypesAsync(true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateLeaveTypeAsync_NormalizesCodeAndInvalidatesCacheAfterSave()
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        var cache = new InMemoryLeaveCatalogCache();
        var unitOfWork = new Mock<IUnitOfWork>();
        var events = new List<string>();
        LeaveType? addedLeaveType = null;
        repository
            .Setup(item => item.ExistsLeaveTypeCodeAsync(
                "PERSONAL",
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
            new CreateLeaveTypeRequest(" personal ", " Personal ", true, true));

        Assert.Equal("PERSONAL", result.Code);
        Assert.True(result.IsActive);
        Assert.NotNull(addedLeaveType);
        Assert.Equal("Personal", addedLeaveType.Name);
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

        Assert.Contains("isPaid", exception.Errors.Keys);
        repository.Verify(item => item.Add(It.IsAny<LeaveType>()), Times.Never);
        unitOfWork.Verify(
            item => item.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateLeaveTypeAsync_UpdatesMutableFieldsAndInvalidatesRelatedCachesAfterSave()
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
    public async Task UpdateLeaveTypeAsync_RejectsChangingUnpaidTypeToPaid()
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

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => service.UpdateLeaveTypeAsync(
                3,
                new UpdateLeaveTypeRequest("Unpaid leave", true, false, true)));

        Assert.Contains("isPaid", exception.Errors.Keys);
        Assert.False(leaveType.IsPaid);
        unitOfWork.Verify(
            item => item.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
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
            .ReturnsAsync(new LeaveType { Id = 4, IsActive = false });
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
