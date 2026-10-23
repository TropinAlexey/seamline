import { Anchor, Stack, Table, Text, Title } from "@mantine/core";
import { Link } from "react-router";
import { useShipments } from "../../api/queries/trading";
import { QueryState } from "../../components/QueryState";
import { formatDate, shortId } from "../../format/date";
import { formatVolume } from "../../format/money";
import { shipmentStateLabel } from "./ShipmentPanel";

// Every Active or delivered cargo trade and where its shipment stands
// (ADR-0034). Back office records the steps on the trade's page.
export function ShipmentsPage() {
  const shipments = useShipments();

  return (
    <Stack>
      <Title order={2}>Shipments</Title>
      <Text c="dimmed" size="sm">
        Cargo trades (FOB): nominate a vessel inside the laycan, record the bill of lading — which
        delivers the trade — then the quality certificate, which issues the final invoice.
      </Text>
      <QueryState
        isLoading={shipments.isLoading}
        error={shipments.error}
        onRetry={() => void shipments.refetch()}
      >
        {shipments.data?.length ? (
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Trade</Table.Th>
                <Table.Th>Commodity</Table.Th>
                <Table.Th>Laycan</Table.Th>
                <Table.Th ta="right">Contract (t)</Table.Th>
                <Table.Th>Vessel</Table.Th>
                <Table.Th ta="right">B/L (t)</Table.Th>
                <Table.Th>Status</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {shipments.data.map((s) => (
                <Table.Tr key={s.tradeId} data-testid="shipment-row" data-trade-id={s.tradeId}>
                  <Table.Td>
                    <Anchor component={Link} to={`/trades/${s.tradeId}`} ff="monospace">
                      {shortId(s.tradeId)}
                    </Anchor>
                  </Table.Td>
                  <Table.Td>
                    {s.commodityCode} {s.deliveryPeriod}
                  </Table.Td>
                  <Table.Td>
                    {formatDate(s.laycanFrom)} – {formatDate(s.laycanTo)}
                  </Table.Td>
                  <Table.Td ta="right">{formatVolume(s.contractVolume)}</Table.Td>
                  <Table.Td>{s.vesselName ?? "—"}</Table.Td>
                  <Table.Td ta="right">{formatVolume(s.blQuantity)}</Table.Td>
                  <Table.Td>{shipmentStateLabel(s.state)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        ) : (
          <Text c="dimmed">No cargo trades yet — book COAL with cargo terms to start one.</Text>
        )}
      </QueryState>
    </Stack>
  );
}
