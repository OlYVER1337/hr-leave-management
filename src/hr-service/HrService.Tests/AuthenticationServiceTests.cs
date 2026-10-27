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
        var employee = new Employee
        {
            Email = "employee@example.com",
            FullName = "Test Employee",
            Role = Role.Employee
        };
        var response = new AuthenticationResponse(
            "access-token",
            DateTimeOffset.UtcNow.AddMinutes(30),
            employee.FullName,
            employee.Email,
            employee.Id,
            employee.Role.ToString());
        var repository = new Mock<IEmployeeRepository>();
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
        tokenGenerator.Setup(item => item.GenerateToken(employee)).Returns(response);

        var service = new AuthenticationService(
            repository.Object,
            passwordHasher.Object,
            tokenGenerator.Object);

        var result = await service.AuthenticateAsync(
            "  EMPLOYEE@EXAMPLE.COM ",
            "correct-password");

        Assert.Same(response, result);
        repository.Verify(
            item => item.GetActiveByEmailAsync(
                "employee@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once);
        tokenGenerator.Verify(item => item.GenerateToken(employee), Times.Once);
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
        var service = new AuthenticationService(
            repository.Object,
            Mock.Of<IPasswordHasher>(),
            Mock.Of<IJwtTokenGenerator>());

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
        var service = new AuthenticationService(
            repository.Object,
            passwordHasher.Object,
            Mock.Of<IJwtTokenGenerator>());
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
}
