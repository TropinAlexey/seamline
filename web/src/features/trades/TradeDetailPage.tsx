import {
  Alert,
  Anchor,
  Button,
  Card,
  Group,
  Loader,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Timeline,
  Title,
} from "@mantine/core";
import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useState, type ReactNode } from "react";
import { Link, useParams } from "react-router";
import { useCounterpartyNames, useUnitOf } from "../../api/queries/reference";
import {
  runTradeAction,
  tradeKeys,
  useRemitReports,
  useTradeHistory,
  type TradeSummary,
} from "../../api/queries/trading";
import { useSession } from "../../auth/AuthContext";
import { isConflict, notifyError, notifySuccess } from "../../components/notify";
import { QueryState } from "../../components/QueryState";
import { StateBadge } from "../../components/StateBadge";
import { formatDateTime } from "../../format/date";
import { ShipmentPanel } from "../shipments/ShipmentPanel";
import { formatAmount, formatPrice, formatVolume } from "../../format/money";
import { allowedActions, type UiTradeAction } from "./allowedActions";
import { AmendModal } from "./AmendModal";
import { PricingPanel } from "./PricingPanel";
import { ApprovalCountdown } from "./ApprovalCountdown";
import { creditPendingSince, diffHistory, type FieldChange } from "./historyDiff";
import { StateDiagram } from "./StateDiagram";
import { useTradeWithSagaPolling } from "./useSagaPolling";

const actionLabels: Record<UiTradeAction, string> = {
  submit: "Submit",
  approve: "Approve",
  reject: "Reject",
  amend: "Amend",
  deliver: "Deliver",
  cancel: "Cancel",
};

const actionColors: Partial<Record<UiTradeAction, string>> = {
  approve: "green",
  reject: "red",
  cancel: "gray",
};

function formatChangeValue(change: FieldChange, value: FieldChange["from"]): string {
  switch (change.field) {
    case "volume":
    case "actualVolume":
      return formatVolume(value as number | null);
    case "price":
      return formatPrice(value as number | null);
    case "notional":
      return formatAmount(value as number | null);
    default:
      return String(value);
  }
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text>{children}</Text>
    </div>
  );
}

