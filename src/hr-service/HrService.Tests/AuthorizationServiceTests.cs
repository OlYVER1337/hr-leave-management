using HrService.Application.Abstractions;
using HrService.Application.Dtos;
using HrService.Application.Services;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class AuthorizationServiceTests
{
    [Fact]
    public async Task GetEffectiveAuthorizationAsync_UnionsAndDeduplicatesScopesAcrossRoles()
    {
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 11, 5, 0, 0, TimeSpan.Zero);
        var repository = new Mock<IAuthorizationRepository>();
        repository
            .Setup(item => item.GetEffectivePermissionScopesAsync(
                employeeId,
                now.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PermissionScopeGrant("employees.read", "Self"),
                new PermissionScopeGrant("employees.read", "Team"),
                new PermissionScopeGrant("employees.read", "Team"),
                new PermissionScopeGrant("leaves.read", "All")
            ]);

        var service = new AuthorizationService(repository.Object, new FixedTimeProvider(now));

        var result = await service.GetEffectiveAuthorizationAsync(employeeId);

        Assert.True(result.HasPermission("employees.read"));
        Assert.True(result.HasPermission("EMPLOYEES.READ"));
        Assert.Equal(
            new[] { "Self", "Team" },
            result.GetScopes("employees.read"));
        Assert.True(result.HasScope("employees.read", "team"));
        Assert.True(result.HasScope("leaves.read", "All"));
        Assert.False(result.HasPermission("leaves.decide"));

        repository.Verify(
            item => item.GetEffectivePermissionScopesAsync(
                employeeId,
                now.UtcDateTime,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEffectiveAuthorizationAsync_RejectsUnsupportedDepartmentScopeInV1()
    {
        var employeeId = Guid.NewGuid();
        var repository = new Mock<IAuthorizationRepository>();
        repository
            .Setup(item => item.GetEffectivePermissionScopesAsync(
                employeeId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PermissionScopeGrant("employees.read", "Department")
            ]);

        var service = new AuthorizationService(repository.Object, TimeProvider.System);

        var result = await service.GetEffectiveAuthorizationAsync(employeeId);

        Assert.False(result.HasPermission("employees.read"));
        Assert.Empty(result.Permissions);
    }

    [Fact]
    public async Task GetEffectiveAuthorizationAsync_ReturnsEmptyAuthorizationWhenNoGrantsExist()
    {
        var repository = new Mock<IAuthorizationRepository>();
        repository
            .Setup(item => item.GetEffectivePermissionScopesAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PermissionScopeGrant>());

        var service = new AuthorizationService(repository.Object, TimeProvider.System);

        var result = await service.GetEffectiveAuthorizationAsync(Guid.NewGuid());

        Assert.Empty(result.Permissions);
        Assert.False(result.HasPermission("employees.read"));
        Assert.Empty(result.GetScopes("employees.read"));
    }

    [Fact]
    public async Task GetEffectiveAuthorizationAsync_RejectsEmptyEmployeeIdWithoutQueryingRepository()
    {
        var repository = new Mock<IAuthorizationRepository>();
        var service = new AuthorizationService(repository.Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetEffectiveAuthorizationAsync(Guid.Empty));

        repository.Verify(
            item => item.GetEffectivePermissionScopesAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
