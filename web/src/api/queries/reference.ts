import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo } from "react";
import { api, unwrap, type Schemas } from "../client";

export type CounterpartyRef = Schemas["CounterpartyRef"];

const counterpartyKeys = { all: ["counterparties"] as const };

export function useCounterparties() {
  return useQuery({
    queryKey: counterpartyKeys.all,
    queryFn: () => unwrap(api.GET("/counterparties")),
    staleTime: 30_000,
  });
}

// Client-side join (spec §5.2): Trading and Settlement return counterparty
// ids only — the name lives in the Reference schema.
export function useCounterpartyNames(): (id: string) => string {
  const { data } = useCounterparties();
  return useMemo(() => {
    const names = new Map((data ?? []).map((c) => [c.id, c.name]));
    return (id: string) => names.get(id) ?? id.slice(0, 8);
  }, [data]);
}

export function useCreateCounterparty() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: Schemas["CreateCounterpartyRequest"]) =>
      unwrap(api.POST("/counterparties", { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: counterpartyKeys.all }),
  });
}

export type CommodityRef = Schemas["CommodityRef"];
export type UnitOfMeasure = Schemas["UnitOfMeasure"];

// Commodity master data changes rarely (ADR-0033): one fetch per session
// is enough for the trade form and unit labels.
export function useCommodities() {
  return useQuery({
    queryKey: ["commodities"],
    queryFn: () => unwrap(api.GET("/commodities")),
    staleTime: 5 * 60_000,
  });
}

export const unitLabels: Record<UnitOfMeasure, string> = { MWh: "MWh", Tonne: "t" };

// Unit label for a commodity code, or null while loading or for an unknown
// code — callers then show the bare number.
export function useUnitOf(): (code: string) => string | null {
  const { data } = useCommodities();
  return useMemo(() => {
    const units = new Map((data ?? []).map((c) => [c.code, unitLabels[c.unit]]));
    return (code: string) => units.get(code) ?? null;
  }, [data]);
}
