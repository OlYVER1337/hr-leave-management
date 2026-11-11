using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class UserRoleServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GrantAsync_AddsAssignmentAndAuditWhenGrantIsWithinActorAccess()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var role = CreateRole("Auditor");
        UserRole? addedAssignment = null;
        AuthzAuditLog? addedAudit = null;
        var repository = new Mock<IUserRoleRepository>();
        repository.Setup(item => item.GetEmployeeByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = targetId, IsActive = true });
        repository.Setup(item => item.GetRoleByCodeAsync("Auditor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        repository.Setup(item => item.GetRolePermissionScopesAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("employees.read", "All")]);
        repository.Setup(item => item.GetUnrevokedAssignmentAsync(
                targetId, role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserRole?)null);
        repository.Setup(item => item.Add(It.IsAny<UserRole>()))
            .Callback<UserRole>(assignment => addedAssignment = assignment);
        repository.Setup(item => item.Add(It.IsAny<AuthzAuditLog>()))
            .Callback<AuthzAuditLog>(audit => addedAudit = audit);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = CreateService(
            repository,
            unitOfWork,
            [
                new PermissionScopeGrant("roles.manage", "All"),
                new PermissionScopeGrant("employees.read", "All")
            ]);

        var result = await service.GrantAsync(
            actorId,
            targetId,
            new GrantUserRoleRequest(" Auditor ", null));

        Assert.Equal("Auditor", result.RoleCode);
        Assert.Equal(targetId, addedAssignment?.EmployeeId);
        Assert.Equal(role.Id, addedAssignment?.RoleId);
        Assert.Equal(actorId, addedAssignment?.GrantedByEmployeeId);
        Assert.Equal(Now.UtcDateTime, addedAssignment?.GrantedAtUtc);
        Assert.Equal("USER_ROLE_GRANTED", addedAudit?.Action);
        Assert.Equal(actorId, addedAudit?.ActorEmployeeId);
        Assert.Equal(targetId, addedAudit?.TargetEmployeeId);
        Assert.Equal("UserRole", addedAudit?.TargetType);
        Assert.NotNull(addedAudit?.AfterJson);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantAsync_RenewsExpiredAssignmentInPlaceAndAuditsPreviousState()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var role = CreateRole("Auditor");
        var existing = new UserRole
        {
            Id = 12,
            EmployeeId = targetId,
            RoleId = role.Id,
            Role = role,
            GrantedAtUtc = Now.AddDays(-20).UtcDateTime,
            ExpiresAtUtc = Now.AddDays(-1).UtcDateTime
        };
        AuthzAuditLog? audit = null;
        var repository = new Mock<IUserRoleRepository>();
        repository.Setup(item => item.GetEmployeeByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = targetId, IsActive = true });
        repository.Setup(item => item.GetRoleByCodeAsync("Auditor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        repository.Setup(item => item.GetRolePermissionScopesAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("employees.read", "All")]);
        repository.Setup(item => item.GetUnrevokedAssignmentAsync(
                targetId, role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        repository.Setup(item => item.Add(It.IsAny<AuthzAuditLog>()))
            .Callback<AuthzAuditLog>(value => audit = value);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = CreateService(
            repository,
            unitOfWork,
            [
                new PermissionScopeGrant("roles.manage", "All"),
                new PermissionScopeGrant("employees.read", "All")
            ]);

        var result = await service.GrantAsync(
            actorId,
            targetId,
            new GrantUserRoleRequest("Auditor", Now.AddDays(30)));

        Assert.Equal("Auditor", result.RoleCode);
        Assert.Equal(Now.UtcDateTime, existing.GrantedAtUtc);
        Assert.Equal(Now.AddDays(30).UtcDateTime, existing.ExpiresAtUtc);
        Assert.Equal(actorId, existing.GrantedByEmployeeId);
        Assert.Null(existing.RevokedAtUtc);
        Assert.Equal("USER_ROLE_RENEWED", audit?.Action);
        Assert.Contains(Now.AddDays(-1).UtcDateTime.ToString("O"), audit?.BeforeJson);
        repository.Verify(item => item.Add(It.IsAny<UserRole>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantAsync_RejectsGrantThatExceedsActorsPermissionScope()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var role = CreateRole("WideReader");
        var repository = new Mock<IUserRoleRepository>();
        repository.Setup(item => item.GetEmployeeByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = targetId, IsActive = true });
        repository.Setup(item => item.GetRoleByCodeAsync("WideReader", It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        repository.Setup(item => item.GetRolePermissionScopesAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("employees.read", "All")]);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(
            repository,
            unitOfWork,
            [
                new PermissionScopeGrant("roles.manage", "All"),
                new PermissionScopeGrant("employees.read", "Self")
            ]);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GrantAsync(
                actorId,
                targetId,
                new GrantUserRoleRequest("WideReader", null)));

        repository.Verify(item => item.Add(It.IsAny<UserRole>()), Times.Never);
        repository.Verify(item => item.Add(It.IsAny<AuthzAuditLog>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantAsync_RejectsSelfAssignment()
    {
        var actorId = Guid.NewGuid();
        var repository = new Mock<IUserRoleRepository>();
        var service = CreateService(
            repository,
            new Mock<IUnitOfWork>(),
            [new PermissionScopeGrant("roles.manage", "All")]);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GrantAsync(
                actorId,
                actorId,
                new GrantUserRoleRequest("HR", null)));

        repository.Verify(
            item => item.GetEmployeeByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GrantAsync_RejectsExpirationForRoleThatGrantsRoleManagement()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var role = CreateRole("HR");
        var repository = new Mock<IUserRoleRepository>();
        repository.Setup(item => item.GetEmployeeByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = targetId, IsActive = true });
        repository.Setup(item => item.GetRoleByCodeAsync("HR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        repository.Setup(item => item.GetRolePermissionScopesAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("roles.manage", "All")]);
        var service = CreateService(
            repository,
            new Mock<IUnitOfWork>(),
            [new PermissionScopeGrant("roles.manage", "All")]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.GrantAsync(
                actorId,
                targetId,
                new GrantUserRoleRequest("HR", Now.AddDays(10))));

        repository.Verify(item => item.Add(It.IsAny<UserRole>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_ProtectsLastActiveRoleManagementAdministrator()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var role = CreateRole("HR");
        var assignment = new UserRole
        {
            EmployeeId = targetId,
            RoleId = role.Id,
            Role = role,
            GrantedAtUtc = Now.AddDays(-20).UtcDateTime
        };
        var repository = new Mock<IUserRoleRepository>();
        repository.Setup(item => item.GetEmployeeByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = targetId, IsActive = true });
        repository.Setup(item => item.GetRoleByCodeAsync("HR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        repository.Setup(item => item.GetUnrevokedAssignmentAsync(
                targetId, role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignment);
        repository.Setup(item => item.GetRolePermissionScopesAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("roles.manage", "All")]);
        repository.Setup(item => item.HasOtherActiveRoleManagersAsync(
                targetId, role.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(
            repository,
            unitOfWork,
            [new PermissionScopeGrant("roles.manage", "All")]);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RevokeAsync(actorId, targetId, "HR"));

        Assert.Null(assignment.RevokedAtUtc);
        repository.Verify(item => item.Add(It.IsAny<AuthzAuditLog>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_RecordsActorAndAuditWhenAnotherAdministratorExists()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var role = CreateRole("HR");
        var assignment = new UserRole
        {
            EmployeeId = targetId,
            RoleId = role.Id,
            Role = role,
            GrantedAtUtc = Now.AddDays(-20).UtcDateTime
        };
        AuthzAuditLog? audit = null;
        var repository = new Mock<IUserRoleRepository>();
        repository.Setup(item => item.GetEmployeeByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = targetId, IsActive = true });
        repository.Setup(item => item.GetRoleByCodeAsync("HR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        repository.Setup(item => item.GetUnrevokedAssignmentAsync(
                targetId, role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignment);
        repository.Setup(item => item.GetRolePermissionScopesAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("roles.manage", "All")]);
        repository.Setup(item => item.HasOtherActiveRoleManagersAsync(
                targetId, role.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository.Setup(item => item.Add(It.IsAny<AuthzAuditLog>()))
            .Callback<AuthzAuditLog>(value => audit = value);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = CreateService(
            repository,
            unitOfWork,
            [new PermissionScopeGrant("roles.manage", "All")]);

        await service.RevokeAsync(actorId, targetId, "HR");

        Assert.Equal(Now.UtcDateTime, assignment.RevokedAtUtc);
        Assert.Equal(actorId, assignment.RevokedByEmployeeId);
        Assert.Equal("USER_ROLE_REVOKED", audit?.Action);
        Assert.Equal(actorId, audit?.ActorEmployeeId);
        Assert.Equal(targetId, audit?.TargetEmployeeId);
        Assert.NotNull(audit?.BeforeJson);
        Assert.NotNull(audit?.AfterJson);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static UserRoleService CreateService(
        Mock<IUserRoleRepository> repository,
        Mock<IUnitOfWork> unitOfWork,
        IReadOnlyList<PermissionScopeGrant> actorGrants)
    {
        var authorizationRepository = new Mock<IAuthorizationRepository>();
        authorizationRepository
            .Setup(item => item.GetEffectivePermissionScopesAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(actorGrants);

        return new UserRoleService(
            repository.Object,
            new AuthorizationService(authorizationRepository.Object, new FixedTimeProvider(Now)),
            unitOfWork.Object,
            new FixedTimeProvider(Now));
    }

    private static SecurityRole CreateRole(string code) =>
        new()
        {
            Id = 10,
            Code = code,
            Name = code,
            IsSystem = true,
            IsActive = true,
            CreatedAtUtc = Now.UtcDateTime
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
