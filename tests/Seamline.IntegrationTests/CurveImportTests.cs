using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.MarketData.Internal;
using Seamline.SharedKernel;
using Xunit;

namespace Seamline.IntegrationTests;

// Exercises CurveImportRunner (ADR-0018) through the same public entry
// point Valuation.Worker calls — MarketDataModuleExtensions.RunCurveImportAsync
// — against a standalone service provider wired the same way the worker's
// own Program.cs is, using the real (deterministic) SyntheticCurveSource so
// these tests don't depend on a real ENTSO-E/EIA API key or network access.
public sealed class CurveImportTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Import_refreshes_the_current_month_point_for_a_tenant_that_already_published_the_commodity()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        // POST /curve-points is MO-only (ADR-0030).
        var marketDataClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);

        var currentPeriod = $"{DateTimeOffset.UtcNow:yyyy-MM}";
        (await marketDataClient.PostAsJsonAsync("/curve-points/", new { commodityCode = "POWER", deliveryPeriod = currentPeriod, price = 1m }))
            .EnsureSuccessStatusCode();

        var provider = BuildCurveImportServiceProvider();
        await provider.RunCurveImportAsync();

        var expectedPrice = await new SyntheticCurveSource().GetMonthlyAveragePriceAsync("POWER", DateOnly.FromDateTime(DateTime.UtcNow));

        var curvePoints = await FetchCurvePointsAsync(client);
        var point = Assert.Single(curvePoints, p => p.CommodityCode == "POWER" && p.DeliveryPeriod == currentPeriod);
        Assert.Equal(expectedPrice, point.Price);
    }

    [Fact]
    public async Task Import_does_not_create_a_point_for_a_commodity_the_tenant_never_published()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);

        // This tenant has never published GAS at all.
        var provider = BuildCurveImportServiceProvider();
        await provider.RunCurveImportAsync();

        var curvePoints = await FetchCurvePointsAsync(client);
        Assert.DoesNotContain(curvePoints, p => p.CommodityCode == "GAS");
    }

    [Fact]
    public async Task Import_leaves_a_past_periods_point_untouched_and_adds_a_separate_current_month_row()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        // POST /curve-points is MO-only (ADR-0030).
        var marketDataClient = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.MiddleOffice);

        (await marketDataClient.PostAsJsonAsync("/curve-points/", new { commodityCode = "POWER", deliveryPeriod = "2020-01", price = 99m }))
            .EnsureSuccessStatusCode();

        var provider = BuildCurveImportServiceProvider();
        await provider.RunCurveImportAsync();

        var curvePoints = await FetchCurvePointsAsync(client);
        var pastPoint = Assert.Single(curvePoints, p => p.DeliveryPeriod == "2020-01");
        Assert.Equal(99m, pastPoint.Price);

        var currentPeriod = $"{DateTimeOffset.UtcNow:yyyy-MM}";
        Assert.Contains(curvePoints, p => p.DeliveryPeriod == currentPeriod && p.CommodityCode == "POWER");
    }

    [Fact]
    public async Task Import_publishes_todays_synthetic_fixing_once_and_never_overwrites_it()
    {
        var tenantId = Guid.NewGuid();
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.FrontOffice);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var provider = BuildCurveImportServiceProvider();
        await provider.RunCurveImportAsync();
        await provider.RunCurveImportAsync();

        var fixings = (await client.GetFromJsonAsync<List<FixingDto>>($"/indices/COAL-IDX/fixings?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"))!;
        if (!PricingCalendar.IsPricingDay(today))
        {
            Assert.Empty(fixings); // indices do not fix at weekends
            return;
        }
        var fixing = Assert.Single(fixings); // the second run left the first one alone
        Assert.Equal(new SyntheticCurveSource().DailyFixing("COAL", today), fixing.Value);
        Assert.Equal("curve-import", fixing.PublishedBy);
    }

    private IServiceProvider BuildCurveImportServiceProvider()
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
        services.AddMarketDataModule(configuration);
        services.AddMarketDataCurveImportSources(configuration);

        return services.BuildServiceProvider();
    }

    private async Task<List<CurvePointDto>> FetchCurvePointsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/curve-points/");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<CurvePointDto>>())!;
    }

    private sealed record FixingDto(DateOnly FixingDate, decimal Value, string PublishedBy);

    private sealed record CurvePointDto(string CommodityCode, string DeliveryPeriod, decimal Price, DateTimeOffset PublishedAt);
}
