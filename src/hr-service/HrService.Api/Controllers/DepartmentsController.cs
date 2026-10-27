using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
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

    [Authorize(Roles = "HR")]
    [HttpPost]
    public async Task<ActionResult<DepartmentResponse>> Create(
        [FromBody] DepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = department.Id }, department);
    }

    [Authorize(Roles = "HR")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> Update(
        int id,
        [FromBody] DepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await departmentService.UpdateAsync(id, request, cancellationToken);
        return Ok(department);
    }

    [Authorize(Roles = "HR")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await departmentService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
