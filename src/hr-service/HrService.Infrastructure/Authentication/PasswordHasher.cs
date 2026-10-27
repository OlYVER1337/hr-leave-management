using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace HrService.Infrastructure.Authentication;

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<Employee> _passwordHasher = new();

    public string Hash(Employee employee, string password)
    {
        return _passwordHasher.HashPassword(employee, password);
    }

    public bool Verify(Employee employee, string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(employee, employee.PasswordHash, password);
        return result != PasswordVerificationResult.Failed;
    }
}
