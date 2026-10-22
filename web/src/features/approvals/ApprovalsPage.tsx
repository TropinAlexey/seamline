import { Badge, Button, Group, Stack, Text, Title } from "@mantine/core";
import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { runTradeAction, tradeKeys, useTrades, type TradeSummary } from "../../api/queries/trading";
import { notifyError, notifySuccess } from "../../components/notify";
import { QueryState } from "../../components/QueryState";
import { ApprovalCountdown } from "../trades/ApprovalCountdown";
import { TradeTable } from "../trades/TradeTable";
import { ExposureSummary } from "./ExposureSummary";

// MO home: the four-eyes queue. A decision is a 202 — the saga applies it
// asynchronously — so decided rows stay visible as "processing" and the
// list polls fast until they leave CreditPending.
export function ApprovalsPage() {
  const queryClient = useQueryClient();
  const [decided, setDecided] = useState<ReadonlySet<string>>(new Set());
  const [refetchInterval, setRefetchInterval] = useState(5000);
  const trades = useTrades(
    { state: ["CreditPending"], page: 1, pageSize: 100 },
    { refetchInterval },
  );

  const pending = trades.data?.items;
  // A decided trade still listed as CreditPending is one the saga hasn't
  // applied yet — poll fast only while there is one.
  const awaitingSaga = (pending ?? []).some((t) => decided.has(t.id));
  const wantedInterval = awaitingSaga ? 1000 : 5000;
  if (wantedInterval !== refetchInterval) setRefetchInterval(wantedInterval);

  const decide = async (trade: TradeSummary, action: "approve" | "reject") => {
    try {
      await runTradeAction(trade.id, action);
      setDecided((current) => new Set(current).add(trade.id));
      notifySuccess(
        "Decision sent — the saga will apply it.",
        action === "approve" ? "Approving" : "Rejecting",
      );
    } catch (e) {
      notifyError(e);
      await queryClient.invalidateQueries({ queryKey: tradeKeys.all });
    }
  };

  return (
    <Stack>
      <Title order={2}>Approvals</Title>
      <Text c="dimmed" size="sm">
        Trades that breached their counterparty&apos;s credit limit wait here for a middle-office
        decision. No decision within 5 minutes rejects them automatically.
      </Text>
      <QueryState
        isLoading={trades.isLoading}
        error={trades.error}
        onRetry={() => trades.refetch()}
      >
        <TradeTable
          trades={pending ?? []}
          extraHeader="Decision"
          renderExtra={(trade) =>
            decided.has(trade.id) ? (
              <Badge color="yellow" variant="outline">
                Processing via saga…
              </Badge>
            ) : (
              <Stack gap={4}>
                <ExposureSummary counterpartyId={trade.counterpartyId} notional={trade.notional} />
                <ApprovalCountdown since={trade.lastChangedAt} />
                <Group gap="xs">
                  <Button size="xs" color="green" onClick={() => decide(trade, "approve")}>
                    Approve
                  </Button>
                  <Button
                    size="xs"
                    color="red"
                    variant="light"
                    onClick={() => decide(trade, "reject")}
                  >
                    Reject
                  </Button>
                </Group>
              </Stack>
            )
          }
        />
      </QueryState>
    </Stack>
  );
}
