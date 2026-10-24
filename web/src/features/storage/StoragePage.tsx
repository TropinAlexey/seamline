import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  NumberInput,
  Paper,
  Progress,
  SegmentedControl,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { useState, type FormEvent, type ReactNode } from "react";
import {
  useInventoryValuation,
  useMovements,
  useRecordMovement,
  useRegisterStorage,
  useStorageContracts,
  type StorageContractEntry,
} from "../../api/queries/inventory";
import { useSession } from "../../auth/AuthContext";
import { errorMessage, notifySuccess } from "../../components/notify";
import { QueryState } from "../../components/QueryState";
import { formatDate, formatDateTime } from "../../format/date";
import { formatAmount, formatPrice, formatVolume } from "../../format/money";

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm">{children}</Text>
    </div>
  );
}

// Back office records a movement. The server enforces the daily rates,
// capacity and the stock; this form only collects the input (ADR-0030).
function MovementForm({ contract }: { contract: StorageContractEntry }) {
  const record = useRecordMovement(contract.id);
  const [kind, setKind] = useState<"inject" | "withdraw">("inject");
  const [gasDay, setGasDay] = useState("");
  const [quantity, setQuantity] = useState<number | "">("");
  const [unitCost, setUnitCost] = useState<number | "">("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    if (!gasDay || quantity === "" || (kind === "inject" && unitCost === "")) {
      setError(
        kind === "inject"
          ? "Gas day, quantity and unit cost are required."
          : "Gas day and quantity are required.",
      );
      return;
    }
    try {
      await record.mutateAsync(
        kind === "inject"
          ? { kind, body: { gasDay, quantity, unitCost: unitCost === "" ? 0 : unitCost } }
          : { kind, body: { gasDay, quantity } },
      );
      notifySuccess(
        `${kind === "inject" ? "Injected" : "Withdrawn"} ${formatVolume(quantity)} MWh.`,
      );
      setQuantity("");
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Paper withBorder p="sm" component="form" onSubmit={submit}>
      <Group align="flex-end">
        <SegmentedControl
          aria-label="Movement"
          data={[
            { value: "inject", label: "Inject" },
            { value: "withdraw", label: "Withdraw" },
          ]}
          value={kind}
          onChange={(v) => setKind(v as "inject" | "withdraw")}
        />
        <TextInput
          type="date"
          label="Gas day"
          value={gasDay}
          onChange={(e) => setGasDay(e.currentTarget.value)}
        />
        <NumberInput
          label="Quantity (MWh)"
          value={quantity}
          onChange={(v) => setQuantity(typeof v === "number" ? v : "")}
          decimalScale={3}
          w={150}
        />
        {kind === "inject" && (
          <NumberInput
            label="Unit cost (EUR/MWh)"
            value={unitCost}
            onChange={(v) => setUnitCost(typeof v === "number" ? v : "")}
            decimalScale={4}
            w={170}
          />
        )}
        <Button type="submit" loading={record.isPending}>
          Record
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

function Ledger({ contractId }: { contractId: string }) {
  const movements = useMovements(contractId);
  return (
    <QueryState isLoading={movements.isLoading} error={movements.error}>
      {movements.data?.length ? (
        <Table striped data-testid="storage-ledger">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Gas day</Table.Th>
              <Table.Th>Movement</Table.Th>
              <Table.Th ta="right">MWh</Table.Th>
              <Table.Th ta="right">Unit cost</Table.Th>
              <Table.Th ta="right">Cost (EUR)</Table.Th>
              <Table.Th ta="right">Stock after</Table.Th>
              <Table.Th ta="right">WACOG after</Table.Th>
              <Table.Th>By</Table.Th>
              <Table.Th>Recorded</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {[...movements.data].reverse().map((m) => (
              <Table.Tr key={m.version}>
                <Table.Td>{formatDate(m.gasDay)}</Table.Td>
                <Table.Td>{m.kind}</Table.Td>
                <Table.Td ta="right">{formatVolume(m.quantity)}</Table.Td>
                <Table.Td ta="right">{formatPrice(m.unitCost)}</Table.Td>
                <Table.Td ta="right">{formatAmount(m.cost)}</Table.Td>
                <Table.Td ta="right">{formatVolume(m.quantityAfter)}</Table.Td>
                <Table.Td ta="right">{formatPrice(m.wacogAfter)}</Table.Td>
                <Table.Td>{m.actor}</Table.Td>
                <Table.Td>{formatDateTime(m.recordedAt)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      ) : (
        <Text size="sm" c="dimmed">
          No movements yet.
        </Text>
      )}
    </QueryState>
  );
}

function ContractCard({
  contract,
  unrealized,
}: {
  contract: StorageContractEntry;
  unrealized: number | null | undefined;
}) {
  const session = useSession();
  const [showLedger, setShowLedger] = useState(false);

  return (
    <Card withBorder data-testid="storage-card">
      <Stack gap="sm">
        <Group justify="space-between">
          <Group>
            <Title order={4}>{contract.facility}</Title>
            <Badge variant="light">{contract.commodityCode}</Badge>
          </Group>
          <Text size="sm" c="dimmed">
            {formatDate(contract.validFrom)} – {formatDate(contract.validTo)}
          </Text>
        </Group>
        <div>
          <Group justify="space-between" mb={4}>
            <Text size="sm" data-testid="storage-fill">
              {formatVolume(contract.quantity)} of {formatVolume(contract.capacity)} MWh
            </Text>
            <Text size="sm" c="dimmed">
              {contract.fillPct}% full
            </Text>
          </Group>
          <Progress value={contract.fillPct} aria-label="Storage fill" />
        </div>
        <SimpleGrid cols={{ base: 2, md: 5 }}>
          <Field label="WACOG">
            <span data-testid="storage-wacog">{formatPrice(contract.wacog)}</span>
          </Field>
          <Field label="Cost of stock">{formatAmount(contract.stockCost)}</Field>
          <Field label="Unrealized (last EOD)">
            {unrealized === undefined ? "—" : formatAmount(unrealized)}
          </Field>
          <Field label="Max injection">{formatVolume(contract.maxInjectionPerDay)} MWh/day</Field>
          <Field label="Max withdrawal">{formatVolume(contract.maxWithdrawalPerDay)} MWh/day</Field>
        </SimpleGrid>
        {session.role === "BO" && <MovementForm contract={contract} />}
        <Group>
          <Button variant="subtle" size="xs" onClick={() => setShowLedger((v) => !v)}>
            {showLedger ? "Hide ledger" : "Show ledger"}
          </Button>
        </Group>
        {showLedger && <Ledger contractId={contract.id} />}
      </Stack>
    </Card>
  );
}

function RegisterForm() {
  const register = useRegisterStorage();
  const [facility, setFacility] = useState("");
  const [capacity, setCapacity] = useState<number | "">("");
  const [maxIn, setMaxIn] = useState<number | "">("");
  const [maxOut, setMaxOut] = useState<number | "">("");
  const [validFrom, setValidFrom] = useState("");
  const [validTo, setValidTo] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    if (
      !facility.trim() ||
      capacity === "" ||
      maxIn === "" ||
      maxOut === "" ||
      !validFrom ||
      !validTo
    ) {
      setError("Every field is required.");
      return;
    }
    try {
      await register.mutateAsync({
        facility: facility.trim(),
        commodityCode: "GAS",
        capacity,
        maxInjectionPerDay: maxIn,
        maxWithdrawalPerDay: maxOut,
        validFrom,
        validTo,
      });
      notifySuccess(`Storage contract at ${facility.trim()} registered.`);
      setFacility("");
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Paper withBorder p="md" component="form" onSubmit={submit}>
      <Group align="flex-end">
        <TextInput
          label="Facility"
          value={facility}
          onChange={(e) => setFacility(e.currentTarget.value)}
        />
        <NumberInput
          label="Capacity (MWh)"
          value={capacity}
          onChange={(v) => setCapacity(typeof v === "number" ? v : "")}
          w={150}
        />
        <NumberInput
          label="Max in / day"
          value={maxIn}
          onChange={(v) => setMaxIn(typeof v === "number" ? v : "")}
          w={130}
        />
        <NumberInput
          label="Max out / day"
          value={maxOut}
          onChange={(v) => setMaxOut(typeof v === "number" ? v : "")}
          w={130}
        />
        <TextInput
          type="date"
          label="Valid from"
          value={validFrom}
          onChange={(e) => setValidFrom(e.currentTarget.value)}
        />
        <TextInput
          type="date"
          label="Valid to"
          value={validTo}
          onChange={(e) => setValidTo(e.currentTarget.value)}
        />
        <Button type="submit" loading={register.isPending}>
          Register
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

// Gas in storage (ADR-0036): stock, its weighted average cost, and its
// unrealized P&L from the latest EOD run — shown next to positions on their
// own page, never summed with them. Every number is computed by the API.
export function StoragePage() {
  const session = useSession();
  const contracts = useStorageContracts();
  const valuation = useInventoryValuation(session.role !== "FO");
  const unrealizedOf = (id: string) =>
    valuation.data?.items.find((v) => v.storageContractId === id)?.unrealizedAmount;

  return (
    <Stack>
      <Title order={2}>Storage</Title>
      <Text size="sm" c="dimmed">
        Gas in storage at its weighted average cost (WACOG): an injection blends its price in, a
        withdrawal leaves the average unchanged. Unrealized P&amp;L comes from the end-of-day run,
        against the curve point of the run&apos;s month.
      </Text>
      {session.role === "BO" && <RegisterForm />}
      <QueryState
        isLoading={contracts.isLoading}
        error={contracts.error}
        onRetry={() => void contracts.refetch()}
      >
        {contracts.data?.length ? (
          contracts.data.map((c) => (
            <ContractCard key={c.id} contract={c} unrealized={unrealizedOf(c.id)} />
          ))
        ) : (
          <Text c="dimmed">No storage contracts in this tenant.</Text>
        )}
      </QueryState>
    </Stack>
  );
}
