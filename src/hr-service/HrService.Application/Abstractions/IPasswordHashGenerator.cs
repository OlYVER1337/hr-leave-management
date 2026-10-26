using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IPasswordHashGenerator
{
    string Hash(Employee employee, string password);
}
