using System.Net.Http.Json;

namespace Seamline.IntegrationTests;

// GET /positions returns { items, totalMtm } (ADR-0031); most tests only
// care about the rows.
internal static class PositionsClient
{
    public static async Task<List<T>> GetPositionsAsync<T>(this HttpClient client)
    {
        var response = await client.GetFromJsonAsync<PositionsEnvelope<T>>("/positions");
        return response!.Items;
    }

    private sealed record PositionsEnvelope<T>(List<T> Items);
}
