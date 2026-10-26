using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IAuthenticationRepository
{
    Task<Employee?> FindActiveByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);
}
