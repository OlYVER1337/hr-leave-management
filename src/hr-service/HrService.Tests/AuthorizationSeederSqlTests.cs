using HrService.Domain.Entities;
using HrService.Infrastructure.Authentication;
using HrService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrService.Tests;

public sealed class AuthorizationSeederSqlTests : IClassFixture<AuthorizationSeederSqlFixture>
{
    private readonly AuthorizationSeederSqlFixture _fixture;

    public AuthorizationSeederSqlTests(AuthorizationSeederSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [HrSqlFact]
    public async Task SeedAsync_CreatesBaselineAndBackfillsLegacyRoles_Idempotently()
    {
        var departmentId = 0;
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var hrId = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            var department = new Department { Name = $"Authorization test {Guid.NewGuid():N}" };
            context.Departments.Add(department);
            await context.SaveChangesAsync();
            departmentId = department.Id;

            context.Employees.AddRange(
                CreateEmployee(employeeId, "AUTH-EMP", "auth-employee@example.test", Role.Employee, departmentId),
                CreateEmployee(managerId, "AUTH-MGR", "auth-manager@example.test", Role.Manager, departmentId),
                CreateEmployee(hrId, "AUTH-HR", "auth-hr@example.test", Role.HR, departmentId));

            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var seeder = new AuthorizationSeeder(
                _fixture.CreateFactory(),
                TimeProvider.System,
                NullLogger<AuthorizationSeeder>.Instance);

            await seeder.SeedAsync();
        }

        int auditorRoleId;
        int employeeRoleId;
        int managerRoleId;

        await using (var context = _fixture.CreateContext())
        {
            Assert.Equal(4, await context.Roles.CountAsync());
            Assert.Equal(20, await context.Permissions.CountAsync());
            Assert.Equal(25, await context.PermissionScopeRules.CountAsync());
            Assert.Equal(31, await context.RolePermissions.CountAsync());
            Assert.Equal(33, await context.RolePermissionScopes.CountAsync());
            Assert.Equal(3, await context.UserRoles.CountAsync());

            auditorRoleId = await context.Roles
                .Where(role => role.Code == "Auditor")
                .Select(role => role.Id)
                .SingleAsync();

            employeeRoleId = await context.Roles
                .Where(role => role.Code == "Employee")
                .Select(role => role.Id)
                .SingleAsync();

            managerRoleId = await context.Roles
                .Where(role => role.Code == "Manager")
                .Select(role => role.Id)
                .SingleAsync();

            var managerDecideScope = await (
                from grant in context.RolePermissions
                join permission in context.Permissions on grant.PermissionId equals permission.Id
                join scope in context.RolePermissionScopes
                    on new { grant.RoleId, grant.PermissionId } equals new { scope.RoleId, scope.PermissionId }
                where grant.RoleId == managerRoleId && permission.Code == "leaves.decide"
                select scope.Scope)
                .SingleAsync();

            Assert.Equal("Team", managerDecideScope);

            var auditorPermissions = await (
                from grant in context.RolePermissions
                join permission in context.Permissions on grant.PermissionId equals permission.Id
                where grant.RoleId == auditorRoleId
                select permission.Code)
                .ToListAsync();

            Assert.Equal(2, auditorPermissions.Count);
            Assert.All(auditorPermissions, code => Assert.EndsWith(".read", code));

            Assert.Equal(
                1,
                await context.UserRoles.CountAsync(
                    assignment => assignment.EmployeeId == employeeId && assignment.RoleId == employeeRoleId));
            Assert.Equal(
                1,
                await context.UserRoles.CountAsync(
                    assignment => assignment.EmployeeId == managerId && assignment.RoleId == managerRoleId));
        }

        // Mô phỏng quản trị viên cấp thêm Auditor cho nhân viên và thu hồi Manager.
        // Chạy seeder lần nữa không được nhân bản dữ liệu hoặc khôi phục Role đã thu hồi.
        await using (var context = _fixture.CreateContext())
        {
            var managerAssignment = await context.UserRoles.SingleAsync(
                assignment => assignment.EmployeeId == managerId && assignment.RoleId == managerRoleId);

            managerAssignment.RevokedAtUtc = DateTime.UtcNow;

            context.UserRoles.Add(new UserRole
            {
                EmployeeId = employeeId,
                RoleId = auditorRoleId,
                GrantedAtUtc = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var seeder = new AuthorizationSeeder(
                _fixture.CreateFactory(),
                TimeProvider.System,
                NullLogger<AuthorizationSeeder>.Instance);

            await seeder.SeedAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            Assert.Equal(4, await context.Roles.CountAsync());
            Assert.Equal(20, await context.Permissions.CountAsync());
            Assert.Equal(25, await context.PermissionScopeRules.CountAsync());
            Assert.Equal(31, await context.RolePermissions.CountAsync());
            Assert.Equal(33, await context.RolePermissionScopes.CountAsync());
            Assert.Equal(4, await context.UserRoles.CountAsync());

            var managerAssignment = await context.UserRoles.SingleAsync(
                assignment => assignment.EmployeeId == managerId && assignment.RoleId == managerRoleId);

            Assert.NotNull(managerAssignment.RevokedAtUtc);

            var employeeRoleIds = await context.UserRoles
                .Where(assignment => assignment.EmployeeId == employeeId && assignment.RevokedAtUtc == null)
                .Select(assignment => assignment.RoleId)
                .ToListAsync();

            Assert.Contains(employeeRoleId, employeeRoleIds);
            Assert.Contains(auditorRoleId, employeeRoleIds);
        }
    }

    private static Employee CreateEmployee(
        Guid id,
        string code,
        string email,
        Role role,
        int departmentId)
    {
        return new Employee
        {
            Id = id,
            Code = code,
            FullName = code,
            Email = email,
            PasswordHash = "integration-test-placeholder",
            Role = role,
            HireDate = new DateOnly(2026, 1, 1),
            IsActive = true,
            DepartmentId = departmentId
        };
    }
}

public sealed class HrSqlFactAttribute : FactAttribute
{
    public HrSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("HR_TEST_SQL_CONNECTION_STRING")))
        {
            Skip = "Đặt HR_TEST_SQL_CONNECTION_STRING để chạy kiểm thử tích hợp SQL Server.";
        }
    }
}

