import {
  Alert,
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
import { useCounterparties, useCreateCounterparty } from "../../api/queries/reference";
import { useExposure } from "../../api/queries/risk";
import { useSession } from "../../auth/AuthContext";
import { errorMessage, notifySuccess } from "../../components/notify";
import { QueryState } from "../../components/QueryState";
import { formatAmount } from "../../format/money";

function CreateCounterpartyForm() {
  const create = useCreateCounterparty();
  const [name, setName] = useState("");
  const [creditLimit, setCreditLimit] = useState<number | "">("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    if (!name.trim() || creditLimit === "") {
      setError("Name and credit limit are required.");
      return;
    }
    try {
      await create.mutateAsync({ name: name.trim(), creditLimit });
      notifySuccess(`Counterparty ${name.trim()} created.`);
      setName("");
      setCreditLimit("");
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Paper withBorder p="md" component="form" onSubmit={submit}>
      <Group align="flex-end">
        <TextInput
          label="Name"
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
          w={260}
        />
        <NumberInput
          label="Credit limit"
          value={creditLimit}
          onChange={(v) => setCreditLimit(typeof v === "number" ? v : "")}
          decimalScale={2}
          min={0}
          w={200}
        />
        <Button type="submit" loading={create.isPending}>
          Create
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

// Exposure is Risk's number (same calculation as the credit check); the
// endpoint is FO/MO only, so back office sees limits without it.
function ExposureCell({ counterpartyId }: { counterpartyId: string }) {
  const exposure = useExposure(counterpartyId);
  return <>{formatAmount(exposure.data?.currentExposure)}</>;
}

export function CounterpartiesPage() {
  const session = useSession();
  const counterparties = useCounterparties();
  const showExposure = session.role === "FO" || session.role === "MO";

  return (
    <Stack>
      <Title order={2}>Counterparties</Title>
      {session.role === "FO" && <CreateCounterpartyForm />}
      <QueryState
        isLoading={counterparties.isLoading}
        error={counterparties.error}
        onRetry={() => counterparties.refetch()}
      >
        {counterparties.data?.length ? (
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th ta="right">Credit limit</Table.Th>
                {showExposure && <Table.Th ta="right">Current exposure</Table.Th>}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {counterparties.data.map((c) => (
                <Table.Tr key={c.id}>
                  <Table.Td>{c.name}</Table.Td>
                  <Table.Td ta="right">{formatAmount(c.creditLimit)}</Table.Td>
                  {showExposure && (
                    <Table.Td ta="right">
                      <ExposureCell counterpartyId={c.id} />
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        ) : (
          <Text c="dimmed">No counterparties in this tenant.</Text>
        )}
      </QueryState>
    </Stack>
  );
}