export function TradeDetailPage() {
  const { id = "" } = useParams();
  const session = useSession();
  const queryClient = useQueryClient();
  const counterpartyName = useCounterpartyNames();
  const unitOf = useUnitOf();
  const [amendOpen, setAmendOpen] = useState(false);
  const [busy, setBusy] = useState<UiTradeAction | null>(null);

  const refreshAll = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: tradeKeys.history(id) });
    void queryClient.invalidateQueries({ queryKey: tradeKeys.remit(id) });
    void queryClient.invalidateQueries({ queryKey: ["trades", "list"] });
  }, [queryClient, id]);

  const onResolved = useCallback(() => {
    refreshAll();
    notifySuccess("The saga applied the decision.", "Processed");
  }, [refreshAll]);

  const { trade, saga } = useTradeWithSagaPolling(id, onResolved);

  const history = useTradeHistory(id);
  const remit = useRemitReports(id);

  const run = async (current: TradeSummary, action: UiTradeAction) => {
    if (action === "amend") {
      setAmendOpen(true);
      return;
    }
    setBusy(action);
    try {
      const { accepted } = await runTradeAction(current.id, action);
      if (accepted) saga.start(current.state);
      await queryClient.invalidateQueries({ queryKey: tradeKeys.detail(id) });
      refreshAll();
    } catch (e) {
      notifyError(e, isConflict(e) ? "The trade changed under you" : "Action failed");
      // 409: the state moved since this page loaded — show the real one.
      await queryClient.invalidateQueries({ queryKey: tradeKeys.all });
    } finally {
      setBusy(null);
    }
  };

  return (
    <QueryState isLoading={trade.isLoading} error={trade.error} onRetry={() => trade.refetch()}>
      {trade.data && (
        <Stack>
          <Group justify="space-between">
            <Group>
              <Title order={2}>
                {trade.data.direction} {trade.data.commodityCode} {trade.data.deliveryPeriod}
              </Title>
              <StateBadge state={trade.data.state} />
              <Text c="dimmed">v{trade.data.version}</Text>
            </Group>
            <Anchor component={Link} to="/trades">
              ← Blotter
            </Anchor>
          </Group>

          <Card withBorder>
            <SimpleGrid cols={{ base: 2, md: 6 }}>
              <Field label="Trade id">
                <Text ff="monospace" size="sm">
                  {trade.data.id}
                </Text>
              </Field>
              <Field label="Volume">
                {formatVolume(trade.data.volume)} {unitOf(trade.data.commodityCode)}
              </Field>
              <Field label="Price">
                {trade.data.price == null && trade.data.pricing
                  ? `${trade.data.pricing.indexCode} average`
                  : formatPrice(trade.data.price)}
              </Field>
              <Field label="Notional">{formatAmount(trade.data.notional)}</Field>
              <Field label="Counterparty">{counterpartyName(trade.data.counterpartyId)}</Field>
              <Field label="Last changed">{formatDateTime(trade.data.lastChangedAt)}</Field>
            </SimpleGrid>
          </Card>

          <Card withBorder>
            <StateDiagram current={trade.data.state} />
          </Card>

          {saga.status === "processing" && (
            <Alert color="blue" icon={<Loader size="xs" />} title="Processing via saga…">
              The API accepted the request (202). The credit-approval saga applies it
              asynchronously; this page polls until the state changes.
            </Alert>
          )}
          {saga.status === "stalled" && (
            <Alert color="yellow" title="Still processing">
              No state change after 30 seconds — the message bus may be retrying. Refresh later.
            </Alert>
          )}

          {trade.data.state === "CreditPending" &&
            history.data &&
            creditPendingSince(history.data) && (
              <ApprovalCountdown since={creditPendingSince(history.data)!} />
            )}

          <Group data-testid="trade-actions">
            {allowedActions(trade.data.state, session.role, {
              cargo: trade.data.cargoTerms != null,
            }).map((action) => (
              <Button
                key={action}
                color={actionColors[action]}
                variant={action === "cancel" ? "light" : "filled"}
                loading={busy === action}
                disabled={saga.status === "processing"}
                onClick={() => run(trade.data!, action)}
              >
                {actionLabels[action]}
              </Button>
            ))}
          </Group>

          {trade.data.pricing && <PricingPanel trade={trade.data} />}

          {trade.data.cargoTerms && <ShipmentPanel trade={trade.data} />}

          <SimpleGrid cols={{ base: 1, lg: 2 }}>
            <Card withBorder>
              <Title order={4} mb="sm">
                History
              </Title>
              <QueryState isLoading={history.isLoading} error={history.error}>
                <Timeline bulletSize={18} lineWidth={2} active={(history.data?.length ?? 1) - 1}>
                  {diffHistory(history.data ?? []).map(({ entry, changes }) => (
                    <Timeline.Item
                      key={entry.version}
                      title={
                        <Group gap="xs">
                          <Text fw={600}>v{entry.version}</Text>
                          <StateBadge state={entry.state} />
                        </Group>
                      }
                      data-testid="history-entry"
                    >
                      <Text size="sm">
                        {entry.changeReason} —{" "}
                        <b data-testid="history-changed-by">{entry.changedBy}</b>
                      </Text>
                      <Text size="xs" c="dimmed">
                        {formatDateTime(entry.validFrom)}
                      </Text>
                      {changes
                        .filter((c) => c.field !== "state")
                        .map((change) => (
                          <Text size="xs" key={change.field} data-testid="history-change">
                            {change.field}: {formatChangeValue(change, change.from)} →{" "}
                            {formatChangeValue(change, change.to)}
                          </Text>
                        ))}
                    </Timeline.Item>
                  ))}
                </Timeline>
              </QueryState>
            </Card>

            <Card withBorder>
              <Title order={4} mb="sm">
                REMIT reports
              </Title>
              <QueryState isLoading={remit.isLoading} error={remit.error}>
                {trade.data.cargoTerms ? (
                  <Text c="dimmed" size="sm">
                    Not reportable — REMIT covers electricity and natural gas, not coal.
                  </Text>
                ) : remit.data?.length ? (
                  <Table>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>Version</Table.Th>
                        <Table.Th>Action</Table.Th>
                        <Table.Th>Ack id</Table.Th>
                        <Table.Th>Submitted</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {remit.data.map((report) => (
                        <Table.Tr key={report.version}>
                          <Table.Td>{report.version}</Table.Td>
                          <Table.Td>{report.action}</Table.Td>
                          <Table.Td ff="monospace">{report.ackId}</Table.Td>
                          <Table.Td>{formatDateTime(report.submittedAt)}</Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                ) : (
                  <Text c="dimmed" size="sm">
                    Not reported yet — the reporting worker submits to the ACER stub once a day.
                  </Text>
                )}
              </QueryState>
            </Card>
          </SimpleGrid>

          {session.role === "FO" && trade.data.state === "Active" && (
            <AmendModal
              key={trade.data.version}
              trade={trade.data}
              opened={amendOpen}
              onClose={() => setAmendOpen(false)}
            />
          )}
        </Stack>
      )}
    </QueryState>
  );
}
