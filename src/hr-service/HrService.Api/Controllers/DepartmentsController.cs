using HrService.Api.Dtos;
using HrService.Application.Dtos;
using HrService.Application.Services;
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
        return Ok(departments);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.GetByIdAsync(id, cancellationToken);
        return Ok(department);
    }

    [HttpPost]
    public async Task<ActionResult<DepartmentResponse>> Create(
        [FromBody] DepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.CreateAsync(request.Name, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = department.Id }, department);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> Update(
        int id,
        [FromBody] DepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.UpdateAsync(id, request.Name, cancellationToken);
        return Ok(department);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await departmentService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
