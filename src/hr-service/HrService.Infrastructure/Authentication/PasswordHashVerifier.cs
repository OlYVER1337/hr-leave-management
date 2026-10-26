using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace HrService.Infrastructure.Authentication;

public sealed class PasswordHashVerifier : IPasswordHashVerifier
{
    private readonly PasswordHasher<Employee> _passwordHasher = new();

    public bool Verify(Employee employee, string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(employee, employee.PasswordHash, password);
        return result != PasswordVerificationResult.Failed;
    }
}
