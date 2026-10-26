using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IPasswordHashVerifier
{
    bool Verify(Employee employee, string password);
}
