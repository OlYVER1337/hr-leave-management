using HrService.Api.Dtos;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Route("api/v1/departments")]
public sealed class DepartmentsController(DepartmentService departmentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DepartmentResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var departments = await departmentService.GetAllAsync(cancellationToken);
        return Ok(departments.Select(ToResponse));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.GetByIdAsync(id, cancellationToken);
        return Ok(ToResponse(department));
    }

    [HttpPost]
    public async Task<ActionResult<DepartmentResponse>> Create(
        [FromBody] CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.CreateAsync(request.Name, cancellationToken);
        var response = ToResponse(department);

        return CreatedAtAction(nameof(GetById), new { id = department.Id }, response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> Update(
        int id,
        [FromBody] UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.UpdateAsync(id, request.Name, cancellationToken);
        return Ok(ToResponse(department));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await departmentService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static DepartmentResponse ToResponse(Department department) =>
        new(department.Id, department.Name);
}
