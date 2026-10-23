import { Anchor, Badge, Group, Stack, Table, Tabs, Text, Title } from "@mantine/core";
import { Link } from "react-router";
import { useSession } from "../../auth/AuthContext";
import { useLatestValuation, usePositions, usePricingExposure } from "../../api/queries/risk";
import { QueryState } from "../../components/QueryState";
import { formatDateTime } from "../../format/date";
import { formatAmount, formatPrice, formatVolume } from "../../format/money";

// Two labelled sources (spec §4.6): live MtM against the current curve, and
// the latest end-of-day snapshot. Every amount and total is computed by the
// server — this page only formats (ADR-0030).
function LivePositions() {
  const positions = usePositions();
  return (
    <QueryState
      isLoading={positions.isLoading}
      error={positions.error}
      onRetry={() => positions.refetch()}
    >
      <Text size="sm" c="dimmed" mb="sm">
        Marked against the current curve; refreshes every 10 seconds and whenever middle office
        moves a price.
      </Text>
      {positions.data?.items.length ? (
        <Table striped data-testid="live-positions">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Commodity</Table.Th>
              <Table.Th>Period</Table.Th>
              <Table.Th ta="right">Net volume</Table.Th>
              <Table.Th ta="right">Avg price</Table.Th>
              <Table.Th ta="right">Curve price</Table.Th>
              <Table.Th ta="right">MTM</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {positions.data.items.map((p) => (
              <Table.Tr
                key={`${p.commodityCode}-${p.deliveryPeriod}`}
                data-testid="position-row"
                data-key={`${p.commodityCode}-${p.deliveryPeriod}`}
              >
                <Table.Td>{p.commodityCode}</Table.Td>
                <Table.Td>{p.deliveryPeriod}</Table.Td>
                <Table.Td ta="right">{formatVolume(p.netVolume)}</Table.Td>
                <Table.Td ta="right">{formatPrice(p.weightedAvgPrice)}</Table.Td>
                <Table.Td ta="right">
                  {p.markPrice === null ? (
                    <Badge variant="outline">no curve</Badge>
                  ) : (
                    formatPrice(p.markPrice)
                  )}
                </Table.Td>
                <Table.Td ta="right" data-testid="position-mtm">
                  {formatAmount(p.mtmAmount)}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
          <Table.Tfoot>
            <Table.Tr>
              <Table.Th colSpan={5}>Total</Table.Th>
              <Table.Th ta="right" data-testid="positions-total">
                {formatAmount(positions.data.totalMtm)}
              </Table.Th>
            </Table.Tr>
          </Table.Tfoot>
        </Table>
      ) : (
        <Text c="dimmed">No open positions.</Text>
      )}
    </QueryState>
  );
}

// Index-priced trades (ADR-0035) are valued one by one, never blended into
// a position: MtM comes only from the days already fixed.
function FormulaTrades() {
  const session = useSession();
  const exposure = usePricingExposure(session.role !== "BO");
  if (!exposure.data?.items.length) return null;
  return (
    <Stack gap="xs" mt="lg">
      <Title order={4}>Index-priced trades</Title>
      <Text size="sm" c="dimmed">
        The unfixed days move with the forward on both sides, so only the fixed share carries price
        risk.
      </Text>
      <Table striped data-testid="formula-trades">
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Trade</Table.Th>
            <Table.Th>Period</Table.Th>
            <Table.Th>Index</Table.Th>
            <Table.Th ta="right">Days fixed</Table.Th>
            <Table.Th ta="right">Fixed qty</Table.Th>
            <Table.Th ta="right">Unfixed qty</Table.Th>
            <Table.Th ta="right">Expected</Table.Th>
            <Table.Th ta="right">Forward</Table.Th>
            <Table.Th ta="right">MTM</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {exposure.data.items.map((t) => (
            <Table.Tr key={t.tradeId}>
              <Table.Td>
                <Anchor component={Link} to={`/trades/${t.tradeId}`} ff="monospace" size="sm">
                  {t.commodityCode} {t.tradeId.slice(0, 8)}
                </Anchor>
              </Table.Td>
              <Table.Td>{t.deliveryPeriod}</Table.Td>
              <Table.Td>{t.indexCode}</Table.Td>
              <Table.Td ta="right">
                {t.fixedDays} / {t.pricingDays}
              </Table.Td>
              <Table.Td ta="right">{formatVolume(t.fixedQuantity)}</Table.Td>
              <Table.Td ta="right">{formatVolume(t.unfixedQuantity)}</Table.Td>
              <Table.Td ta="right">{formatPrice(t.finalPrice ?? t.expectedPrice)}</Table.Td>
              <Table.Td ta="right">{formatPrice(t.forward)}</Table.Td>
              <Table.Td ta="right">{formatAmount(t.mtmAmount)}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
        <Table.Tfoot>
          <Table.Tr>
            <Table.Th colSpan={8}>Total</Table.Th>
            <Table.Th ta="right">{formatAmount(exposure.data.totalMtm)}</Table.Th>
          </Table.Tr>
        </Table.Tfoot>
      </Table>
    </Stack>
  );
}

function EodSnapshot() {
  const valuation = useLatestValuation();
  return (
    <QueryState
      isLoading={valuation.isLoading}
      error={valuation.error}
      onRetry={() => valuation.refetch()}
    >
      {valuation.data?.runId ? (
        <>
          <Text size="sm" c="dimmed" mb="sm">
            EOD run {valuation.data.runId.slice(0, 8)} at {formatDateTime(valuation.data.runAt)}.
            Snapshots carry the curve price they used, so they stay reproducible after the curve
            moves.
          </Text>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Commodity</Table.Th>
                <Table.Th>Period</Table.Th>
                <Table.Th ta="right">Net volume</Table.Th>
                <Table.Th ta="right">Avg price</Table.Th>
                <Table.Th ta="right">Curve price</Table.Th>
                <Table.Th>Curve published</Table.Th>
                <Table.Th ta="right">MTM</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {valuation.data.items.map((v) => (
                <Table.Tr key={`${v.commodityCode}-${v.deliveryPeriod}-${v.tradeId ?? "position"}`}>
                  <Table.Td>
                    {v.commodityCode}
                    {v.tradeId && (
                      <Text span size="xs" c="dimmed" ml={4}>
                        (index trade {v.tradeId.slice(0, 8)})
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>{v.deliveryPeriod}</Table.Td>
                  <Table.Td ta="right">{formatVolume(v.netVolume)}</Table.Td>
                  <Table.Td ta="right">{formatPrice(v.weightedAvgPrice)}</Table.Td>
                  <Table.Td ta="right">{formatPrice(v.curvePrice)}</Table.Td>
                  <Table.Td>{formatDateTime(v.curvePublishedAt)}</Table.Td>
                  <Table.Td ta="right">{formatAmount(v.mtmAmount)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
            <Table.Tfoot>
              <Table.Tr>
                <Table.Th colSpan={6}>Total</Table.Th>
                <Table.Th ta="right">{formatAmount(valuation.data.totalMtm)}</Table.Th>
              </Table.Tr>
            </Table.Tfoot>
          </Table>
        </>
      ) : (
        <Text c="dimmed">
          No EOD run yet. It runs daily at 06:00; to run it now, trigger <b>eod-valuation</b> in the
          valuation worker&apos;s Hangfire dashboard (http://localhost:8082/hangfire in the compose
          demo).
        </Text>
      )}
    </QueryState>
  );
}

export function PositionsPage() {
  return (
    <Stack>
      <Group>
        <Title order={2}>Positions &amp; MTM</Title>
      </Group>
      <Tabs defaultValue="live" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="live">Live</Tabs.Tab>
          <Tabs.Tab value="eod">EOD snapshot</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="live" pt="md">
          <LivePositions />
          <FormulaTrades />
        </Tabs.Panel>
        <Tabs.Panel value="eod" pt="md">
          <EodSnapshot />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  );
}
