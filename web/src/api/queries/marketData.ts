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