public sealed class AuthorizationSeederSqlFixture : IAsyncLifetime
{
    private readonly string? _masterConnectionString =
        Environment.GetEnvironmentVariable("HR_TEST_SQL_CONNECTION_STRING");

    private bool _databaseCreated;

    public string DatabaseName { get; } = $"HrAuthorization_{Guid.NewGuid():N}";
    public string DatabaseConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_masterConnectionString))
        {
            return;
        }

        var masterConnection = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
            Encrypt = false,
            TrustServerCertificate = true
        };

        var databaseConnection = new SqlConnectionStringBuilder(masterConnection.ConnectionString)
        {
            InitialCatalog = DatabaseName
        };

        DatabaseConnectionString = databaseConnection.ConnectionString;

        await using (var connection = new SqlConnection(masterConnection.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            await command.ExecuteNonQueryAsync();
            _databaseCreated = true;
        }

        try
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
        }
        catch
        {
            await DropDatabaseAsync(masterConnection.ConnectionString);
            throw;
        }
    }

    public HrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HrDbContext>()
            .UseSqlServer(DatabaseConnectionString)
            .Options;

        return new HrDbContext(options);
    }

    public IDbContextFactory<HrDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<HrDbContext>()
            .UseSqlServer(DatabaseConnectionString, sqlServer => sqlServer.EnableRetryOnFailure())
            .Options;

        return new AuthorizationSeederContextFactory(options);
    }

    private sealed class AuthorizationSeederContextFactory(
        DbContextOptions<HrDbContext> options) : IDbContextFactory<HrDbContext>
    {
        public HrDbContext CreateDbContext() => new(options);
    }

    public async Task DisposeAsync()
    {
        if (!_databaseCreated || string.IsNullOrWhiteSpace(_masterConnectionString))
        {
            return;
        }

        var masterConnection = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
            Encrypt = false,
            TrustServerCertificate = true
        };

        await DropDatabaseAsync(masterConnection.ConnectionString);
    }

    private async Task DropDatabaseAsync(string masterConnectionString)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]";
        await command.ExecuteNonQueryAsync();
        _databaseCreated = false;
    }
}
