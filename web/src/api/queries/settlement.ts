import { useQuery } from "@tanstack/react-query";
import { api, unwrap } from "../client";

export function useInvoices() {
  return useQuery({
    queryKey: ["invoices"],
    queryFn: () => unwrap(api.GET("/invoices")),
  });
}
