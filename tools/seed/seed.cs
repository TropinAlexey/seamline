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
// skipped. Logs in once per user. The login endpoint allows five attempts
// a minute per client, and the seed needs six, so a 429 waits for the next
// window instead of failing.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

var baseUrl = (args.Length > 0 ? args[0] : "http://localhost:8081/api").TrimEnd('/') + "/";
const string password = "Demo-Password-123!";

var tenants = new[]
{
    new TenantSeed(
        "Tenant A", Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Counterparties: [("Northgate Utilities (demo)", 5_000_000m), ("Riverbend Gas (demo)", 2_000_000m), ("Pebble Trading (demo)", 50_000m), ("Bulk Mining Co (demo)", 20_000_000m)]),
    new TenantSeed(
        "Tenant B", Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Counterparties: [("Harbour Energy Partners (demo)", 3_000_000m), ("Kestrel Power (demo)", 1_000_000m)]),
};

foreach (var tenant in tenants)
{
    Console.WriteLine($"== {tenant.Name} ({tenant.Id})");
    var trader = await LoginAsync(tenant.Id, "trader");
    var risk = await LoginAsync(tenant.Id, "risk");
    var backoffice = await LoginAsync(tenant.Id, "backoffice");

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
        // COAL 2027-03 is spec 03's forward, so the index-priced example
        // below shows the spec's numbers.
        [("COAL", "2027-03")] = 106.00m, [("COAL", "2027-04")] = 111.50m,
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

        // Coal cargoes (ADR-0034, spec 01). The worked example, settled end
        // to end by back office: provisional 8,635,000.00, final
        // 8,519,864.05, balance -115,135.95.
        var bulk = counterparties[3].Id;
        var cargo = await CreateCargoAsync(trader, "2027-03", "Buy", 75_000m, 110.00m, bulk, 10m, "2027-03-10", "2027-03-20");
        await SubmitAsync(trader, cargo);
        await SendAsync(backoffice, HttpMethod.Post, $"shipments/{cargo}/nominate", new { vesselName = "MV Demo Carrier", imo = "9876543", eta = "2027-03-12" });
        await SendAsync(backoffice, HttpMethod.Post, $"shipments/{cargo}/load", new { blQuantity = 78_500m, blDate = "2027-03-14" });
        await SendAsync(backoffice, HttpMethod.Post, $"shipments/{cargo}/certify", new { measuredNarKcalKg = 5_920.0m, sulphurPct = 0.85m, ashPct = 14.20m });

        // And one waiting for back office to nominate a vessel.
        var awaiting = await CreateCargoAsync(trader, "2027-04", "Sell", 50_000m, 112.00m, bulk, 5m, "2027-04-05", "2027-04-15");
        await SubmitAsync(trader, awaiting);

        // Index-priced coal (ADR-0035, spec 03): March average of COAL-IDX
        // plus 1.5000, with ten of the 23 pricing days fixed at an average
        // of 104.2000 — expected price 106.7174, MtM -53,804.35. The
        // fixings are append-only, so they are published once, here.
        var march = Enumerable.Range(1, 31).Select(d => new DateOnly(2027, 3, d))
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(10).ToList();
        for (var i = 0; i < march.Count; i++)
            await SendAsync(risk, HttpMethod.Post, "indices/COAL-IDX/fixings", new { fixingDate = march[i], value = i % 2 == 0 ? 104.00m : 104.40m });
        var indexPriced = await PostAsync<IdResult>(trader, "trades", new
        {
            commodityCode = "COAL", deliveryPeriod = "2027-03", direction = "Buy", volume = 75_000m, counterpartyId = bulk,
            cargoTerms = new { tolerancePct = 10m, laycanFrom = "2027-03-22", laycanTo = "2027-03-26" },
            indexPricing = new { indexCode = "COAL-IDX", windowFrom = "2027-03-01", windowTo = "2027-03-31", premium = 1.50m }
        });
        await SubmitAsync(trader, indexPriced.Id);
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
    for (var attempt = 0; response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2; attempt++)
    {
        Console.WriteLine("   login rate limit reached — waiting for the next one-minute window…");
        await Task.Delay(TimeSpan.FromSeconds(61));
        response = await client.PostAsJsonAsync("auth/login", new { tenantId, login, password });
    }
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

async Task<Guid> CreateCargoAsync(
    HttpClient client, string period, string direction, decimal volume, decimal price, Guid counterpartyId,
    decimal tolerancePct, string laycanFrom, string laycanTo)
{
    var created = await PostAsync<IdResult>(client, "trades", new
    {
        commodityCode = "COAL", deliveryPeriod = period, direction, volume, price, counterpartyId,
        cargoTerms = new { tolerancePct, laycanFrom, laycanTo }
    });
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
