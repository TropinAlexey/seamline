using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Seamline.Modules.Identity.Contracts;
using Xunit;

namespace Seamline.IntegrationTests;

// Commodity master data (ADR-0033, spec 00): middle office defines it,
// everyone reads it, trades can only be booked on a known code, and each
// tenant sees only its own commodities.
public sealed class CommodityTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    private static readonly object CoalSpec = new
    {
        baseNarKcalKg = 6000.0m,
        rejectBelowNarKcalKg = 5700.0m,
        maxSulphurPct = 1.00m,
        maxAshPct = 16.00m
    };

    [Fact]
    public async Task Every_tenant_starts_with_the_flow_commodities_in_MWh()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.BackOffice);

        var commodities = await client.GetFromJsonAsync<List<CommodityDto>>("/commodities");

        Assert.Equal(["GAS", "POWER"], commodities!.Select(c => c.Code));
        Assert.All(commodities!, c =>
        {
            Assert.Equal("MWh", c.Unit);
            Assert.Equal("Flow", c.DeliveryModel);
            Assert.Null(c.QualitySpec);
        });
    }

    [Fact]
    public async Task Middle_office_creates_a_cargo_commodity_with_a_quality_spec()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);

        var response = await client.PostAsJsonAsync("/commodities", new
        {
            code = "COAL",
            name = "Steam coal",
            unit = "Tonne",
            deliveryModel = "Cargo",
            qualitySpec = CoalSpec
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var coal = await response.Content.ReadFromJsonAsync<CommodityDto>();
        Assert.Equal("Tonne", coal!.Unit);
        Assert.Equal(6000.0m, coal.QualitySpec!.BaseNarKcalKg);
        Assert.Equal(1.00m, coal.QualitySpec.MaxSulphurPct);
    }

    [Fact]
    public async Task Front_office_cannot_create_a_commodity()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);

        var response = await client.PostAsJsonAsync("/commodities", new { code = "OIL", name = "Oil", unit = "Tonne", deliveryModel = "Flow" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_code_is_a_409()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);

        var response = await client.PostAsJsonAsync("/commodities", new { code = "GAS", name = "Gas again", unit = "MWh", deliveryModel = "Flow" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("Cargo", false)] // cargo without a spec
    [InlineData("Flow", true)]   // flow with a spec
    public async Task A_quality_spec_must_match_the_delivery_model(string deliveryModel, bool withSpec)
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);

        var response = await client.PostAsJsonAsync("/commodities", new
        {
            code = "X1",
            name = "Mismatch",
            unit = "Tonne",
            deliveryModel,
            qualitySpec = withSpec ? CoalSpec : null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_rejection_floor_above_the_base_calorific_value_is_a_400()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);

        var response = await client.PostAsJsonAsync("/commodities", new
        {
            code = "COAL",
            name = "Steam coal",
            unit = "Tonne",
            deliveryModel = "Cargo",
            qualitySpec = new { baseNarKcalKg = 6000.0m, rejectBelowNarKcalKg = 6100.0m, maxSulphurPct = 1.00m, maxAshPct = 16.00m }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_quality_spec_of_a_cargo_commodity_can_change_but_a_flow_commodity_has_none()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);
        await client.PostAsJsonAsync("/commodities", new { code = "COAL", name = "Steam coal", unit = "Tonne", deliveryModel = "Cargo", qualitySpec = CoalSpec });

        var changed = await client.PutAsJsonAsync("/commodities/COAL/quality-spec",
            new { baseNarKcalKg = 5500.0m, rejectBelowNarKcalKg = 5200.0m, maxSulphurPct = 0.80m, maxAshPct = 15.00m });
        var onFlow = await client.PutAsJsonAsync("/commodities/GAS/quality-spec", CoalSpec);
        var missing = await client.PutAsJsonAsync("/commodities/NOPE/quality-spec", CoalSpec);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(5500.0m, (await changed.Content.ReadFromJsonAsync<CommodityDto>())!.QualitySpec!.BaseNarKcalKg);
        Assert.Equal(HttpStatusCode.Conflict, onFlow.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_trade_on_an_unknown_commodity_is_a_400()
    {
        var client = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.FrontOffice);
        var counterparty = await (await client.PostAsJsonAsync("/counterparties/", new { name = "Acme Energy", creditLimit = 1_000_000m }))
            .Content.ReadFromJsonAsync<IdDto>();

        var response = await client.PostAsJsonAsync("/trades/", new
        {
            commodityCode = "POWR",
            deliveryPeriod = "2027-08",
            direction = "Buy",
            volume = 10m,
            price = 40m,
            counterpartyId = counterparty!.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Commodity POWR not found", (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
    }

    [Fact]
    public async Task A_tenant_never_sees_another_tenants_commodities()
    {
        var tenantA = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);
        var tenantB = await AuthTestHelper.CreateAuthenticatedClientAsync(factory, Guid.NewGuid(), IdentityRoles.MiddleOffice);
        await tenantA.PostAsJsonAsync("/commodities", new { code = "COAL", name = "Steam coal", unit = "Tonne", deliveryModel = "Cargo", qualitySpec = CoalSpec });

        var seenByB = await tenantB.GetFromJsonAsync<List<CommodityDto>>("/commodities");

        Assert.DoesNotContain(seenByB!, c => c.Code == "COAL");
    }

    [Fact]
    public async Task Row_level_security_hides_commodities_from_another_tenants_session()
    {
        var tenantId = Guid.NewGuid();
        await AuthTestHelper.CreateAuthenticatedClientAsync(factory, tenantId, IdentityRoles.BackOffice);

        // The restricted app role with a different app.tenant_id sees none of
        // this tenant's rows — the database layer of ADR-0005, independent of
        // the EF query filter.
        await using var connection = new NpgsqlConnection(factory.AppConnectionString);
        await connection.OpenAsync();
        await using var setOtherTenant = connection.CreateCommand();
        setOtherTenant.CommandText = $"SET app.tenant_id = '{Guid.NewGuid():D}';";
        await setOtherTenant.ExecuteNonQueryAsync();

        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT count(*) FROM reference.commodity WHERE tenant_id = @tenantId";
        count.Parameters.AddWithValue("tenantId", tenantId);

        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    private sealed record CommodityDto(string Code, string Name, string Unit, string DeliveryModel, QualitySpecDto? QualitySpec);

    private sealed record QualitySpecDto(decimal BaseNarKcalKg, decimal RejectBelowNarKcalKg, decimal MaxSulphurPct, decimal MaxAshPct);

    private sealed record IdDto(Guid Id);

    private sealed record ErrorDto(string Error);
}
