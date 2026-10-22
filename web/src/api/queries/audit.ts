import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api, unwrap } from "../client";

export function useAuditEvents(page: number, pageSize: number, entityId?: string) {
  return useQuery({
    queryKey: ["audit-events", page, pageSize, entityId ?? null],
    queryFn: () =>
      unwrap(
        api.GET("/audit-events", {
          params: { query: { page, pageSize, entityId: entityId || undefined } },
        }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: 10_000,
  });
}
