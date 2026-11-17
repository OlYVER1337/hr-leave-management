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
    // Chỉ ANNUAL vừa active vừa trừ quota; SICK inactive và UNPAID không trừ quota nên không được tạo balance.
    [Fact]
    public async Task SynchronizeMineAsync_CreatesEntitlementsForActiveDeductingTypesOnly()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        // Catalog giả lập ba trường hợp: active + trừ quota, inactive + trừ quota và active + không trừ quota.
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

    // HR cho phép quota tối đa 366 ngày; Leave Service phải chấp nhận đúng giá trị biên này.
    [Fact]
    public async Task SynchronizeMineAsync_AcceptsMaximumHrPolicyEntitlement()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetLeaveCatalogAsync(2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrLeaveCatalog(
                [new HrLeaveType(1, "ANNUAL", "Annual", true, true, true)],
                [new HrLeavePolicy(11, 1, "ANNUAL", "Annual", 2027, 366m)]));
        repository.Setup(item => item.SynchronizeBalancesAsync(
                employeeId,
                2027,
                It.Is<IReadOnlyList<LeaveBalanceEntitlement>>(values =>
                    values.Count == 1 && values[0].Entitled == 366m),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LeaveBalance>)[new LeaveBalance
            {
                EmployeeId = employeeId,
                LeaveTypeId = 1,
                Year = 2027,
                Entitled = 366m
            }]);
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, TimeProvider.System);

        var balance = Assert.Single(await service.SynchronizeMineAsync(employeeId, 2027, "Bearer token"));

        Assert.Equal(366m, balance.Entitled);
        Assert.Equal(366m, balance.Available);
    }

    // Kiểm tra quota âm, vượt giới hạn HR và có nhiều hơn một chữ số thập phân.
    [Theory]
    [InlineData(-0.1)]
    [InlineData(366.1)]
    [InlineData(15.25)]
    public async Task SynchronizeMineAsync_RejectsInvalidPolicyEntitlementBeforeWriting(decimal days)
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetLeaveCatalogAsync(2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrLeaveCatalog(
                [new HrLeaveType(1, "ANNUAL", "Annual", true, true, true)],
                [new HrLeavePolicy(11, 1, "ANNUAL", "Annual", 2027, days)]));
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveConflictException>(() =>
            service.SynchronizeMineAsync(employeeId, 2027, "Bearer token"));

        repository.Verify(item => item.SynchronizeBalancesAsync(
            It.IsAny<Guid>(),
            It.IsAny<int>(),
            It.IsAny<IReadOnlyList<LeaveBalanceEntitlement>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // 18:00 UTC ngày 31/12 tương ứng 01:00 ngày 01/01 tại Việt Nam, nên năm mặc định phải chuyển sang năm mới.
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
        // Cố định thời điểm sát giao thừa UTC để kiểm tra chuyển đổi năm theo giờ Việt Nam.
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero));
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, timeProvider);

        await service.SynchronizeMineAsync(employeeId, null, "Bearer token");

        hrClient.Verify(item => item.GetLeaveCatalogAsync(
            2027, "Bearer token", It.IsAny<CancellationToken>()), Times.Once);
    }

    // Một leave type có hai policy cùng năm là dữ liệu không nhất quán; không được ghi balance.
    [Fact]
    public async Task SynchronizeMineAsync_RejectsDuplicatePoliciesBeforeWriting()
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

    // Leave type active nhưng thiếu policy cho năm yêu cầu: phải báo conflict trước khi gọi repository.
    [Fact]
    public async Task SynchronizeMineAsync_RejectsMissingPolicyBeforeWriting()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();
        hrClient.Setup(item => item.GetLeaveCatalogAsync(2027, "Bearer token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrLeaveCatalog(
                [new HrLeaveType(1, "ANNUAL", "Annual", true, true, true)],
                []));
        var service = new LeaveBalanceService(repository.Object, hrClient.Object, TimeProvider.System);

        await Assert.ThrowsAsync<LeaveConflictException>(() =>
            service.SynchronizeMineAsync(employeeId, 2027, "Bearer token"));

        repository.Verify(item => item.SynchronizeBalancesAsync(
            It.IsAny<Guid>(),
            It.IsAny<int>(),
            It.IsAny<IReadOnlyList<LeaveBalanceEntitlement>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // HR lỗi thì Leave phải fail closed, không ghi balance dựa trên quota cũ hoặc không xác định.
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

    // Kiểm tra employee ID và năm trước khi gọi HR hoặc database.
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
