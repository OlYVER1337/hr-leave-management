using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class EmployeeServiceTests
{
    [Fact]
    public async Task GetLookupAsync_RejectsUserOutsideEmployeeScope()
    {
        var repository = new Mock<IEmployeeRepository>();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetLookupAsync(Guid.NewGuid(), Guid.NewGuid(), requesterIsHr: false));

        repository.Verify(
            item => item.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetLookupAsync_AllowsDirectManagerAndReturnsOnlyLookupFields()
    {
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var employee = CreateEmployee(employeeId, managerId);
        var repository = new Mock<IEmployeeRepository>();
        repository
            .Setup(item => item.IsDirectManagerOfAsync(
                managerId,
                employeeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository
            .Setup(item => item.GetByIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var service = CreateService(repository);

        var result = await service.GetLookupAsync(employeeId, managerId, requesterIsHr: false);

        Assert.Equal(employeeId, result.Id);
        Assert.Equal(employee.Code, result.Code);
        Assert.Equal(employee.ManagerId, result.ManagerId);
        Assert.DoesNotContain("Email", typeof(EmployeeLookupResponse).GetProperties().Select(property => property.Name));
    }

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
                "EMP-42",
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
            " EMP-42 ",
            " Person Name ",
            " PERSON@EXAMPLE.COM ",
            "valid-password-123",
            "employee",
            new DateOnly(2026, 1, 15),
            7,
            null));

        Assert.NotNull(addedEmployee);
        Assert.Equal("EMP-42", addedEmployee.Code);
        Assert.Equal("Person Name", addedEmployee.FullName);
        Assert.Equal("person@example.com", addedEmployee.Email);
        Assert.Equal("password-hash", addedEmployee.PasswordHash);
        Assert.Equal(department.Id, result.DepartmentId);
        Assert.Equal(department.Name, result.DepartmentName);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_IsIdempotentForAlreadyInactiveEmployee()
    {
        var employee = CreateEmployee(Guid.NewGuid());
        employee.IsActive = false;
        var repository = new Mock<IEmployeeRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetForUpdateByIdAsync(employee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var service = CreateService(repository, unitOfWork: unitOfWork);

        var result = await service.DeactivateAsync(employee.Id);

        Assert.False(result.IsActive);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

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
        Mock<IUnitOfWork>? unitOfWork = null) =>
        new(
            repository.Object,
            (departmentRepository ?? new Mock<IDepartmentRepository>()).Object,
            (passwordHasher ?? new Mock<IPasswordHasher>()).Object,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object);

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
