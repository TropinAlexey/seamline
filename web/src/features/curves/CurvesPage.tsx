import {
  Alert,
  Autocomplete,
  Button,
  Group,
  NumberInput,
  Paper,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { useState, type FormEvent } from "react";
import { useCurvePoints, useUpsertCurvePoint } from "../../api/queries/marketData";
import { useSession } from "../../auth/AuthContext";
import { errorMessage, notifySuccess } from "../../components/notify";
import { QueryState } from "../../components/QueryState";
import { formatDateTime } from "../../format/date";
import { formatPrice } from "../../format/money";

// Middle office moves a price here and watches live MtM change on
// Positions — the quickest way to see the risk read model react.
function UpsertCurvePointForm({ commodities }: { commodities: string[] }) {
  const upsert = useUpsertCurvePoint();
  const [commodityCode, setCommodityCode] = useState("");
  const [deliveryPeriod, setDeliveryPeriod] = useState("");
  const [price, setPrice] = useState<number | "">("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    if (!commodityCode.trim() || !deliveryPeriod || price === "" || price <= 0) {
      setError("Commodity, delivery month and a positive price are required.");
      return;
    }
    try {
      await upsert.mutateAsync({
        commodityCode: commodityCode.trim().toUpperCase(),
        deliveryPeriod,
        price,
      });
      notifySuccess(
        `${commodityCode.toUpperCase()} ${deliveryPeriod} published at ${formatPrice(price)}.`,
      );
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Paper withBorder p="md" component="form" onSubmit={submit}>
      <Group align="flex-end">
        <Autocomplete
          label="Commodity"
          data={commodities}
          value={commodityCode}
          onChange={setCommodityCode}
          w={160}
        />
        <TextInput
          type="month"
          label="Delivery period"
          value={deliveryPeriod}
          onChange={(e) => setDeliveryPeriod(e.currentTarget.value)}
          w={180}
        />
        <NumberInput
          label="Price"
          value={price}
          onChange={(v) => setPrice(typeof v === "number" ? v : "")}
          decimalScale={4}
          w={160}
        />
        <Button type="submit" loading={upsert.isPending}>
          Publish
        </Button>
      </Group>
      {error && (
        <Alert color="red" mt="sm" role="alert">
          {error}
        </Alert>
      )}
    </Paper>
  );
}

export function CurvesPage() {
  const session = useSession();
  const curve = useCurvePoints();
  const commodities = [...new Set((curve.data ?? []).map((p) => p.commodityCode))];

  return (
    <Stack>
      <Title order={2}>Market data</Title>
      <Text size="sm" c="dimmed">
        Forward curve: one flat price per commodity and delivery month. The curve-import job
        refreshes the current month daily; middle office can publish any point.
      </Text>
      {session.role === "MO" && <UpsertCurvePointForm commodities={commodities} />}
      <QueryState isLoading={curve.isLoading} error={curve.error} onRetry={() => curve.refetch()}>
        {curve.data?.length ? (
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Commodity</Table.Th>
                <Table.Th>Period</Table.Th>
                <Table.Th ta="right">Price</Table.Th>
                <Table.Th>Published</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {curve.data.map((p) => (
                <Table.Tr key={`${p.commodityCode}-${p.deliveryPeriod}`}>
                  <Table.Td>{p.commodityCode}</Table.Td>
                  <Table.Td>{p.deliveryPeriod}</Table.Td>
                  <Table.Td ta="right">{formatPrice(p.price)}</Table.Td>
                  <Table.Td>{formatDateTime(p.publishedAt)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        ) : (
          <Text c="dimmed">No curve points published in this tenant.</Text>
        )}
      </QueryState>
    </Stack>
  );
}
