import { useQuery } from "@tanstack/react-query";
import { api, unwrap, type Schemas } from "../client";

export type InvoiceEntry = Schemas["InvoiceEntry"];

// Invoices for one trade (a cargo has a provisional and a final, ADR-0034)
// or for the whole tenant. Settlement issues each one asynchronously after
// the event that triggers it, so a cargo view polls until the invoice it
// expects is there.
export function useInvoices(tradeId?: string, options: { pollUntil?: InvoiceEntry["kind"] } = {}) {
  return useQuery({
    queryKey: ["invoices", tradeId ?? "all"],
    queryFn: () => unwrap(api.GET("/invoices", { params: { query: { tradeId } } })),
    refetchInterval: (query) =>
      options.pollUntil && !query.state.data?.some((i) => i.kind === options.pollUntil)
        ? 1_000
        : false,
  });
}
