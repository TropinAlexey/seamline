import {
  Alert,
  Button,
  Group,
  NumberInput,
  Paper,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
} from "@mantine/core";
import { useState, type FormEvent } from "react";
import { useFixings, useIndices, usePublishFixing } from "../../api/queries/marketData";
import { useSession } from "../../auth/AuthContext";
import { errorMessage, notifySuccess } from "../../components/notify";
import { QueryState } from "../../components/QueryState";
import { formatDate, formatDateTime } from "../../format/date";
import { formatPrice } from "../../format/money";

// Daily index fixings (ADR-0035). Append-only: the API refuses a second
// value for a day, so there is no edit here, only publish.
function PublishFixingForm({ indexCode }: { indexCode: string }) {
  const publish = usePublishFixing(indexCode);
  const [fixingDate, setFixingDate] = useState("");
  const [value, setValue] = useState<number | "">("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    if (!fixingDate || value === "" || value <= 0) {
      setError("A weekday and a positive value are required.");
      return;
    }
    try {
      await publish.mutateAsync({ fixingDate, value });
      notifySuccess(`${indexCode} ${fixingDate} fixed at ${formatPrice(value)}.`);
      setValue("");
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Paper withBorder p="md" component="form" onSubmit={submit}>
      <Group align="flex-end">
        <TextInput
          type="date"
          label="Fixing date"
          value={fixingDate}
          onChange={(e) => setFixingDate(e.currentTarget.value)}
          w={180}
        />
        <NumberInput
          label="Value"
          value={value}
          onChange={(v) => setValue(typeof v === "number" ? v : "")}
          decimalScale={4}
          w={160}
        />
        <Button type="submit" loading={publish.isPending}>
          Publish fixing
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

export function FixingsPanel() {
  const session = useSession();
  const indices = useIndices();
  const [selected, setSelected] = useState<string | null>(null);
  const indexCode = selected ?? indices.data?.[0]?.code ?? null;
  const fixings = useFixings(indexCode);

  return (
    <Stack>
      <Text size="sm" c="dimmed">
        One published value per index and weekday, never overwritten: index-priced trades average
        them, and invoices are computed from them.
      </Text>
      <Select
        label="Index"
        data={(indices.data ?? []).map((i) => ({ value: i.code, label: `${i.code} — ${i.name}` }))}
        value={indexCode}
        onChange={setSelected}
        w={360}
      />
      {session.role === "MO" && indexCode && <PublishFixingForm indexCode={indexCode} />}
      <QueryState isLoading={fixings.isLoading} error={fixings.error}>
        {fixings.data?.length ? (
          <Table striped data-testid="fixings">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Date</Table.Th>
                <Table.Th ta="right">Value</Table.Th>
                <Table.Th>Published</Table.Th>
                <Table.Th>By</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {fixings.data.map((f) => (
                <Table.Tr key={f.fixingDate}>
                  <Table.Td>{formatDate(f.fixingDate)}</Table.Td>
                  <Table.Td ta="right">{formatPrice(f.value)}</Table.Td>
                  <Table.Td>{formatDateTime(f.publishedAt)}</Table.Td>
                  <Table.Td>{f.publishedBy}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        ) : (
          <Text c="dimmed">No fixings published for this index yet.</Text>
        )}
      </QueryState>
    </Stack>
  );
}
