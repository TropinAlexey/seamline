using System.Xml.Linq;

namespace Seamline.Modules.Trading.Internal;

// Simplified REMIT trade report — not a compliant XSD, stated as such in
// README/CLAUDE.md's clean-room section (public REMIT/ACER specs only, no
// real regulatory schema). Carries exactly what TradeHistory already has.
internal static class RemitReportXml
{
    public static string Build(TradeHistory history, RemitAction action)
    {
        var report = new XElement("RemitReport",
            new XElement("TradeId", history.TradeId),
            new XElement("Version", history.Version),
            new XElement("Action", action),
            new XElement("CommodityCode", history.CommodityCode),
            new XElement("DeliveryPeriod", history.DeliveryPeriod),
            new XElement("Direction", history.Direction),
            new XElement("Volume", history.Volume),
            new XElement("Price", history.Price),
            new XElement("CounterpartyId", history.CounterpartyId),
            new XElement("ValidFrom", history.ValidFrom.ToString("O")));

        return report.ToString(SaveOptions.DisableFormatting);
    }
}
