import { useQuery } from "@tanstack/react-query";
import { api, unwrap, type Schemas } from "../client";

export type PositionRef = Schemas["PositionRef"];
export type StressScenarioGroup = Schemas["StressScenarioGroup"];
export type PricingExposureEntry = Schemas["PricingExposureEntry"];

export const riskKeys = {
  positions: ["positions"] as const,
  valuation: ["valuations", "latest"] as const,
  stress: ["stress-results", "latest"] as const,
  exposure: (counterpartyId: string) => ["exposure", counterpartyId] as const,
  pricingExposure: ["pricing-exposure"] as const,
};

// Index-priced trades (ADR-0035): valued per trade, not in /positions.
// FO and MO only; back office gets 403, so it never asks.
export function usePricingExposure(enabled: boolean) {
  return useQuery({
    queryKey: riskKeys.pricingExposure,
    queryFn: () => unwrap(api.GET("/pricing-exposure")),
    refetchInterval: 10_000,
    enabled,
  });
}

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
