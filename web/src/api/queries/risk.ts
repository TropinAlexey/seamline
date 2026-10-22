import { useQuery } from "@tanstack/react-query";
import { api, unwrap, type Schemas } from "../client";

export type PositionRef = Schemas["PositionRef"];
export type StressScenarioGroup = Schemas["StressScenarioGroup"];

export const riskKeys = {
  positions: ["positions"] as const,
  valuation: ["valuations", "latest"] as const,
  stress: ["stress-results", "latest"] as const,
  exposure: (counterpartyId: string) => ["exposure", counterpartyId] as const,
};

export function usePositions() {
  return useQuery({
    queryKey: riskKeys.positions,
    queryFn: () => unwrap(api.GET("/positions")),
    refetchInterval: 10_000,
  });
}

export function useLatestValuation() {
  return useQuery({
    queryKey: riskKeys.valuation,
    queryFn: () => unwrap(api.GET("/valuations/latest")),
  });
}

export function useLatestStress() {
  return useQuery({
    queryKey: riskKeys.stress,
    queryFn: () => unwrap(api.GET("/stress-results/latest")),
  });
}

export function useExposure(counterpartyId: string) {
  return useQuery({
    queryKey: riskKeys.exposure(counterpartyId),
    queryFn: () =>
      unwrap(
        api.GET("/counterparties/{id}/exposure", { params: { path: { id: counterpartyId } } }),
      ),
    refetchInterval: 10_000,
  });
}
