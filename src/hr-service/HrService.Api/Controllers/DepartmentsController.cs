using System.Collections;
using HrService.Api.Dtos;
using HrService.Domain.Entities;
using HrService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrService.Api.Controllers;

[ApiController]
[Route("api/v1/departments")]
public class DepartmentsControllers:ControllerBase
{
    private readonly HrDbContext _dbContext;

    public DepartmentsControllers(HrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

// GET api/v1/departments
[HttpGet]
    public async Task<ActionResult<IEnumerable<DepartmentResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var departments = await _dbContext.Departments.AsNoTracking()
        .OrderBy(d=> d.Name)
        .Select(d => new DepartmentResponse(d.Id, d.Name))
        .ToListAsync(cancellationToken);
        return Ok(departments);
    }

//GET api/v1/departments/{id}
[HttpGet("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> GetById(int Id, CancellationToken cancellationToken)
    { 
        var department = await _dbContext.Departments
        .AsNoTracking()
        .Where(d => Id == d.Id)
        .Select(d => new DepartmentResponse( d.Id,d.Name))
        .FirstOrDefaultAsync(cancellationToken);


        return Ok(department);
    }
// POST api/v1/departments/{body}
[HttpPost]
    public async Task<ActionResult<DepartmentResponse>> Create([FromBody] CreateDepartmentRequest request , CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        var exists = await _dbContext.Departments
        .AsNoTracking()
        .AnyAsync(d=> d.Name == name,cancellationToken );

        if (exists == true)
        {
            return Conflict( new ProblemDetails
            {
                Title = " Department already exist",
                Detail = $"Department `{name}` already exist",
                Status = StatusCodes.Status409Conflict
            });
        }
        var department = new Department
        {
            Name = name
        };
        _dbContext.Departments.Add(department);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new DepartmentResponse(
            department.Id
            ,department.Name
        );

        return CreatedAtAction(nameof(GetById), new { id = department.Id},response);
    }
// PUT api/v1/departments/{id}
[HttpPut("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> Update(int id,[FromBody] UpdateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var department = await _dbContext.Departments.FirstAsync(d => d.Id == id);
        if (department is null)
        {
            return NotFound();
        }
        var name = request.Name.Trim();

        var dulicate = await _dbContext.Departments.AnyAsync(d=> d.Id != id && d.Name == name);
        if (dulicate)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Department already exist",
                Detail = $"Department `{name}`  already exist",
                Status = StatusCodes.Status409Conflict
                
            });
        }
        department.Name = name;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new DepartmentResponse(department.Id, department.Name));
    }

}

    
public record DepartmentResponse(int Id, string Name);