using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class DepartmentServiceTests
{
    [Fact]
    public async Task CreateAsync_TrimsNameAndPersistsDepartment()
    {
        var repository = new Mock<IDepartmentRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        Department? addedDepartment = null;
        repository
            .Setup(item => item.ExistsByNameAsync(
                "People Operations",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(item => item.Add(It.IsAny<Department>()))
            .Callback<Department>(department => addedDepartment = department);
        unitOfWork
            .Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = new DepartmentService(repository.Object, unitOfWork.Object);

        var result = await service.CreateAsync(new DepartmentRequest(" People Operations "));

        Assert.NotNull(addedDepartment);
        Assert.Equal("People Operations", addedDepartment.Name);
        Assert.Equal("People Operations", result.Name);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateNameWithoutSaving()
    {
        var repository = new Mock<IDepartmentRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.ExistsByNameAsync(
                "Engineering",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new DepartmentService(repository.Object, unitOfWork.Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync(new DepartmentRequest("Engineering")));

        repository.Verify(item => item.Add(It.IsAny<Department>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_RejectsDepartmentWithEmployees()
    {
        var department = new Department { Id = 12, Name = "Engineering" };
        var repository = new Mock<IDepartmentRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository
            .Setup(item => item.GetByIdAsync(12, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);
        repository
            .Setup(item => item.HasEmployeesAsync(12, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new DepartmentService(repository.Object, unitOfWork.Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => service.DeleteAsync(12));

        repository.Verify(item => item.Remove(It.IsAny<Department>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
