import { Table, Text } from "@mantine/core";
import type { ReactNode } from "react";
import { useNavigate } from "react-router";
import type { TradeSummary } from "../../api/queries/trading";
import { useCounterpartyNames, useUnitOf } from "../../api/queries/reference";
import { StateBadge } from "../../components/StateBadge";
import { formatDateTime, shortId } from "../../format/date";
import { formatAmount, formatPrice, formatVolume } from "../../format/money";

interface Props {
  trades: readonly TradeSummary[];
  extraHeader?: string;
  renderExtra?: (trade: TradeSummary) => ReactNode;
}

export function TradeTable({ trades, extraHeader, renderExtra }: Props) {
  const navigate = useNavigate();
  const counterpartyName = useCounterpartyNames();
  const unitOf = useUnitOf();

  if (trades.length === 0) {
    return (
      <Text c="dimmed" py="lg" ta="center">
        No trades.
      </Text>
    );
  }

  return (
    <Table.ScrollContainer minWidth={1000}>
      <Table highlightOnHover striped>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Id</Table.Th>
            <Table.Th>Commodity</Table.Th>
            <Table.Th>Period</Table.Th>
            <Table.Th>Dir</Table.Th>
            <Table.Th ta="right">Volume</Table.Th>
            <Table.Th ta="right">Price</Table.Th>
            <Table.Th ta="right">Notional</Table.Th>
            <Table.Th>Counterparty</Table.Th>
            <Table.Th>State</Table.Th>
            <Table.Th ta="right">Ver</Table.Th>
            <Table.Th>Last changed</Table.Th>
            {renderExtra && <Table.Th>{extraHeader}</Table.Th>}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {trades.map((trade) => {
            const open = () => navigate(`/trades/${trade.id}`);
            return (
              <Table.Tr
                key={trade.id}
                onClick={open}
                onKeyDown={(e) => e.key === "Enter" && open()}
                tabIndex={0}
                style={{ cursor: "pointer" }}
                data-testid="trade-row"
                data-trade-id={trade.id}
              >
                <Table.Td ff="monospace">{shortId(trade.id)}</Table.Td>
                <Table.Td>{trade.commodityCode}</Table.Td>
                <Table.Td>{trade.deliveryPeriod}</Table.Td>
                <Table.Td>{trade.direction}</Table.Td>
                <Table.Td ta="right">
                  {formatVolume(trade.volume)} {unitOf(trade.commodityCode)}
                </Table.Td>
                <Table.Td ta="right">
                  {trade.price == null && trade.pricing ? (
                    <Text span size="sm" c="dimmed" title={`Average of ${trade.pricing.indexCode}`}>
                      {trade.pricing.indexCode} avg
                    </Text>
                  ) : (
                    formatPrice(trade.price)
                  )}
                </Table.Td>
                <Table.Td ta="right">{formatAmount(trade.notional)}</Table.Td>
                <Table.Td>{counterpartyName(trade.counterpartyId)}</Table.Td>
                <Table.Td>
                  <StateBadge state={trade.state} />
                </Table.Td>
                <Table.Td ta="right">{trade.version}</Table.Td>
                <Table.Td>{formatDateTime(trade.lastChangedAt)}</Table.Td>
                {renderExtra && (
                  // Inline actions must not also open the row.
                  <Table.Td
                    onClick={(e) => e.stopPropagation()}
                    onKeyDown={(e) => e.stopPropagation()}
                  >
                    {renderExtra(trade)}
                  </Table.Td>
                )}
              </Table.Tr>
            );
          })}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}
