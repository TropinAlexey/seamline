import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap, type Schemas } from "../client";
import { riskKeys } from "./risk";

const curveKeys = { all: ["curve-points"] as const };

export function useCurvePoints() {
  return useQuery({
    queryKey: curveKeys.all,
    queryFn: () => unwrap(api.GET("/curve-points")),
    staleTime: 60_000,
  });
}

// MO only (ADR-0030). Moving a price moves live MtM, so positions refetch.
export function useUpsertCurvePoint() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: Schemas["SetCurvePointRequest"]) =>
      unwrap(api.POST("/curve-points", { body })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: curveKeys.all });
      await queryClient.invalidateQueries({ queryKey: riskKeys.positions });
    },
  });
}

// Price indices and their daily fixings (ADR-0035). Fixings are append-only:
// a second value for a day is a 409.
export type PriceIndexEntry = Schemas["PriceIndexEntry"];
export type IndexFixingEntry = Schemas["IndexFixingEntry"];

export const indexKeys = {
  all: ["indices"] as const,
  fixings: (code: string) => ["indices", code, "fixings"] as const,
};

export function useIndices() {
  return useQuery({
    queryKey: indexKeys.all,
    queryFn: () => unwrap(api.GET("/indices")),
    staleTime: 5 * 60_000,
  });
}

export function useFixings(code: string | null) {
  return useQuery({
    queryKey: indexKeys.fixings(code ?? ""),
    queryFn: () =>
      unwrap(api.GET("/indices/{code}/fixings", { params: { path: { code: code! }, query: {} } })),
    enabled: !!code,
  });
}

// MO only. A new fixing moves every formula trade on the index, so the
// pricing views refetch.
export function usePublishFixing(code: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: Schemas["PublishFixingRequest"]) =>
      unwrap(api.POST("/indices/{code}/fixings", { params: { path: { code } }, body })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: indexKeys.fixings(code) });
      await queryClient.invalidateQueries({ queryKey: ["trades", "pricing"] });
      await queryClient.invalidateQueries({ queryKey: riskKeys.pricingExposure });
    },
  });
}
