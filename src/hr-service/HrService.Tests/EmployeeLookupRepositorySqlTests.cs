using HrService.Domain.Entities;
using HrService.Infrastructure.Persistence;
using HrService.Infrastructure.Persistence.Repositories;
using Xunit;

namespace HrService.Tests;

public sealed class EmployeeLookupRepositorySqlTests : IClassFixture<AuthorizationSeederSqlFixture>
{
    private readonly AuthorizationSeederSqlFixture _fixture;

    public EmployeeLookupRepositorySqlTests(AuthorizationSeederSqlFixture fixture)
    {
        _fixture = fixture;
    }

    // Batch lookup và Team listing phải cùng chỉ trả direct report active theo ManagerId hiện tại.
    [HrSqlFact]
    public async Task TeamLookups_OnlyReturnActiveCurrentDirectReports()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        Guid managerId;
        Guid otherManagerId;
        Guid activeDirectReportId;
        Guid inactiveDirectReportId;
        Guid reassignedDirectReportId;

        await using (var context = _fixture.CreateContext())
        {
            var department = new Department
            {
                Name = $"Employee lookup test {suffix}"
            };
            context.Departments.Add(department);
            await context.SaveChangesAsync();

            var manager = CreateEmployee(
                Guid.NewGuid(), $"M1-{suffix}", $"m1-{suffix}@example.test",
                Role.Manager, department.Id, isActive: true);
            var otherManager = CreateEmployee(
                Guid.NewGuid(), $"M2-{suffix}", $"m2-{suffix}@example.test",
                Role.Manager, department.Id, isActive: true);
            var activeDirectReport = CreateEmployee(
                Guid.NewGuid(), $"A-{suffix}", $"a-{suffix}@example.test",
                Role.Employee, department.Id, manager.Id, isActive: true);
            var inactiveDirectReport = CreateEmployee(
                Guid.NewGuid(), $"I-{suffix}", $"i-{suffix}@example.test",
                Role.Employee, department.Id, manager.Id, isActive: false);
            var reassignedDirectReport = CreateEmployee(
                Guid.NewGuid(), $"R-{suffix}", $"r-{suffix}@example.test",
                Role.Employee, department.Id, otherManager.Id, isActive: true);

            context.Employees.AddRange(
                manager,
                otherManager,
                activeDirectReport,
                inactiveDirectReport,
                reassignedDirectReport);
            await context.SaveChangesAsync();

            managerId = manager.Id;
            otherManagerId = otherManager.Id;
            activeDirectReportId = activeDirectReport.Id;
            inactiveDirectReportId = inactiveDirectReport.Id;
            reassignedDirectReportId = reassignedDirectReport.Id;
        }

        await using (var context = _fixture.CreateContext())
        {
            var repository = new EmployeeRepository(context);
            var requestedIds = new[]
            {
                activeDirectReportId,
                inactiveDirectReportId,
                reassignedDirectReportId
            };

            var teamList = await repository.GetDirectReportsAsync(managerId);
            var newManagerTeam = await repository.GetDirectReportsAsync(otherManagerId);
            var teamPage = await repository.ListDirectReportsPageAsync(
                managerId, search: null, page: 1, pageSize: 10);
            var batchLookup = await repository.ListVisibleByIdsAsync(
                managerId,
                includeSelf: false,
                includeTeam: true,
                includeAll: false,
                employeeIds: requestedIds);

            Assert.Equal(new[] { activeDirectReportId }, teamList.Select(employee => employee.Id));
            Assert.Equal(new[] { reassignedDirectReportId }, newManagerTeam.Select(employee => employee.Id));
            Assert.Equal(new[] { activeDirectReportId }, teamPage.Items.Select(employee => employee.Id));
            Assert.Equal(1, teamPage.TotalCount);
            Assert.Equal(new[] { activeDirectReportId }, batchLookup.Select(employee => employee.Id));

            // Scope All (được HR cấp) vẫn cho phép tra cứu hồ sơ inactive.
            var hrLookup = await repository.ListVisibleByIdsAsync(
                managerId,
                includeSelf: false,
                includeTeam: false,
                includeAll: true,
                employeeIds: [inactiveDirectReportId]);

            var inactiveEmployee = Assert.Single(hrLookup);
            Assert.Equal(inactiveDirectReportId, inactiveEmployee.Id);
            Assert.False(inactiveEmployee.IsActive);
        }
    }

    private static Employee CreateEmployee(
        Guid id,
        string code,
        string email,
        Role role,
        int departmentId,
        Guid? managerId = null,
        bool isActive = true) =>
        new()
        {
            Id = id,
            Code = code,
            FullName = code,
            Email = email,
            PasswordHash = "integration-test-placeholder",
            Role = role,
            HireDate = new DateOnly(2026, 1, 1),
            IsActive = isActive,
            DepartmentId = departmentId,
            ManagerId = managerId
        };
}
