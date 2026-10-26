using HrService.Application.Dtos;
using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IJwtTokenGenerator
{
    AuthenticationResponse GenerateToken(Employee employee);
}
