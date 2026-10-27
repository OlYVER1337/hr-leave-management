using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LeaveService.Application.Abstractions;
using LeaveService.Application.Common;
using LeaveService.Application.Dtos;

namespace LeaveService.Infrastructure.Clients;

public sealed class HrServiceClient(HttpClient httpClient) : ILeaveHrClient
{
    public Task<HrEmployeeSnapshot> GetEmployeeAsync(
        Guid employeeId,
        string authorization,
        CancellationToken cancellationToken = default) =>
        GetRequiredAsync<HrEmployeeSnapshot>(
            $"/api/v1/lookup/employees/{employeeId}",
            authorization,
            "employee",
            cancellationToken);

    public Task<HrLeaveType> GetLeaveTypeAsync(
        int leaveTypeId,
        string authorization,
        CancellationToken cancellationToken = default) =>
        GetRequiredAsync<HrLeaveType>(
            $"/api/v1/leave-types/{leaveTypeId}",
            authorization,
            "leave type",
            cancellationToken);

    public async Task<IReadOnlyList<HrLeavePolicy>> GetLeavePoliciesAsync(
        int year,
        string authorization,
        CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<HrLeavePolicy[]>(
            $"/api/v1/leave-policies?year={year}",
            authorization,
            "leave policies",
            cancellationToken);

    public async Task<IReadOnlyList<HrHoliday>> GetHolidaysAsync(
        DateOnly from,
        DateOnly to,
        string authorization,
        CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<HrHoliday[]>(
            $"/api/v1/holidays?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
            authorization,
            "holidays",
            cancellationToken);

    private async Task<T> GetRequiredAsync<T>(
        string path,
        string authorization,
        string resource,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new LeaveNotFoundException($"The requested {resource} was not found.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new LeaveUnauthorizedException("The HR service rejected the access token.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new LeaveForbiddenException("The HR service denied access to the requested data.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException(
                    "HR service",
                    new HttpRequestException($"HR service returned HTTP {(int)response.StatusCode}."));
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException($"HR service returned an empty {resource} response.");
        }
        catch (HttpRequestException exception)
        {
            throw new DependencyUnavailableException("HR service", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("HR service", exception);
        }
    }
}
