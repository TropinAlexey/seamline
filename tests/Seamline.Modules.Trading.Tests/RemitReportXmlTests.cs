using System.Xml.Linq;
using Seamline.Modules.Trading.Contracts;
using Seamline.Modules.Trading.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Tests;

public class RemitReportXmlTests
{
    [Fact]
    public void Build_carries_every_field_from_the_history_row()
    {
        var trade = Trade.CreateDraft(TenantId.New(), "POWER", "2027-03", TradeDirection.Buy, 100m, 45.5m, Guid.NewGuid());
        var history = TradeHistory.CreateSnapshot(trade, "trader", "Booked");

        var xml = XDocument.Parse(RemitReportXml.Build(history, RemitAction.New));
        var root = xml.Root!;

        Assert.Equal("RemitReport", root.Name.LocalName);
        Assert.Equal(history.TradeId.ToString(), root.Element("TradeId")!.Value);
        Assert.Equal(history.Version.ToString(), root.Element("Version")!.Value);
        Assert.Equal("New", root.Element("Action")!.Value);
        Assert.Equal("POWER", root.Element("CommodityCode")!.Value);
        Assert.Equal("2027-03", root.Element("DeliveryPeriod")!.Value);
        Assert.Equal("Buy", root.Element("Direction")!.Value);
        Assert.Equal("100", root.Element("Volume")!.Value);
        Assert.Equal("45.5", root.Element("Price")!.Value);
        Assert.Equal(history.CounterpartyId.ToString(), root.Element("CounterpartyId")!.Value);
    }

    [Theory]
    [InlineData("New", "New")]
    [InlineData("Modify", "Modify")]
    [InlineData("Terminate", "Terminate")]
    public void Build_writes_the_action_by_name(string actionName, string expected)
    {
        var action = Enum.Parse<RemitAction>(actionName);
        var trade = Trade.CreateDraft(TenantId.New(), "GAS", "2027-04", TradeDirection.Sell, 50m, 20m, Guid.NewGuid());
        var history = TradeHistory.CreateSnapshot(trade, "trader", "Booked");

        var xml = XDocument.Parse(RemitReportXml.Build(history, action));

        Assert.Equal(expected, xml.Root!.Element("Action")!.Value);
    }
}
