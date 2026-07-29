namespace Seamline.Modules.Trading.Internal;

// Pure transport: XML in, acer-stub's ack id out. Domain formatting (which
// TradeHistory row, which RemitAction) is RemitReportingRunner's job, not
// this client's — see ADR-0015.
internal interface IRemitSubmissionClient
{
    Task<string> SubmitAsync(string reportXml, CancellationToken cancellationToken = default);
}
