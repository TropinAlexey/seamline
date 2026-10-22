#:property PublishAot=false
// Demo data for the web UI (ADR-0030, spec §5.6), seeded through the public
// HTTP API as the demo users — so every trade goes through the real
// lifecycle, credit check, saga and events instead of rows written behind
// the system's back.
//
//   dotnet run tools/seed/seed.cs                          # via the web proxy (compose)
//   dotnet run tools/seed/seed.cs -- http://localhost:5000 # straight at the API
//
// Idempotent enough to re-run: a tenant that already has counterparties is
// skipped. Logs in once per user (the login endpoint is rate limited).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

var baseUrl = (args.Length > 0 ? args[0] : "http://localhost:8081/api").TrimEnd('/') + "/";
const string password = "Demo-Password-123!";

var tenants = new[]
{
    new TenantSeed(
        "Tenant A", Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Counterparties: [("Northgate Utilities (demo)", 5_000_000m), ("Riverbend Gas (demo)", 2_000_000m), ("Pebble Trading (demo)", 50_000m)]),
    new TenantSeed(
        "Tenant B", Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Counterparties: [("Harbour Energy Partners (demo)", 3_000_000m), ("Kestrel Power (demo)", 1_000_000m)]),
};

foreach (var tenant in tenants)
{
    Console.WriteLine($"== {tenant.Name} ({tenant.Id})");
    var trader = await LoginAsync(tenant.Id, "trader");
    var risk = await LoginAsync(tenant.Id, "risk");

    var existing = await trader.GetFromJsonAsync<List<Counterparty>>("counterparties");
    if (existing is { Count: > 0 })
    {
        Console.WriteLine($"   {existing.Count} counterparties already present — skipping.");
        continue;
    }

    var counterparties = new List<Counterparty>();
    foreach (var (name, limit) in tenant.Counterparties)
        counterparties.Add(await PostAsync<Counterparty>(trader, "counterparties", new { name, creditLimit = limit }));
    Console.WriteLine($"   {counterparties.Count} counterparties");

    // Curve points: published by middle office (POST /curve-points is MO-only).
    // Only near months are published. With no curve point, credit exposure
    // falls back to full notional, so a trade in a far month (the demo uses
    // 2031-12) breaches a small limit deterministically.
    var curve = new Dictionary<(string, string), decimal>
    {
        [("POWER", "2027-01")] = 92.40m, [("POWER", "2027-02")] = 88.10m, [("POWER", "2027-03")] = 79.75m,
        [("GAS", "2027-01")] = 36.20m, [("GAS", "2027-02")] = 34.85m, [("GAS", "2027-03")] = 31.10m,
    };
    foreach (var ((commodity, period), price) in curve)
        await SendAsync(risk, HttpMethod.Post, "curve-points", new { commodityCode = commodity, deliveryPeriod = period, price });
    Console.WriteLine($"   {curve.Count} curve points");

    var big = counterparties[0].Id;
    var second = counterparties[1].Id;

    // Active, within limit.
    var active = await CreateAsync(trader, "POWER", "2027-02", "Buy", 200m, 86.50m, big);
    await SubmitAsync(trader, active);

    // Draft, never submitted.
    await CreateAsync(trader, "POWER", "2027-03", "Sell", 120m, 81.00m, second);

    if (tenant.Name == "Tenant A")
    {
        var pebble = counterparties[2].Id;

        // Active, then amended — version history with a diff.
        var amended = await CreateAsync(trader, "GAS", "2027-01", "Sell", 500m, 35.00m, second);
        await SubmitAsync(trader, amended);
        await SendAsync(trader, HttpMethod.Post, $"trades/{amended}/amend", new { volume = 450m, price = 35.25m, reason = "Nomination correction" });

        // Delivered — Settlement issues an invoice for back office.
        var delivered = await CreateAsync(trader, "POWER", "2027-01", "Buy", 50m, 90.00m, big);
        await SubmitAsync(trader, delivered);
        await SendAsync(trader, HttpMethod.Post, $"trades/{delivered}/deliver", body: null);

        // Over Pebble's 50,000 limit, rejected by risk through the saga.
        var rejected = await CreateAsync(trader, "GAS", "2031-12", "Buy", 4_000m, 30.00m, pebble);
        await SubmitAsync(trader, rejected);
        await SendAsync(risk, HttpMethod.Post, $"trades/{rejected}/reject", body: null);
        await WaitForStateAsync(trader, rejected, "Rejected");

        // Cancelled while still a draft.
        var cancelled = await CreateAsync(trader, "POWER", "2027-02", "Sell", 75m, 87.00m, big);
        await SendAsync(trader, HttpMethod.Post, $"trades/{cancelled}/cancel", body: null);
    }
    else
    {
        var second2 = await CreateAsync(trader, "GAS", "2027-02", "Buy", 300m, 34.00m, second);
        await SubmitAsync(trader, second2);
    }

    Console.WriteLine("   trades seeded");
}

Console.WriteLine("Done. No trade is left in CreditPending: the approval saga auto-rejects after 5 minutes,");
Console.WriteLine("so the demo script creates its own limit breach (Pebble Trading, delivery period 2031-12).");

async Task<HttpClient> LoginAsync(Guid tenantId, string login)
{
    var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var response = await client.PostAsJsonAsync("auth/login", new { tenantId, login, password });
    if (response.StatusCode == HttpStatusCode.TooManyRequests)
        throw new InvalidOperationException("Login rate limit hit — wait a minute and re-run.");
    response.EnsureSuccessStatusCode();
    var token = (await response.Content.ReadFromJsonAsync<LoginResult>())!.Token;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return client;
}

async Task<Guid> CreateAsync(HttpClient client, string commodity, string period, string direction, decimal volume, decimal price, Guid counterpartyId)
{
    var created = await PostAsync<IdResult>(client, "trades", new { commodityCode = commodity, deliveryPeriod = period, direction, volume, price, counterpartyId });
    return created.Id;
}

async Task SubmitAsync(HttpClient client, Guid tradeId)
    => await SendAsync(client, HttpMethod.Post, $"trades/{tradeId}/submit", body: null);

async Task<T> PostAsync<T>(HttpClient client, string path, object body)
{
    var response = await SendAsync(client, HttpMethod.Post, path, body);
    return (await response.Content.ReadFromJsonAsync<T>())!;
}

async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
{
    using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
    var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"{method} {path} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    return response;
}

async Task WaitForStateAsync(HttpClient client, Guid tradeId, string state)
{
    for (var attempt = 0; attempt < 60; attempt++)
    {
        var trade = await client.GetFromJsonAsync<TradeState>($"trades/{tradeId}");
        if (trade!.State == state)
            return;
        await Task.Delay(500);
    }
    Console.WriteLine($"   warning: trade {tradeId} did not reach {state} within 30s (saga still retrying?)");
}

record TenantSeed(string Name, Guid Id, (string Name, decimal Limit)[] Counterparties);
record Counterparty(Guid Id, string Name, decimal CreditLimit);
record LoginResult(string Token);
record IdResult(Guid Id);
record TradeState(Guid Id, string State);
