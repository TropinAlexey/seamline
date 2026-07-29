using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Trading.Internal;
using Seamline.SharedKernel;
using Xunit;

namespace Seamline.IntegrationTests;

// Exercises RemitReportingRunner (ADR-0015) through the same public entry
// point Reporting.Worker calls — TradingModuleExtensions.RunReportingBatchAsync
// — against a standalone service provider wired the same way the worker's
// own Program.cs is, but with a fake IRemitSubmissionClient in place of the
// real HTTP client (AddTradingReportingClient) so these tests are
// deterministic instead of depending on acer-stub's actual randomness.
public sealed class RemitReportingTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Activating_a_trade_produces_a_New_report()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-11", "Buy", 100m, 40m, counterparty.Id);
        (await client.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();

        var provider = BuildReportingServiceProvider(new FakeRemitSubmissionClient());
        await provider.RunReportingBatchAsync();

        // Just one report — the Draft/Submitted transitions on the way to
        // Active aren't reportable (ADR-0015), regardless of which
        // trade_history version number Active itself landed on.
        var report = Assert.Single(await FetchReportsAsync(trade.Id));
        Assert.Equal("New", report.Action);
    }

    [Fact]
    public async Task Amending_an_active_trade_produces_a_New_then_a_Modify_report()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2027-12", "Buy", 100m, 40m, counterparty.Id);
        (await client.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/trades/{trade.Id}/amend", new { volume = 150m, price = 46m, reason = "correction" }))
            .EnsureSuccessStatusCode();

        var provider = BuildReportingServiceProvider(new FakeRemitSubmissionClient());
        await provider.RunReportingBatchAsync();

        var reports = await FetchReportsAsync(trade.Id);
        Assert.Equal(2, reports.Count);
        Assert.Equal("New", reports[0].Action);
        Assert.Equal("Modify", reports[1].Action);
    }

    [Fact]
    public async Task A_failed_New_submission_holds_back_the_Modify_until_the_next_run()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        var counterparty = await CreateCounterpartyAsync(client, "Acme Energy", 1_000_000m);
        var trade = await CreateTradeAsync(client, "POWER", "2028-01", "Buy", 100m, 40m, counterparty.Id);
        (await client.PostAsync($"/trades/{trade.Id}/submit", content: null)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/trades/{trade.Id}/amend", new { volume = 150m, price = 46m, reason = "correction" }))
            .EnsureSuccessStatusCode();

        // First run: New fails, so Modify must be held back this run rather
        // than being misclassified as a New of its own.
        var failingProvider = BuildReportingServiceProvider(
            new FakeRemitSubmissionClient(shouldFail: xml => xml.Contains("<Action>New</Action>")));
        await failingProvider.RunReportingBatchAsync();
        Assert.Empty(await FetchReportsAsync(trade.Id));

        // Second run: nothing fails now, both land in the right order.
        var succeedingProvider = BuildReportingServiceProvider(new FakeRemitSubmissionClient());
        await succeedingProvider.RunReportingBatchAsync();

        var reports = await FetchReportsAsync(trade.Id);
        Assert.Equal(2, reports.Count);
        Assert.Equal("New", reports[0].Action);
        Assert.Equal("Modify", reports[1].Action);
    }

    private IServiceProvider BuildReportingServiceProvider(IRemitSubmissionClient submissionClient)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = factory.AppConnectionString,
                ["ConnectionStrings:PostgresMigrator"] = factory.ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantSessionVariableInterceptor>();
        services.AddTradingModule(configuration);
        services.AddSingleton(submissionClient);

        return services.BuildServiceProvider();
    }

    private async Task<List<RemitReportRow>> FetchReportsAsync(Guid tradeId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT version, action FROM trading.remit_report
            WHERE trade_id = @tradeId
            ORDER BY version
            """;
        command.Parameters.AddWithValue("tradeId", tradeId);

        var reports = new List<RemitReportRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            reports.Add(new RemitReportRow(reader.GetInt32(0), reader.GetString(1)));

        return reports;
    }

    private static async Task<CounterpartyDto> CreateCounterpartyAsync(HttpClient client, string name, decimal creditLimit)
    {
        var response = await client.PostAsJsonAsync("/counterparties/", new { name, creditLimit });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CounterpartyDto>())!;
    }

    private static async Task<TradeIdDto> CreateTradeAsync(
        HttpClient client, string commodityCode, string deliveryPeriod, string direction, decimal volume, decimal price, Guid counterpartyId)
    {
        var response = await client.PostAsJsonAsync("/trades/", new
        {
            commodityCode,
            deliveryPeriod,
            direction,
            volume,
            price,
            counterpartyId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TradeIdDto>())!;
    }

    private sealed record CounterpartyDto(Guid Id, string Name, decimal CreditLimit);

    private sealed record TradeIdDto(Guid Id);

    private sealed record RemitReportRow(int Version, string Action);

    private sealed class FakeRemitSubmissionClient(Func<string, bool>? shouldFail = null) : IRemitSubmissionClient
    {
        public Task<string> SubmitAsync(string reportXml, CancellationToken cancellationToken = default)
        {
            if (shouldFail?.Invoke(reportXml) == true)
                throw new HttpRequestException("Simulated acer-stub failure.");

            return Task.FromResult(Guid.NewGuid().ToString());
        }
    }
}
