using HrService.Api.Dtos;
using HrService.Application.Common;
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
        try
        {
            var department = await departmentService.GetByIdAsync(id, cancellationToken);
            return Ok(ToResponse(department));
        }
        catch (AppException exception)
        {
            return ToProblem(exception);
        }
    }

    [HttpPost]
    public async Task<ActionResult<DepartmentResponse>> Create(
        [FromBody] CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var department = await departmentService.CreateAsync(request.Name, cancellationToken);
            var response = ToResponse(department);

            return CreatedAtAction(nameof(GetById), new { id = department.Id }, response);
        }
        catch (AppException exception)
        {
            return ToProblem(exception);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> Update(
        int id,
        [FromBody] UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var department = await departmentService.UpdateAsync(id, request.Name, cancellationToken);
            return Ok(ToResponse(department));
        }
        catch (AppException exception)
        {
            return ToProblem(exception);
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await departmentService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (AppException exception)
        {
            return ToProblem(exception);
        }
    }

    private static DepartmentResponse ToResponse(Department department) =>
        new(department.Id, department.Name);

    private ObjectResult ToProblem(AppException exception)
    {
        var statusCode = int.Parse(exception.StatusCode);

        if (exception is RequestValidationException validationException)
        {
            return new ObjectResult(new ValidationProblemDetails
            {
                Title = exception.Code,
                Status = statusCode,
                Errors = validationException.Errors.ToDictionary(
                    error => error.Key,
                    error => error.Value)
            })
            {
                StatusCode = statusCode
            };
        }

        return new ObjectResult(new ProblemDetails
        {
            Title = exception.Code,
            Detail = exception.Message,
            Status = statusCode
        })
        {
            StatusCode = statusCode
        };
    }
}

public sealed record DepartmentResponse(int Id, string Name);
