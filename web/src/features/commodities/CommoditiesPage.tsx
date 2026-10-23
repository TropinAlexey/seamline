import { Stack, Table, Text, Title } from "@mantine/core";
import { unitLabels, useCommodities } from "../../api/queries/reference";
import { QueryState } from "../../components/QueryState";

// Commodity master data (ADR-0033): what a volume means (unit), how the
// commodity is delivered, and — for cargo commodities — the contract quality
// the price refers to. Read-only here; middle office maintains it through
// the API.
export function CommoditiesPage() {
  const commodities = useCommodities();

  return (
    <Stack>
      <Title order={2}>Commodities</Title>
      <QueryState
        isLoading={commodities.isLoading}
        error={commodities.error}
        onRetry={() => void commodities.refetch()}
      >
        <Table striped>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Code</Table.Th>
              <Table.Th>Name</Table.Th>
              <Table.Th>Unit</Table.Th>
              <Table.Th>Delivery</Table.Th>
              <Table.Th>Contract quality</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(commodities.data ?? []).map((c) => (
              <Table.Tr key={c.code} data-testid="commodity-row">
                <Table.Td>{c.code}</Table.Td>
                <Table.Td>{c.name}</Table.Td>
                <Table.Td>{unitLabels[c.unit]}</Table.Td>
                <Table.Td>{c.deliveryModel}</Table.Td>
                <Table.Td>
                  {c.qualitySpec ? (
                    `NAR ${c.qualitySpec.baseNarKcalKg} kcal/kg (reject < ${c.qualitySpec.rejectBelowNarKcalKg}), ` +
                    `S ≤ ${c.qualitySpec.maxSulphurPct}%, ash ≤ ${c.qualitySpec.maxAshPct}%`
                  ) : (
                    <Text c="dimmed" size="sm">
                      —
                    </Text>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </QueryState>
    </Stack>
  );
}
