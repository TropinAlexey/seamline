import {
  Button,
  Group,
  MultiSelect,
  Pagination,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { useState } from "react";
import { tradeStates, useTrades, type TradeState } from "../../api/queries/trading";
import { useSession } from "../../auth/AuthContext";
import { QueryState } from "../../components/QueryState";
import { NewTradeModal } from "./NewTradeModal";
import { TradeTable } from "./TradeTable";

const pageSize = 20;

export function TradesPage() {
  const session = useSession();
  const [states, setStates] = useState<TradeState[]>([]);
  const [commodity, setCommodity] = useState("");
  const [period, setPeriod] = useState("");
  const [page, setPage] = useState(1);
  const [newTradeOpen, setNewTradeOpen] = useState(false);

  const trades = useTrades(
    { state: states, commodity: commodity.trim(), period, page, pageSize },
    { refetchInterval: 5000 },
  );
  const total = trades.data?.total ?? 0;

  const resetPage =
    <T,>(setter: (value: T) => void) =>
    (value: T) => {
      setter(value);
      setPage(1);
    };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Trades</Title>
        {session.role === "FO" && <Button onClick={() => setNewTradeOpen(true)}>New trade</Button>}
      </Group>
      <Group align="flex-end">
        <MultiSelect
          label="State"
          data={[...tradeStates]}
          value={states}
          onChange={resetPage((v: string[]) => setStates(v as TradeState[]))}
          clearable
          w={320}
        />
        <TextInput
          label="Commodity"
          value={commodity}
          onChange={(e) => resetPage(setCommodity)(e.currentTarget.value.toUpperCase())}
          w={140}
        />
        <TextInput
          type="month"
          label="Delivery period"
          value={period}
          onChange={(e) => resetPage(setPeriod)(e.currentTarget.value)}
          w={180}
        />
      </Group>
      <QueryState
        isLoading={trades.isLoading}
        error={trades.error}
        onRetry={() => trades.refetch()}
      >
        <TradeTable trades={trades.data?.items ?? []} />
        <Group justify="space-between">
          <Text size="sm" c="dimmed">
            {total} trade{total === 1 ? "" : "s"}
          </Text>
          <Pagination
            total={Math.max(1, Math.ceil(total / pageSize))}
            value={page}
            onChange={setPage}
          />
        </Group>
      </QueryState>
      {session.role === "FO" && (
        <NewTradeModal opened={newTradeOpen} onClose={() => setNewTradeOpen(false)} />
      )}
    </Stack>
  );
}
