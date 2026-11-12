using System.Security.Claims;
using System.Text.Json;
using HrService.Api.Controllers;
using HrService.Application.Abstractions;
using HrService.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class LeaveTeamScopeControllerTests
{
    [Theory]
    [InlineData("leaves.read")]
    [InlineData("leaves.decide")]
    public async Task GetTeamEmployeeIds_AllowsMatchingTeamPermission(string permissionCode)
    {
        var managerId = Guid.NewGuid();
        var expectedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var controller = CreateController(
            managerId,
            expectedIds,
            out var employeeRepository,
            (permissionCode, "Team"));

        var action = await controller.GetTeamEmployeeIds(managerId, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(action.Result);
        var actualIds = Assert.IsAssignableFrom<IReadOnlyList<Guid>>(response.Value);
        Assert.Equal(expectedIds, actualIds);
        employeeRepository.Verify(
            repository => repository.GetDirectReportIdsAsync(
                managerId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTeamEmployeeIds_DoesNotRequireEmployeesReadPermission()
    {
        var managerId = Guid.NewGuid();
        var expectedIds = new[] { Guid.NewGuid() };
        var controller = CreateController(
            managerId,
            expectedIds,
            out var employeeRepository,
            ("leaves.read", "Team"));

        var action = await controller.GetTeamEmployeeIds(managerId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(action.Result);
        employeeRepository.Verify(
            repository => repository.GetDirectReportIdsAsync(
                managerId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTeamEmployeeIds_DeniesEmployeesReadOnlyPermission()
    {
        var managerId = Guid.NewGuid();
        var controller = CreateController(
            managerId,
            [],
            out var employeeRepository,
            ("employees.read", "Team"));

        var action = await controller.GetTeamEmployeeIds(managerId, CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        employeeRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetTeamEmployeeIds_DeniesQueryForAnotherManager()
    {
        var requesterId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        var controller = CreateController(
            requesterId,
            [],
            out var employeeRepository,
            ("leaves.read", "Team"));

        var action = await controller.GetTeamEmployeeIds(otherManagerId, CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        employeeRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetTeamEmployeeIds_DoesNotTreatAllAsTeamPermission()
    {
        var managerId = Guid.NewGuid();
        var controller = CreateController(
            managerId,
            [],
            out var employeeRepository,
            ("leaves.read", "All"));

        var action = await controller.GetTeamEmployeeIds(managerId, CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        employeeRepository.VerifyNoOtherCalls();
    }

    private static LeaveTeamScopeController CreateController(
        Guid requesterId,
        IReadOnlyList<Guid> teamEmployeeIds,
        out Mock<IEmployeeRepository> employeeRepository,
        params (string PermissionCode, string Scope)[] grants)
    {
        employeeRepository = new Mock<IEmployeeRepository>();
        employeeRepository
            .Setup(repository => repository.GetDirectReportIdsAsync(
                requesterId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamEmployeeIds);

        var service = new EmployeeService(
            employeeRepository.Object,
            new Mock<IDepartmentRepository>().Object,
            new Mock<IPasswordHasher>().Object,
            new Mock<IRefreshTokenRepository>().Object,
            TimeProvider.System,
            new Mock<IUnitOfWork>().Object);

        var permissions = JsonSerializer.Serialize(grants.Select(grant => new
        {
            p = grant.PermissionCode,
            s = new[] { grant.Scope }
        }));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, requesterId.ToString()),
            new Claim("authz_v", "1"),
            new Claim("perm", permissions)
        ], "test"));

        var controller = new LeaveTeamScopeController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return controller;
    }
}
