import { Card, Group, SimpleGrid, Stack, Table, Text, Title } from "@mantine/core";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useLatestStress, type StressScenarioGroup } from "../../api/queries/risk";
import { QueryState } from "../../components/QueryState";
import { formatDateTime } from "../../format/date";
import { formatAmount, formatAmountCompact, formatPrice, formatVolume } from "../../format/money";

function scenarioLabel(
  group: Pick<StressScenarioGroup, "scenarioType" | "shockPercentage">,
): string {
  const sign = group.shockPercentage > 0 ? "+" : "";
  const kind = group.scenarioType === "FlatShock" ? "Flat" : "Single commodity";
  return `${kind} ${sign}${group.shockPercentage}%`;
}

interface ChartRow {
  label: string;
  totalMtm: number;
  isBase: boolean;
}

// One chart only (spec §4.7): total MTM per scenario next to the base. One
// series, so no legend — the title names it, and the tables below are the
// accessible view of the same numbers. Bars are plotted straight from
// server totals; nothing is summed here.
function StressChart({ rows }: { rows: ChartRow[] }) {
  return (
    <ResponsiveContainer width="100%" height={280}>
      <BarChart
        data={rows}
        margin={{ top: 8, right: 16, bottom: 8, left: 16 }}
        barCategoryGap="30%"
      >
        <CartesianGrid vertical={false} stroke="var(--mantine-color-default-border)" />
        <XAxis
          dataKey="label"
          tick={{ fill: "var(--mantine-color-dimmed)", fontSize: 12 }}
          axisLine={false}
          tickLine={false}
        />
        <YAxis
          // Bars must grow from zero, so the domain always includes it —
          // otherwise an all-negative book hangs its bars off the top edge.
          domain={[(min: number) => Math.min(0, min), (max: number) => Math.max(0, max)]}
          tickFormatter={(v: number) => formatAmountCompact(v)}
          tick={{ fill: "var(--mantine-color-dimmed)", fontSize: 12 }}
          axisLine={false}
          tickLine={false}
          width={64}
        />
        <ReferenceLine y={0} stroke="var(--mantine-color-dimmed)" />
        <Tooltip
          cursor={{ fill: "var(--mantine-color-default-hover)" }}
          formatter={(value) => [formatAmount(Number(value)), "Total MTM"]}
          contentStyle={{
            background: "var(--mantine-color-body)",
            border: "1px solid var(--mantine-color-default-border)",
            color: "var(--mantine-color-text)",
          }}
        />
        <Bar dataKey="totalMtm" radius={[4, 4, 0, 0]} maxBarSize={56}>
          {rows.map((row) => (
            <Cell
              key={row.label}
              fill={
                row.isBase ? "var(--mantine-color-gray-filled)" : "var(--mantine-color-blue-filled)"
              }
            />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}

export function StressPage() {
  const stress = useLatestStress();
  const data = stress.data;

  return (
    <Stack>
      <Title order={2}>Stress scenarios</Title>
      <QueryState
        isLoading={stress.isLoading}
        error={stress.error}
        onRetry={() => stress.refetch()}
      >
        {data?.runId ? (
          <>
            <Text size="sm" c="dimmed">
              EOD run {data.runId.slice(0, 8)} at {formatDateTime(data.runAt)}. Fixed scenarios
              (ADR-0016): flat ±10% across all commodities, single-commodity ±25%. Base MTM is from
              the same run.
            </Text>
            <Card withBorder>
              <Text fw={600} mb="xs">
                Total MTM per scenario (grey = base)
              </Text>
              <StressChart
                rows={[
                  { label: "Base", totalMtm: data.baseTotalMtm, isBase: true },
                  ...data.scenarios.map((s) => ({
                    label: scenarioLabel(s),
                    totalMtm: s.totalMtm,
                    isBase: false,
                  })),
                ]}
              />
            </Card>
            <SimpleGrid cols={{ base: 1, lg: 2 }}>
              {data.scenarios.map((scenario) => (
                <Card withBorder key={scenarioLabel(scenario)}>
                  <Group justify="space-between" mb="xs">
                    <Text fw={600}>{scenarioLabel(scenario)}</Text>
                    <Text>
                      {formatAmount(scenario.totalMtm)}{" "}
                      <Text span c="dimmed" size="sm">
                        vs base {formatAmount(data.baseTotalMtm)}
                      </Text>
                    </Text>
                  </Group>
                  <Table>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>Position</Table.Th>
                        <Table.Th ta="right">Net volume</Table.Th>
                        <Table.Th ta="right">Shocked price</Table.Th>
                        <Table.Th ta="right">MTM</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {scenario.items.map((item) => (
                        <Table.Tr
                          key={`${item.commodityCode}-${item.deliveryPeriod}-${item.tradeId ?? "position"}`}
                        >
                          <Table.Td>
                            {item.commodityCode} {item.deliveryPeriod}
                          </Table.Td>
                          <Table.Td ta="right">{formatVolume(item.netVolume)}</Table.Td>
                          <Table.Td ta="right">{formatPrice(item.shockedPrice)}</Table.Td>
                          <Table.Td ta="right">{formatAmount(item.mtmAmount)}</Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </Card>
              ))}
            </SimpleGrid>
          </>
        ) : (
          <Text c="dimmed">
            No EOD run yet. Trigger <b>eod-valuation</b> in the valuation worker&apos;s Hangfire
            dashboard (http://localhost:8082/hangfire in the compose demo).
          </Text>
        )}
      </QueryState>
    </Stack>
  );
}
