using System.Text.Json;
using HrService.Application.Abstractions;
using HrService.Application.Common;

namespace HrService.Infrastructure.Clients;

public sealed class LeavePolicyUsageClient(HttpClient httpClient) : ILeavePolicyUsageClient
{
    public async Task<int> CountReferencesAsync(
        int policyVersionId,
        string authorization,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/v1/leave-policy-usage/{policyVersionId}");
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw Unavailable($"Leave Service returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.GetProperty("referencedAllocations").GetInt32();
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable($"Leave Service is unreachable: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable("Leave Service did not respond in time.");
        }
        catch (JsonException)
        {
            throw Unavailable("Leave Service returned an unexpected response.");
        }
        catch (KeyNotFoundException)
        {
            throw Unavailable("Leave Service response is missing referencedAllocations.");
        }
    }

    private static LeavePolicyUsageUnavailableException Unavailable(string message) =>
        new(message);
}
