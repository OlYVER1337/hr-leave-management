using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_NormalizesEmailAndReturnsToken()
    {
        var now = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        var employee = new Employee
        {
            Email = "employee@example.com",
            FullName = "Test Employee",
            Role = Role.Employee
        };
        var response = new AuthenticationResponse(
            "access-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            employee.FullName,
            employee.Email,
            employee.Id,
            employee.Role.ToString());
        var repository = new Mock<IEmployeeRepository>();
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        var authorizationRepository = new Mock<IAuthorizationRepository>();
        RefreshToken? persistedRefreshToken = null;
        var unitOfWork = new Mock<IUnitOfWork>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var tokenGenerator = new Mock<IJwtTokenGenerator>();

        repository
            .Setup(item => item.GetActiveByEmailAsync(
                "employee@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        passwordHasher
            .Setup(item => item.Verify(employee, "correct-password"))
            .Returns(true);
        authorizationRepository
            .Setup(item => item.GetEffectivePermissionScopesAsync(
                employee.Id,
                now.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("leaves.read", "Self")]);
        tokenGenerator
            .Setup(item => item.GenerateToken(
                employee,
                It.Is<EffectiveAuthorization>(
                    value => value.HasScope("leaves.read", "Self"))))
            .Returns(response);
        refreshTokenRepository
            .Setup(item => item.Add(It.IsAny<RefreshToken>()))
            .Callback<RefreshToken>(token =>
            {
                persistedRefreshToken = token;
                Assert.Equal(employee.Id, token.EmployeeId);
                Assert.NotEqual("opaque-refresh-token", token.TokenHash);
            });
        unitOfWork
            .Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = CreateService(
            repository.Object,
            passwordHasher.Object,
            tokenGenerator.Object,
            refreshTokenRepository.Object,
            unitOfWork.Object,
            authorizationRepository.Object,
            new FixedTimeProvider(now));

        var result = await service.AuthenticateAsync(
            "  EMPLOYEE@EXAMPLE.COM ",
            "correct-password");

        Assert.Same(response, result.Response);
        Assert.Equal(86, result.RefreshToken.Length);
        Assert.Equal(now.AddDays(15).UtcDateTime, persistedRefreshToken?.ExpiresAtUtc);
        Assert.Equal(now.AddDays(15), result.RefreshTokenExpiresAtUtc);
        repository.Verify(
            item => item.GetActiveByEmailAsync(
                "employee@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once);
        authorizationRepository.Verify(
            item => item.GetEffectivePermissionScopesAsync(
                employee.Id,
                now.UtcDateTime,
                It.IsAny<CancellationToken>()),
            Times.Once);
        tokenGenerator.Verify(
            item => item.GenerateToken(employee, It.Is<EffectiveAuthorization>(
                value => value.HasScope("leaves.read", "Self"))),
            Times.Once);
        refreshTokenRepository.Verify(item => item.Add(It.IsAny<RefreshToken>()), Times.Once);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "some-password")]
    [InlineData("employee@example.com", "")]
    [InlineData("   ", "some-password")]
    public async Task AuthenticateAsync_RejectsMissingCredentials(
        string email,
        string password)
    {
        var repository = new Mock<IEmployeeRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(
            repository.Object,
            Mock.Of<IPasswordHasher>(),
            Mock.Of<IJwtTokenGenerator>(),
            Mock.Of<IRefreshTokenRepository>(),
            unitOfWork.Object,
            Mock.Of<IAuthorizationRepository>(),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(
            () => service.AuthenticateAsync(email, password));

        Assert.Equal(401, exception.StatusCode);
        repository.Verify(
            item => item.GetActiveByEmailAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_UsesSameErrorForUnknownUserAndWrongPassword()
    {
        var repository = new Mock<IEmployeeRepository>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var service = CreateService(
            repository.Object,
            passwordHasher.Object,
            Mock.Of<IJwtTokenGenerator>(),
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IAuthorizationRepository>(),
            TimeProvider.System);
        repository
            .Setup(item => item.GetActiveByEmailAsync(
                "unknown@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);
        var employee = new Employee { Email = "employee@example.com" };
        repository
            .Setup(item => item.GetActiveByEmailAsync(
                "employee@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        passwordHasher.Setup(item => item.Verify(employee, "wrong-password")).Returns(false);

        var unknownUser = await Assert.ThrowsAsync<UnauthorizedException>(
            () => service.AuthenticateAsync("unknown@example.com", "wrong-password"));
        var wrongPassword = await Assert.ThrowsAsync<UnauthorizedException>(
            () => service.AuthenticateAsync("employee@example.com", "wrong-password"));

        Assert.Equal(unknownUser.Message, wrongPassword.Message);
        Assert.Equal(401, unknownUser.StatusCode);
    }

    [Fact]
    public async Task RefreshAsync_RotatesPersistedTokenAndReevaluatesAuthorization()
    {
        var now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var employee = new Employee
        {
            Email = "employee@example.com",
            FullName = "Test Employee",
            Role = Role.Employee,
            IsActive = true
        };
        RefreshToken? replacement = null;
        var response = new AuthenticationResponse(
            "new-access-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            employee.FullName,
            employee.Email,
            employee.Id,
            employee.Role.ToString());
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        var tokenGenerator = new Mock<IJwtTokenGenerator>();
        var authorizationRepository = new Mock<IAuthorizationRepository>();
        refreshTokenRepository
            .Setup(item => item.RotateAsync(
                It.IsAny<string>(),
                It.IsAny<RefreshToken>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RefreshToken, DateTime, CancellationToken>(
                (_, token, _, _) =>
                {
                    token.EmployeeId = employee.Id;
                    replacement = token;
                })
            .ReturnsAsync(employee);
        authorizationRepository
            .Setup(item => item.GetEffectivePermissionScopesAsync(
                employee.Id,
                now.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("leaves.read", "Self")]);
        tokenGenerator
            .Setup(item => item.GenerateToken(
                employee,
                It.Is<EffectiveAuthorization>(
                    value => value.HasScope("leaves.read", "Self"))))
            .Returns(response);
        var service = CreateService(
            Mock.Of<IEmployeeRepository>(),
            Mock.Of<IPasswordHasher>(),
            tokenGenerator.Object,
            refreshTokenRepository.Object,
            Mock.Of<IUnitOfWork>(),
            authorizationRepository.Object,
            new FixedTimeProvider(now));

        var result = await service.RefreshAsync("old-refresh-token");

        Assert.Same(response, result.Response);
        Assert.NotEqual("old-refresh-token", result.RefreshToken);
        Assert.NotNull(replacement);
        Assert.Equal(employee.Id, replacement.EmployeeId);
        Assert.NotEqual(result.RefreshToken, replacement.TokenHash);
        Assert.Equal(now.AddDays(15).UtcDateTime, replacement.ExpiresAtUtc);
        Assert.Equal(now.AddDays(15), result.RefreshTokenExpiresAtUtc);
        authorizationRepository.Verify(
            item => item.GetEffectivePermissionScopesAsync(
                employee.Id,
                now.UtcDateTime,
                It.IsAny<CancellationToken>()),
            Times.Once);
        tokenGenerator.Verify(
            item => item.GenerateToken(employee, It.IsAny<EffectiveAuthorization>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_AfterRoleRevocation_IssuesReducedAuthorization()
    {
        var now = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
        var employee = new Employee { IsActive = true };
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        var authorizationRepository = new Mock<IAuthorizationRepository>();
        var tokenGenerator = new Mock<IJwtTokenGenerator>();

        refreshTokenRepository
            .Setup(item => item.RotateAsync(
                It.IsAny<string>(),
                It.IsAny<RefreshToken>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RefreshToken, DateTime, CancellationToken>(
                (_, token, _, _) => token.EmployeeId = employee.Id)
            .ReturnsAsync(employee);

        authorizationRepository
            .SetupSequence(item => item.GetEffectivePermissionScopesAsync(
                employee.Id,
                now.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PermissionScopeGrant("employees.read", "All"),
                new PermissionScopeGrant("leaves.read", "Team")
            ])
            .ReturnsAsync(
            [
                new PermissionScopeGrant("leaves.read", "Self")
            ]);

        tokenGenerator
            .Setup(item => item.GenerateToken(employee, It.IsAny<EffectiveAuthorization>()))
            .Returns(new AuthenticationResponse(
                "access-token",
                now.AddMinutes(15),
                employee.FullName,
                employee.Email,
                employee.Id,
                employee.Role.ToString()));

        var service = CreateService(
            Mock.Of<IEmployeeRepository>(),
            Mock.Of<IPasswordHasher>(),
            tokenGenerator.Object,
            refreshTokenRepository.Object,
            Mock.Of<IUnitOfWork>(),
            authorizationRepository.Object,
            new FixedTimeProvider(now));

        var first = await service.RefreshAsync("first-refresh-token");
        var second = await service.RefreshAsync("second-refresh-token");

        Assert.NotNull(first.Response);
        Assert.NotNull(second.Response);
        tokenGenerator.Verify(
            item => item.GenerateToken(
                employee,
                It.Is<EffectiveAuthorization>(value =>
                    value.HasScope("employees.read", "All") &&
                    value.HasScope("leaves.read", "Team"))),
            Times.Once);
        tokenGenerator.Verify(
            item => item.GenerateToken(
                employee,
                It.Is<EffectiveAuthorization>(value =>
                    !value.HasPermission("employees.read") &&
                    value.HasScope("leaves.read", "Self"))),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_RejectsMissingOrExpiredRefreshToken()
    {
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        refreshTokenRepository
            .Setup(item => item.RotateAsync(
                It.IsAny<string>(),
                It.IsAny<RefreshToken>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);
        var service = CreateService(
            Mock.Of<IEmployeeRepository>(),
            Mock.Of<IPasswordHasher>(),
            Mock.Of<IJwtTokenGenerator>(),
            refreshTokenRepository.Object,
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IAuthorizationRepository>(),
            TimeProvider.System);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(null));
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => service.RefreshAsync("expired-or-revoked-token"));

        refreshTokenRepository.Verify(
            item => item.RotateAsync(
                It.IsAny<string>(),
                It.IsAny<RefreshToken>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static AuthenticationService CreateService(
        IEmployeeRepository employeeRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IAuthorizationRepository authorizationRepository,
        TimeProvider timeProvider) =>
        new(
            employeeRepository,
            passwordHasher,
            jwtTokenGenerator,
            refreshTokenRepository,
            unitOfWork,
            new AuthorizationService(authorizationRepository, timeProvider),
            timeProvider);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
