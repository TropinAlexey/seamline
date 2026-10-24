import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap, type Schemas } from "../client";

// Gas storage (ADR-0036): contracts with their balance, the movement ledger,
// and the latest EOD valuation of the stock (MO, BO).
export type StorageContractEntry = Schemas["StorageContractEntry"];
export type InventoryMovementEntry = Schemas["InventoryMovementEntry"];

export const storageKeys = {
  all: ["storage-contracts"] as const,
  movements: (id: string) => ["storage-contracts", id, "movements"] as const,
  valuation: ["inventory", "valuations", "latest"] as const,
};

export function useStorageContracts() {
  return useQuery({
    queryKey: storageKeys.all,
    queryFn: () => unwrap(api.GET("/storage-contracts")),
  });
}

export function useMovements(id: string) {
  return useQuery({
    queryKey: storageKeys.movements(id),
    queryFn: () =>
      unwrap(api.GET("/storage-contracts/{id}/movements", { params: { path: { id } } })),
  });
}

export function useInventoryValuation(enabled: boolean) {
  return useQuery({
    queryKey: storageKeys.valuation,
    queryFn: () => unwrap(api.GET("/inventory/valuations/latest")),
    enabled,
  });
}

export function useRegisterStorage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: Schemas["RegisterStorageContractRequest"]) =>
      unwrap(api.POST("/storage-contracts", { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: storageKeys.all }),
  });
}

type Movement =
  | { kind: "inject"; body: Schemas["InjectGasRequest"] }
  | { kind: "withdraw"; body: Schemas["WithdrawGasRequest"] };

export function useRecordMovement(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: Movement) => {
      const params = { path: { id } };
      return input.kind === "inject"
        ? unwrap(api.POST("/storage-contracts/{id}/injections", { params, body: input.body }))
        : unwrap(api.POST("/storage-contracts/{id}/withdrawals", { params, body: input.body }));
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: storageKeys.all }),
  });
}
