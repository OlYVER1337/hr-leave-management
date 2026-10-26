using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace HrService.Infrastructure.Authentication;

public sealed class PasswordHashGenerator : IPasswordHashGenerator
{
    private readonly PasswordHasher<Employee> _passwordHasher = new();

    public string Hash(Employee employee, string password)
    {
        return _passwordHasher.HashPassword(employee, password);
    }
}
