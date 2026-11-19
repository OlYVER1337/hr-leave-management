using LeaveService.Application.Abstractions;
using LeaveService.Application.Services;
using Moq;
using Xunit;

namespace LeaveService.Tests;

public sealed class LeavePolicyUsageTests
{
    [Fact]
    public async Task GetPolicyVersionReferenceCount_ReturnsRepositoryCount()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.CountPolicyVersionReferencesAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        var service = new LeaveRequestService(
            repository.Object,
            new Mock<ILeaveHrClient>().Object,
            TimeProvider.System);

        Assert.Equal(3, await service.GetPolicyVersionReferenceCountAsync(7));
    }

    [Fact]
    public async Task GetPolicyVersionReferenceCount_ReturnsZeroWhenUnreferenced()
    {
        var repository = new Mock<ILeaveRequestRepository>();
        repository
            .Setup(item => item.CountPolicyVersionReferencesAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var service = new LeaveRequestService(
            repository.Object,
            new Mock<ILeaveHrClient>().Object,
            TimeProvider.System);

        Assert.Equal(0, await service.GetPolicyVersionReferenceCountAsync(9));
    }
}
