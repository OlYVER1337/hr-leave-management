using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(Employee employee, string password);
    bool Verify(Employee employee, string password);
}
