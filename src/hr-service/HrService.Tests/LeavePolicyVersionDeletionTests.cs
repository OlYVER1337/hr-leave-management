using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class LeavePolicyVersionDeletionTests
{
    private const string Auth = "Bearer hr-token";

    // 19/11/2026 09:00 giờ Việt Nam: năm 2027 còn sửa được, phiên bản 01/03/2027 chưa hiệu lực.
    private static readonly TimeProvider Clock = new FixedUtcClock(new DateTimeOffset(2026, 11, 19, 2, 0, 0, TimeSpan.Zero));

    private static LeavePolicy FuturePolicy() => new()
    {
        Id = 42,
        Year = 2027,
        EffectiveFrom = new DateOnly(2027, 3, 1),
        DaysPerYear = 10m,
        LeaveType = new LeaveType { Id = 1, Code = "ANNUAL", Name = "Nghỉ phép năm", DeductsBalance = true }
    };

    private static (LeavePolicyVersionDeletionService Service, Mock<ILeaveCatalogRepository> Repository,
        Mock<ILeavePolicyUsageClient> Usage) Create(LeavePolicy? policy)
    {
        var repository = new Mock<ILeaveCatalogRepository>();
        repository.Setup(item => item.GetLeavePolicyForUpdateByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        var catalog = new LeaveCatalogService(
            repository.Object,
            new Mock<ILeaveCatalogCache>().Object,
            new Mock<IUnitOfWork>().Object,
            Clock);
        var usage = new Mock<ILeavePolicyUsageClient>();
        return (new LeavePolicyVersionDeletionService(catalog, usage.Object), repository, usage);
    }

    [Fact]
    public async Task DeletesFutureVersionWhenLeaveReportsNoReferences()
    {
        var (service, repository, usage) = Create(FuturePolicy());
        usage.Setup(item => item.CountReferencesAsync(42, Auth, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await service.DeleteAsync(42, Auth);

        repository.Verify(item => item.Remove(It.IsAny<LeavePolicy>()), Times.Once);
    }

    [Fact]
    public async Task ReturnsConflictAndKeepsVersionWhenReferenced()
    {
        var (service, repository, usage) = Create(FuturePolicy());
        usage.Setup(item => item.CountReferencesAsync(42, Auth, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(42, Auth));
        repository.Verify(item => item.Remove(It.IsAny<LeavePolicy>()), Times.Never);
    }

    [Fact]
    public async Task FailsClosedWhenLeaveIsUnavailable()
    {
        var (service, repository, usage) = Create(FuturePolicy());
        usage.Setup(item => item.CountReferencesAsync(42, Auth, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LeavePolicyUsageUnavailableException("Leave Service is unreachable."));

        await Assert.ThrowsAsync<LeavePolicyUsageUnavailableException>(() => service.DeleteAsync(42, Auth));
        repository.Verify(item => item.Remove(It.IsAny<LeavePolicy>()), Times.Never);
    }

    [Fact]
    public async Task DoesNotAskLeaveWhenVersionAlreadyEffective()
    {
        var effective = FuturePolicy();
        effective.EffectiveFrom = new DateOnly(2026, 11, 1);
        var (service, repository, usage) = Create(effective);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(42, Auth));
        usage.Verify(item => item.CountReferencesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(item => item.Remove(It.IsAny<LeavePolicy>()), Times.Never);
    }

    private sealed class FixedUtcClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
