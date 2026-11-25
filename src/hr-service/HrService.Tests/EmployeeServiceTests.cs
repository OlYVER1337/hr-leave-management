using System.Reflection;
using HrService.Api.Authorization;
using HrService.Api.Controllers;
using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Moq;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace HrService.Tests;

public sealed class EmployeeServiceTests
{
    // Đảm bảo endpoint overview dùng permission policy động thay vì role legacy.
    [Fact]
    public void EmployeeOverviewEndpointUsesExistingDynamicReadPolicy()
    {
        var method = typeof(EmployeesController).GetMethod(nameof(EmployeesController.GetOverview));
        Assert.NotNull(method);

        var authorization = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>();
        Assert.Contains(authorization, attribute =>
            attribute.Policy == PermissionPolicies.EmployeesRead && attribute.Roles is null);
    }

    // Kết quả nhân viên phải là hợp của Self và Team, không vượt phạm vi được cấp.
    [Fact]
    public async Task GetVisibleAsync_CombinesSelfAndTeamScopes()
    {
        var actorId = Guid.NewGuid();
        var employees = new[]
        {
            CreateEmployee(actorId),
            CreateEmployee(Guid.NewGuid(), actorId)
        };
        var repository = new Mock<IEmployeeRepository>();
        repository
            .Setup(item => item.ListVisibleAsync(
                actorId, true, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employees);
        var authorization = new EffectiveAuthorization(
        [
            new EffectivePermission("employees.read", ["Self"]),
            new EffectivePermission("employees.read", ["Team"])
        ]);
        var service = CreateService(repository);

        var result = await service.GetVisibleAsync(actorId, authorization);

        Assert.Equal(2, result.Count);
        repository.Verify(
            item => item.ListVisibleAsync(
                actorId, true, true, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Danh sách phân trang và summary phải cùng tuân theo authorization scope của caller.
    [Fact]
    public async Task GetVisibleOverviewPagedAsync_ReturnsPageAndScopedSummary()
    {
        var actorId = Guid.NewGuid();
        var employee = CreateEmployee(Guid.NewGuid(), actorId);
        var repository = new Mock<IEmployeeRepository>();
        repository
            .Setup(item => item.ListVisiblePageAsync(
                actorId,
                true,
                true,
                false,
                null,
                null,
                null,
                null,
                "fullName",
                false,
                1,
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Items: (IReadOnlyList<EmployeeReadModel>)new[]
                {
                    new EmployeeReadModel(
                        employee.Id,
                        employee.Code,
                        employee.FullName,
                        employee.Email,
                        employee.Role,
                        employee.HireDate,
                        employee.IsActive,
                        employee.DepartmentId,
                        employee.Department!.Name,
                        employee.ManagerId,
                        null)
                },
                TotalCount: 1));
        repository
            .Setup(item => item.GetVisibleSummaryAsync(
                actorId, true, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Total: 4, Active: 3, Managers: 1));
        var authorization = new EffectiveAuthorization(
        [
            new EffectivePermission("employees.read", ["Self"]),
            new EffectivePermission("employees.read", ["Team"])
        ]);
        var service = CreateService(repository);

        var overview = await service.GetVisibleOverviewPagedAsync(
            actorId,
            authorization,
            1,
            10,
            null,
            null,
            null,
            null,
            "fullName",
            "asc");

        var item = Assert.Single(overview.Items);
        Assert.Equal(employee.Id, item.Id);
        Assert.Equal(1, overview.TotalCount);
        Assert.Equal(new EmployeeSummaryResponse(4, 3, 1), overview.Summary);
        repository.Verify(item => item.ListVisiblePageAsync(
            actorId, true, true, false, null, null, null, null,
            "fullName", false, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.GetVisibleSummaryAsync(
            actorId, true, true, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Không có read scope được hỗ trợ thì không được trả dữ liệu nhân viên.
    [Fact]
    public async Task GetVisibleAsync_RejectsAuthorizationWithoutSupportedReadScope()
    {
        var actorId = Guid.NewGuid();
        var repository = new Mock<IEmployeeRepository>();
        var authorization = new EffectiveAuthorization(
            [new EffectivePermission("employees.read", ["Department"])]);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetVisibleAsync(actorId, authorization));

        repository.Verify(
            item => item.ListVisibleAsync(
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // API Team yêu cầu đúng permission employees.read với scope Team.
    [Fact]
    public void GetTeamEndpoint_RequiresEmployeesReadTeamScope()
    {
        var method = typeof(CurrentEmployeeController).GetMethod(nameof(CurrentEmployeeController.GetTeam));
        Assert.NotNull(method);

        var authorization = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>();

        Assert.Contains(authorization, attribute =>
            attribute.Policy == PermissionPolicies.EmployeesReadTeam && attribute.Roles is null);
    }

    // Không cho lookup nhân viên nằm ngoài phạm vi của người gọi.
    [Fact]
    public async Task GetLookupAsync_RejectsUserOutsideEmployeeScope()
    {
        var repository = new Mock<IEmployeeRepository>();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetLookupAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                EffectiveAuthorization.Empty));

        repository.Verify(
            item => item.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Manager chỉ xem direct report và response chỉ chứa các trường lookup cần thiết.
    [Fact]
    public async Task GetLookupAsync_AllowsDirectManagerAndReturnsOnlyLookupFields()
    {
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var employee = CreateEmployee(employeeId, managerId);
        var repository = new Mock<IEmployeeRepository>();
        repository
            .Setup(item => item.IsActiveDirectReportOfManagerAsync(
                managerId,
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository
            .Setup(item => item.GetByIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var service = CreateService(repository);

        var managerAuthorization = new EffectiveAuthorization(
            [new EffectivePermission("employees.read", ["Team"])]);
        var result = await service.GetLookupAsync(employeeId, managerId, managerAuthorization);

        Assert.Equal(employeeId, result.Id);
        Assert.Equal(employee.Code, result.Code);
        Assert.Equal(employee.ManagerId, result.ManagerId);
        Assert.DoesNotContain("Email", typeof(EmployeeLookupResponse).GetProperties().Select(property => property.Name));
    }

    // Direct report đã deactivate không còn thuộc phạm vi Team của Manager.
    [Fact]
    public async Task GetLookupAsync_RejectsInactiveDirectReportForTeamScope()
    {
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var employee = CreateEmployee(employeeId, managerId);
        employee.IsActive = false;

        var repository = new Mock<IEmployeeRepository>();
        repository
            .Setup(item => item.IsActiveDirectReportOfManagerAsync(
                managerId,
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(repository);
        var managerAuthorization = new EffectiveAuthorization(
            [new EffectivePermission("employees.read", ["Team"])]);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetLookupAsync(employeeId, managerId, managerAuthorization));

        repository.Verify(
            item => item.IsActiveDirectReportOfManagerAsync(
                managerId, employeeId, It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            item => item.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Scope All cho phép lookup không cần quan hệ manager trực tiếp.
    [Fact]
    public async Task GetLookupAsync_AllowsAllScopeWithoutManagerRelationship()
    {
        var employeeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var employee = CreateEmployee(employeeId);
        var repository = new Mock<IEmployeeRepository>();
        repository.Setup(item => item.GetByIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var service = CreateService(repository);
        var authorization = new EffectiveAuthorization(
            [new EffectivePermission("employees.read", ["All"])]);

        var result = await service.GetLookupAsync(employeeId, requesterId, authorization);

        Assert.Equal(employeeId, result.Id);
        repository.Verify(
            item => item.IsActiveDirectReportOfManagerAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Danh sách Team phải được giới hạn vào các nhân viên báo cáo trực tiếp cho Manager.
    [Fact]
    public async Task GetTeamAsync_ReturnsEmployeesFromManagersDirectReports()
    {
        var managerId = Guid.NewGuid();
        var directReports = new[]
        {
            CreateEmployee(Guid.NewGuid(), managerId),
            CreateEmployee(Guid.NewGuid(), managerId)
        };
        var repository = new Mock<IEmployeeRepository>();
        repository
            .Setup(item => item.GetDirectReportsAsync(managerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(directReports);
        var service = CreateService(repository);

        var result = await service.GetTeamAsync(managerId);

        Assert.Equal(2, result.Count);
        Assert.All(result, employee => Assert.Equal(managerId, employee.ManagerId));
        repository.Verify(
            item => item.GetDirectReportsAsync(managerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Đổi mật khẩu phải xác minh mật khẩu hiện tại trước khi lưu mật khẩu mới.
    [Fact]
    public async Task ChangePasswordAsync_VerifiesCurrentPasswordHashesNewPasswordAndSaves()
    {
        var employee = CreateEmployee(Guid.NewGuid());
        employee.PasswordHash = "old-hash";
        var repository = new Mock<IEmployeeRepository>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetForUpdateByIdAsync(employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        passwordHasher.Setup(item => item.Verify(employee, "current-password")).Returns(true);
        passwordHasher
            .Setup(item => item.Hash(employee, "new-password-123"))
            .Returns("new-hash");
        var service = CreateService(
            repository,
            passwordHasher: passwordHasher,
            unitOfWork: unitOfWork);

        await service.ChangePasswordAsync(
            employee.Id,
            new ChangeEmployeePasswordRequest("current-password", "new-password-123"));

        Assert.Equal("new-hash", employee.PasswordHash);
        passwordHasher.Verify(item => item.Verify(employee, "current-password"), Times.Once);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Mật khẩu hiện tại sai thì không được thay đổi dữ liệu.
    [Fact]
    public async Task ChangePasswordAsync_RejectsIncorrectCurrentPasswordWithoutSaving()
    {
        var employee = CreateEmployee(Guid.NewGuid());
        var repository = new Mock<IEmployeeRepository>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetForUpdateByIdAsync(employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        passwordHasher.Setup(item => item.Verify(employee, "wrong-password")).Returns(false);
        var service = CreateService(
            repository,
            passwordHasher: passwordHasher,
            unitOfWork: unitOfWork);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => service.ChangePasswordAsync(
                employee.Id,
                new ChangeEmployeePasswordRequest("wrong-password", "new-password-123")));

        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        passwordHasher.Verify(
            item => item.Hash(It.IsAny<Employee>(), It.IsAny<string>()),
            Times.Never);
    }

    // Mật khẩu reset phải tuân theo giới hạn độ dài cấu hình.
    [Theory]
    [InlineData("short")]
    [InlineData("1234567890123456789012345678901")]
    public async Task ResetPasswordAsync_RejectsPasswordsOutsideConfiguredLength(
        string password)
    {
        var repository = new Mock<IEmployeeRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(repository, unitOfWork: unitOfWork);

        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.ResetPasswordAsync(
                Guid.NewGuid(),
                new ResetEmployeePasswordRequest(password)));

        repository.Verify(
            item => item.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Chuẩn hóa thông tin nhân viên và bảo đảm password được xử lý qua password hasher.
    [Fact]
    public async Task CreateAsync_NormalizesEmployeeFieldsAndHashesPassword()
    {
        var department = new Department { Id = 7, Name = "Engineering" };
        var repository = new Mock<IEmployeeRepository>();
        var departmentRepository = new Mock<IDepartmentRepository>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var unitOfWork = new Mock<IUnitOfWork>();
        Employee? addedEmployee = null;
        repository
            .Setup(item => item.ExistsByCodeAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(item => item.ExistsByEmailAsync(
                "person@example.com",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(item => item.Add(It.IsAny<Employee>()))
            .Callback<Employee>(employee => addedEmployee = employee);
        departmentRepository
            .Setup(item => item.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);
        passwordHasher
            .Setup(item => item.Hash(It.IsAny<Employee>(), "valid-password-123"))
            .Returns("password-hash");
        var service = CreateService(repository, departmentRepository, passwordHasher, unitOfWork);

        var result = await service.CreateAsync(new CreateEmployeeRequest(
            " Person Name ",
            " PERSON@EXAMPLE.COM ",
            "valid-password-123",
            "employee",
            new DateOnly(2026, 1, 15),
            7,
            null));

        Assert.NotNull(addedEmployee);
        Assert.Matches("^EMP-[A-F0-9]{16}$", addedEmployee.Code);
        Assert.Equal($"EMP-{addedEmployee.Id:N}"[..20].ToUpperInvariant(), addedEmployee.Code);
        Assert.Equal(addedEmployee.Code, result.Code);
        Assert.Equal("Person Name", addedEmployee.FullName);
        Assert.Equal("person@example.com", addedEmployee.Email);
        Assert.Equal("password-hash", addedEmployee.PasswordHash);
        Assert.Equal(department.Id, result.DepartmentId);
        Assert.Equal(department.Name, result.DepartmentName);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Gọi deactivate nhiều lần cho nhân viên đã inactive không tạo thêm thay đổi.
    [Fact]
    public async Task DeactivateAsync_IsIdempotentForAlreadyInactiveEmployee()
    {
        var employee = CreateEmployee(Guid.NewGuid());
        employee.IsActive = false;
        var repository = new Mock<IEmployeeRepository>();
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetForUpdateByIdAsync(employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var service = CreateService(
            repository,
            unitOfWork: unitOfWork,
            refreshTokenRepository: refreshTokenRepository);

        var result = await service.DeactivateAsync(employee.Id);

        Assert.False(result.IsActive);
        refreshTokenRepository.Verify(
            item => item.RevokeAllAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Khi deactivate phải thu hồi refresh token để tài khoản không tiếp tục refresh phiên.
    [Fact]
    public async Task DeactivateAsync_RevokesAllRefreshTokensWhenDeactivatingEmployee()
    {
        var employee = CreateEmployee(Guid.NewGuid());
        var repository = new Mock<IEmployeeRepository>();
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetForUpdateByIdAsync(employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var service = CreateService(
            repository,
            unitOfWork: unitOfWork,
            refreshTokenRepository: refreshTokenRepository);

        var result = await service.DeactivateAsync(employee.Id);

        Assert.False(result.IsActive);
        refreshTokenRepository.Verify(
            item => item.RevokeAllAsync(
                employee.Id,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Không cho bỏ role Manager khi nhân viên vẫn đang quản lý direct reports.
    [Fact]
    public async Task UpdateAsync_RejectsRemovingManagerRoleWhenEmployeeHasDirectReports()
    {
        var employeeId = Guid.NewGuid();
        var employee = CreateEmployee(employeeId);
        employee.Role = Role.Manager;
        var repository = new Mock<IEmployeeRepository>();
        var departmentRepository = new Mock<IDepartmentRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetForUpdateByIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        repository
            .Setup(item => item.ExistsByCodeAsync(
                "EMP-01",
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(item => item.ExistsByEmailAsync(
                "employee@example.com",
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(item => item.HasDirectReportsAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService(
            repository,
            departmentRepository,
            unitOfWork: unitOfWork);

        await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(
                employeeId,
                new UpdateEmployeeRequest(
                    "EMP-01",
                    "Test Employee",
                    "employee@example.com",
                    "Employee",
                    new DateOnly(2020, 1, 1),
                    employee.DepartmentId,
                    null)));

        departmentRepository.Verify(
            item => item.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Không cho nhân viên tự gán chính mình làm Manager.
    [Fact]
    public async Task UpdateAsync_RejectsSelfAsManagerWithoutSaving()
    {
        var employeeId = Guid.NewGuid();
        var employee = CreateEmployee(employeeId);
        var repository = new Mock<IEmployeeRepository>();
        var departmentRepository = new Mock<IDepartmentRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetForUpdateByIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        repository
            .Setup(item => item.ExistsByCodeAsync(
                employee.Code,
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(item => item.ExistsByEmailAsync(
                employee.Email,
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        departmentRepository
            .Setup(item => item.GetByIdAsync(employee.DepartmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee.Department);
        var service = CreateService(
            repository,
            departmentRepository,
            unitOfWork: unitOfWork);

        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.UpdateAsync(
                employeeId,
                new UpdateEmployeeRequest(
                    employee.Code,
                    employee.FullName,
                    employee.Email,
                    employee.Role.ToString(),
                    employee.HireDate,
                    employee.DepartmentId,
                    employeeId)));

        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static EmployeeService CreateService(
        Mock<IEmployeeRepository> repository,
        Mock<IDepartmentRepository>? departmentRepository = null,
        Mock<IPasswordHasher>? passwordHasher = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IRefreshTokenRepository>? refreshTokenRepository = null)
    {
        refreshTokenRepository ??= new Mock<IRefreshTokenRepository>();
        refreshTokenRepository
            .Setup(item => item.RevokeAllAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new EmployeeService(
            repository.Object,
            (departmentRepository ?? new Mock<IDepartmentRepository>()).Object,
            (passwordHasher ?? new Mock<IPasswordHasher>()).Object,
            refreshTokenRepository.Object,
            TimeProvider.System,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object);
    }

    private static Employee CreateEmployee(Guid id, Guid? managerId = null) =>
        new()
        {
            Id = id,
            Code = "EMP-01",
            FullName = "Test Employee",
            Email = "employee@example.com",
            Role = Role.Employee,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true,
            DepartmentId = 7,
            Department = new Department { Id = 7, Name = "Engineering" },
            ManagerId = managerId
        };
}
