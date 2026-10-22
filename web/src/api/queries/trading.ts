import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap, type Schemas } from "../client";

export type TradeSummary = Schemas["TradeSummary"];
export type TradeState = Schemas["TradeState"];
export type TradeDirection = Schemas["TradeDirection"];
export type TradeHistoryEntry = Schemas["TradeHistoryEntry"];
export type RemitReportEntry = Schemas["RemitReportEntry"];
export type CreditReservationOutcome = Schemas["CreditReservationOutcome"];

export const tradeStates: readonly TradeState[] = [
  "Draft",
  "Submitted",
  "CreditPending",
  "Active",
  "Rejected",
  "Cancelled",
  "Delivered",
];

export interface TradeFilters {
  state?: TradeState[];
  commodity?: string;
  period?: string;
  page: number;
  pageSize: number;
}

export const tradeKeys = {
  all: ["trades"] as const,
  list: (filters: TradeFilters) => ["trades", "list", filters] as const,
  detail: (id: string) => ["trades", "detail", id] as const,
  history: (id: string) => ["trades", "history", id] as const,
  remit: (id: string) => ["trades", "remit", id] as const,
};

export function useTrades(filters: TradeFilters, options: { refetchInterval?: number } = {}) {
  return useQuery({
    queryKey: tradeKeys.list(filters),
    queryFn: () =>
      unwrap(
        api.GET("/trades", {
          params: {
            query: {
              state: filters.state?.length ? filters.state : undefined,
              commodity: filters.commodity || undefined,
              period: filters.period || undefined,
              page: filters.page,
              pageSize: filters.pageSize,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: options.refetchInterval,
    refetchIntervalInBackground: false,
  });
}

export function useTrade(id: string, options: { refetchInterval?: number | false } = {}) {
  return useQuery({
    queryKey: tradeKeys.detail(id),
    queryFn: () => unwrap(api.GET("/trades/{id}", { params: { path: { id } } })),
    refetchInterval: options.refetchInterval,
  });
}

export function useTradeHistory(id: string) {
  return useQuery({
    queryKey: tradeKeys.history(id),
    queryFn: () => unwrap(api.GET("/trades/{id}/history", { params: { path: { id } } })),
  });
}

export function useRemitReports(id: string) {
  return useQuery({
    queryKey: tradeKeys.remit(id),
    queryFn: () => unwrap(api.GET("/trades/{id}/remit-reports", { params: { path: { id } } })),
  });
}

export type CreateTradeBody = Schemas["CreateTradeRequest"];
export type AmendTradeBody = Schemas["AmendTradeRequest"];

export function useCreateTrade() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateTradeBody) => unwrap(api.POST("/trades", { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: tradeKeys.all }),
  });
}

export type TradeAction = "submit" | "approve" | "reject" | "cancel" | "deliver";

// Every action endpoint shares the /trades/{id}/<action> shape. The result
// says whether the server finished the transition inline (200) or handed
// it to the saga (202) — the UI only polls in the second case.
export async function runTradeAction(id: string, action: TradeAction) {
  const params = { params: { path: { id } } };
  const request = (() => {
    switch (action) {
      case "submit":
        return api.POST("/trades/{id}/submit", params);
      case "approve":
        return api.POST("/trades/{id}/approve", params);
      case "reject":
        return api.POST("/trades/{id}/reject", params);
      case "cancel":
        return api.POST("/trades/{id}/cancel", params);
      case "deliver":
        return api.POST("/trades/{id}/deliver", params);
    }
  })();
  const response = (await request).response;
  const data = await unwrap(request);
  return { accepted: response.status === 202, data };
}

export function useTradeAction(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (action: TradeAction) => runTradeAction(id, action),
    onSettled: () => queryClient.invalidateQueries({ queryKey: tradeKeys.all }),
  });
}

export function useAmendTrade(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AmendTradeBody) =>
      unwrap(api.POST("/trades/{id}/amend", { params: { path: { id } }, body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: tradeKeys.all }),
  });
}
