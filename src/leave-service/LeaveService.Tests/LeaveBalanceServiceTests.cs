using LeaveService.Application.Abstractions;
using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using LeaveService.Domain.Entities;
using Moq;
using Xunit;

namespace LeaveService.Tests;

public sealed class LeaveBalanceServiceTests
{
    [Fact]
    public async Task SynchronizeMineAsync_CreatesEntitlementsForActiveDeductingTypesOnly()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var catalog = new HrLeaveCatalog(
            [
                new HrLeaveType(1, "ANNUAL", "Annual", true, true, true),
                new HrLeaveType(2, "SICK", "Sick", true, true, false),
                new HrLeaveType(3, "UNPAID", "Unpaid", false, false, true)
            ],
            [
                new HrLeavePolicy(11, 1, "ANNUAL", "Annual", 2027, 15m)
            ]);
        hrClient.Setup(item => item.GetLeaveCatalogAsync(2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        repository.Setup(item => item.SynchronizeBalancesAsync(
                employeeId,
                2027,
                It.Is<IReadOnlyList<LeaveBalanceEntitlement>>(values =>
                    values.Count == 1 &&
                    values[0] == new LeaveBalanceEntitlement(1, 15m)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int year, IReadOnlyList<LeaveBalanceEntitlement> values, CancellationToken _) =>
                (IReadOnlyList<LeaveBalance>)[new LeaveBalance
                {
                    EmployeeId = employeeId,
                    LeaveTypeId = values[0].LeaveTypeId,
                    Year = year,
                    Entitled = values[0].Entitled
                }]);
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, TimeProvider.System);

        var result = await service.SynchronizeMineAsync(employeeId, 2027, "Bearer token");

        var balance = Assert.Single(result);
        Assert.Equal(new LeaveBalanceResponse(1, 2027, 15m, 0m, 0m, 15m), balance);
        hrClient.Verify(item => item.GetLeaveCatalogAsync(
            2027, "Bearer token", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.SynchronizeBalancesAsync(
            employeeId,
            2027,
            It.IsAny<IReadOnlyList<LeaveBalanceEntitlement>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SynchronizeMineAsync_UsesVietnamBusinessYearWhenYearIsOmitted()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetLeaveCatalogAsync(2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrLeaveCatalog([], []));
        repository.Setup(item => item.SynchronizeBalancesAsync(
                employeeId, 2027, It.IsAny<IReadOnlyList<LeaveBalanceEntitlement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LeaveBalance>)[]);
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero));
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, timeProvider);

        await service.SynchronizeMineAsync(employeeId, null, "Bearer token");

        hrClient.Verify(item => item.GetLeaveCatalogAsync(
            2027, "Bearer token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SynchronizeMineAsync_RejectsMissingOrDuplicatePolicyBeforeWriting()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetLeaveCatalogAsync(2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrLeaveCatalog(
                [new HrLeaveType(1, "ANNUAL", "Annual", true, true, true)],
                [
                    new HrLeavePolicy(1, 1, "ANNUAL", "Annual", 2027, 15m),
                    new HrLeavePolicy(2, 1, "ANNUAL", "Annual", 2027, 16m)
                ]));
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveConflictException>(() =>
            service.SynchronizeMineAsync(employeeId, 2027, "Bearer token"));

        repository.Verify(item => item.SynchronizeBalancesAsync(
            It.IsAny<Guid>(),
            It.IsAny<int>(),
            It.IsAny<IReadOnlyList<LeaveBalanceEntitlement>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SynchronizeMineAsync_DoesNotWriteWhenHrCatalogIsUnavailable()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetLeaveCatalogAsync(
                2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DependencyUnavailableException("HR service"));
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            service.SynchronizeMineAsync(employeeId, 2027, "Bearer token"));

        repository.Verify(item => item.SynchronizeBalancesAsync(
            It.IsAny<Guid>(),
            It.IsAny<int>(),
            It.IsAny<IReadOnlyList<LeaveBalanceEntitlement>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SynchronizeMineAsync_RejectsInvalidEmployeeOrYear()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveForbiddenException>(() =>
            service.SynchronizeMineAsync(Guid.Empty, 2027, "Bearer token"));
        await Assert.ThrowsAsync<LeaveValidationException>(() =>
            service.SynchronizeMineAsync(Guid.NewGuid(), 1899, "Bearer token"));

        repository.VerifyNoOtherCalls();
        hrClient.VerifyNoOtherCalls();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
