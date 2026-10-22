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
