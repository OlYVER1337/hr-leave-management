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
    // Manager chỉ được truy vấn danh sách ID của chính team mình khi có leaves permission scope Team phù hợp.
    [Theory]
    [InlineData("leaves.read")]
    [InlineData("leaves.decide")]
    public async Task GetTeamEmployeeIds_AllowsManagerWithMatchingTeamPermission(string permissionCode)
    {
        var managerId = Guid.NewGuid();
        var expectedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var controller = CreateController(
            managerId,
            managerId,
            expectedIds,
            out var employeeRepository,
            (permissionCode, "Team"));

        var action = await controller.GetTeamEmployeeIds(managerId, CancellationToken.None);

        AssertTeamIdsReturned(action, expectedIds);
        VerifyTeamQuery(employeeRepository, managerId);
    }

    // HR có scope All trên permission leave tương ứng được truy vấn team của Manager khác.
    [Theory]
    [InlineData("leaves.read")]
    [InlineData("leaves.decide")]
    public async Task GetTeamEmployeeIds_AllowsHrWithAllPermissionToQueryAnotherManager(string permissionCode)
    {
        var hrEmployeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var expectedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var controller = CreateController(
            hrEmployeeId,
            managerId,
            expectedIds,
            out var employeeRepository,
            (permissionCode, "All"));

        var action = await controller.GetTeamEmployeeIds(managerId, CancellationToken.None);

        AssertTeamIdsReturned(action, expectedIds);
        VerifyTeamQuery(employeeRepository, managerId);
    }

    // Scope All hợp lệ cũng dùng được khi truy vấn team của chính caller.
    [Fact]
    public async Task GetTeamEmployeeIds_AllowsAllScopeForOwnTeam()
    {
        var managerId = Guid.NewGuid();
        var expectedIds = new[] { Guid.NewGuid() };
        var controller = CreateController(
            managerId,
            managerId,
            expectedIds,
            out var employeeRepository,
            ("leaves.read", "All"));

        var action = await controller.GetTeamEmployeeIds(managerId, CancellationToken.None);

        AssertTeamIdsReturned(action, expectedIds);
        VerifyTeamQuery(employeeRepository, managerId);
    }

    // Team scope không được dùng để xem team của một Manager khác.
    [Fact]
    public async Task GetTeamEmployeeIds_DeniesTeamPermissionForAnotherManager()
    {
        var requesterId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        var controller = CreateController(
            requesterId,
            otherManagerId,
            [],
            out var employeeRepository,
            ("leaves.read", "Team"));

        var action = await controller.GetTeamEmployeeIds(otherManagerId, CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        employeeRepository.VerifyNoOtherCalls();
    }

    // Permission All ở nghiệp vụ khác không được thay thế leaves.read/decide All.
    [Fact]
    public async Task GetTeamEmployeeIds_DeniesAllWhenCallerHasNoLeaveAllPermission()
    {
        var requesterId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        var controller = CreateController(
            requesterId,
            otherManagerId,
            [],
            out var employeeRepository,
            ("employees.read", "All"));

        var action = await controller.GetTeamEmployeeIds(otherManagerId, CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        employeeRepository.VerifyNoOtherCalls();
    }

    // Scope All trên permission không liên quan không được cấp quyền truy vấn team.
    [Fact]
    public async Task GetTeamEmployeeIds_DeniesWhenManagerHasOnlyAllPermissionForDifferentPermission()
    {
        var managerId = Guid.NewGuid();
        var controller = CreateController(
            managerId,
            Guid.NewGuid(),
            [],
            out var employeeRepository,
            ("employees.read", "All"));

        var action = await controller.GetTeamEmployeeIds(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ForbidResult>(action.Result);
        employeeRepository.VerifyNoOtherCalls();
    }

    private static LeaveTeamScopeController CreateController(
        Guid requesterId,
        Guid queriedManagerId,
        IReadOnlyList<Guid> teamEmployeeIds,
        out Mock<IEmployeeRepository> employeeRepository,
        params (string PermissionCode, string Scope)[] grants)
    {
        employeeRepository = new Mock<IEmployeeRepository>();
        employeeRepository
            .Setup(repository => repository.GetDirectReportIdsAsync(
                queriedManagerId,
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

    private static void AssertTeamIdsReturned(
        ActionResult<IReadOnlyList<Guid>> action,
        IReadOnlyList<Guid> expectedIds)
    {
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var actualIds = Assert.IsAssignableFrom<IReadOnlyList<Guid>>(response.Value);
        Assert.Equal(expectedIds, actualIds);
    }

    private static void VerifyTeamQuery(
        Mock<IEmployeeRepository> employeeRepository,
        Guid managerId)
    {
        employeeRepository.Verify(
            repository => repository.GetDirectReportIdsAsync(
                managerId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        employeeRepository.VerifyNoOtherCalls();
    }
}
