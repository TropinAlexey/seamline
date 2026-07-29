using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Seamline.Modules.Trading.Internal;

// Retry/timeout/circuit-breaker against acer-stub's flakiness live in the
// AddStandardResilienceHandler() pipeline this HttpClient is registered
// with (see AddTradingReportingClient) — this class just does one call and
// throws if the final response, after resilience has already retried
// transient failures, still isn't a success. See ADR-0015.
internal sealed class RemitSubmissionClient(HttpClient httpClient) : IRemitSubmissionClient
{
    // acer-stub is a Minimal API — its JSON comes out camelCase under
    // ASP.NET Core's own web defaults. Web defaults on the read side too,
    // so "ackId" matches the AckId property without a case-sensitivity trap.
    private static readonly JsonSerializerOptions ResponseOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> SubmitAsync(string reportXml, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(reportXml, Encoding.UTF8, "application/xml");
        using var response = await httpClient.PostAsync("/reports", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AcerStubResponse>(ResponseOptions, cancellationToken);
        return body!.AckId;
    }

    // "duplicate" and "accepted" are both terminal success from our side
    // (ADR-0015) — the status field only matters for logging, not control
    // flow; either way an AckId comes back and gets recorded.
    private sealed record AcerStubResponse(string Status, string AckId);
}
