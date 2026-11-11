using HrService.Api.Controllers;
using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace HrService.Tests;

public sealed class PermissionMigrationTests
{
    [Theory]
    [InlineData(nameof(EmployeesController), nameof(EmployeesController.Create), "employees.create")]
    [InlineData(nameof(EmployeesController), nameof(EmployeesController.Update), "employees.update")]
    [InlineData(nameof(EmployeesController), nameof(EmployeesController.Deactivate), "employees.deactivate")]
    [InlineData(nameof(EmployeesController), nameof(EmployeesController.ResetPassword), "employees.password.reset")]
    [InlineData(nameof(DepartmentsController), nameof(DepartmentsController.Create), "departments.create")]
    [InlineData(nameof(DepartmentsController), nameof(DepartmentsController.Update), "departments.update")]
    [InlineData(nameof(DepartmentsController), nameof(DepartmentsController.Delete), "departments.delete")]
    [InlineData(nameof(HolidaysController), nameof(HolidaysController.Create), "holidays.create")]
    [InlineData(nameof(HolidaysController), nameof(HolidaysController.Update), "holidays.update")]
    [InlineData(nameof(HolidaysController), nameof(HolidaysController.Delete), "holidays.delete")]
    [InlineData(nameof(LeavePoliciesController), nameof(LeavePoliciesController.Create), "leave-policies.create")]
    [InlineData(nameof(LeavePoliciesController), nameof(LeavePoliciesController.Update), "leave-policies.update")]
    [InlineData(nameof(LeaveTypesController), nameof(LeaveTypesController.Create), "leave-types.create")]
    [InlineData(nameof(LeaveTypesController), nameof(LeaveTypesController.Update), "leave-types.update")]
    public void HrWriteEndpoint_UsesPermissionPolicyWithoutLegacyRole(
        string controllerName,
        string actionName,
        string expectedPermission)
    {
        var controllerType = new[]
        {
            typeof(EmployeesController),
            typeof(DepartmentsController),
            typeof(HolidaysController),
            typeof(LeavePoliciesController),
            typeof(LeaveTypesController)
        }.Single(type => type.Name == controllerName);
        var method = controllerType.GetMethod(actionName);
        Assert.NotNull(method);

        var authorization = method!.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();
        Assert.Contains(authorization, attribute => attribute.Policy == $"permission:{expectedPermission}");
        Assert.DoesNotContain(authorization, attribute => !string.IsNullOrWhiteSpace(attribute.Roles));
    }

    [Fact]
    public void EmployeeLookupService_UsesEffectiveAuthorizationInsteadOfHrBoolean()
    {
        var method = typeof(EmployeeService).GetMethod(nameof(EmployeeService.GetLookupAsync));
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(bool));
        Assert.Contains(parameters, parameter => parameter.ParameterType == typeof(EffectiveAuthorization));
    }

    [Fact]
    public void EmployeeLookupEndpoints_UseEmployeeReadPermissionPolicies()
    {
        var lookup = typeof(EmployeeLookupController);
        AssertPolicy(lookup, nameof(EmployeeLookupController.GetById), "permission:employees.read");
        AssertPolicy(lookup, nameof(EmployeeLookupController.GetTeam), "permission:employees.read:team-or-all");
    }

    [Fact]
    public void HrControllers_DoNotDeclareLegacyHrRoleAuthorization()
    {
        var controllerTypes = new[]
        {
            typeof(EmployeesController),
            typeof(DepartmentsController),
            typeof(HolidaysController),
            typeof(LeavePoliciesController),
            typeof(LeaveTypesController),
            typeof(EmployeeLookupController)
        };

        foreach (var controllerType in controllerTypes)
        {
            var attributes = controllerType
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(controllerType.GetMethods()
                    .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)));
            Assert.DoesNotContain(attributes, attribute => attribute.Roles?.Split(',')
                .Any(role => role.Trim().Equals("HR", StringComparison.OrdinalIgnoreCase)) == true);
        }
    }

    private static void AssertPolicy(Type controller, string action, string expectedPolicy)
    {
        var method = controller.GetMethod(action);
        Assert.NotNull(method);
        Assert.Contains(
            method!.GetCustomAttributes<AuthorizeAttribute>(inherit: true),
            attribute => attribute.Policy == expectedPolicy && attribute.Roles is null);
    }
}
