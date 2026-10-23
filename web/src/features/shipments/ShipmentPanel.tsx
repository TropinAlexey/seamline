import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  NumberInput,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { useState, type FormEvent, type ReactNode } from "react";
import { useInvoices, type InvoiceEntry } from "../../api/queries/settlement";
import {
  useShipment,
  useShipmentStep,
  type ShipmentEntry,
  type TradeSummary,
} from "../../api/queries/trading";
import { useSession } from "../../auth/AuthContext";
import { errorMessage, notifySuccess } from "../../components/notify";
import { QueryState } from "../../components/QueryState";
import { formatDate } from "../../format/date";
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

export function shipmentStateLabel(state: ShipmentEntry["state"]): string {
  switch (state) {
    case "Nominated":
      return "Vessel nominated";
    case "Loaded":
      return "Loaded";
    case "QualityCertified":
      return "Quality certified";
    case "QualityRejected":
      return "Rejected on quality";
    default:
      return "Awaiting nomination";
  }
}

// Cargo trade, physical side (ADR-0034): contract terms, the shipment's
// progress, the one next step back office can record, and the
// provisional/final invoices. Every number arrives computed by the API —
// the tolerance band included — and is only formatted here (ADR-0030).
export function ShipmentPanel({ trade }: { trade: TradeSummary }) {
  const session = useSession();
  const shipment = useShipment(trade.id, true);
  const terms = trade.cargoTerms!;

  return (
    <Card withBorder data-testid="shipment-panel">
      <Group justify="space-between" mb="sm">
        <Title order={4}>Shipment</Title>
        {shipment.data && (
          <Badge
            variant="light"
            color={shipment.data.state === "QualityRejected" ? "red" : "blue"}
            data-testid="shipment-state"
          >
            {shipmentStateLabel(shipment.data.state)}
          </Badge>
        )}
      </Group>
      <QueryState isLoading={shipment.isLoading} error={shipment.error}>
        {shipment.data && (
          <Stack>
            <SimpleGrid cols={{ base: 2, md: 4 }}>
              <Field label="Incoterm">FOB</Field>
              <Field label="Tolerance band (seller's option)">
                {formatVolume(shipment.data.toleranceMin)} –{" "}
                {formatVolume(shipment.data.toleranceMax)} t ({terms.tolerancePct}%)
              </Field>
              <Field label="Laycan">
                {formatDate(terms.laycanFrom)} – {formatDate(terms.laycanTo)}
              </Field>
              <Field label="Contract quality">
                NAR {terms.baseNarKcalKg} kcal/kg (reject &lt; {terms.rejectBelowNarKcalKg}), S ≤{" "}
                {terms.maxSulphurPct}%, ash ≤ {terms.maxAshPct}%
              </Field>
              {shipment.data.vesselName && (
                <Field label="Vessel">
                  {shipment.data.vesselName} (IMO {shipment.data.imo}), ETA{" "}
                  {formatDate(shipment.data.eta!)}
                </Field>
              )}
              {shipment.data.blQuantity != null && (
                <Field label="Bill of lading">
                  <span data-testid="bl-quantity">{formatVolume(shipment.data.blQuantity)} t</span>,{" "}
                  {formatDate(shipment.data.blDate!)}
                </Field>
              )}
              {shipment.data.measuredNarKcalKg != null && (
                <Field label="Certificate of analysis">
                  NAR {shipment.data.measuredNarKcalKg}, S {shipment.data.sulphurPct}%, ash{" "}
                  {shipment.data.ashPct}%
                </Field>
              )}
            </SimpleGrid>

            {session.role === "BO" && trade.state !== "Draft" && (
              <NextStep
                tradeId={trade.id}
                shipment={shipment.data}
                active={trade.state === "Active"}
              />
            )}
            {session.role === "BO" && trade.state === "Delivered" && (
              <CargoInvoices
                tradeId={trade.id}
                awaitingFinal={shipment.data.state === "QualityCertified"}
              />
            )}
          </Stack>
        )}
      </QueryState>
    </Card>
  );
}

function NextStep({
  tradeId,
  shipment,
  active,
}: {
  tradeId: string;
  shipment: ShipmentEntry;
  active: boolean;
}) {
  const step = useShipmentStep(tradeId);
  const [error, setError] = useState<string | null>(null);
  const [vesselName, setVesselName] = useState("");
  const [imo, setImo] = useState("");
  const [date, setDate] = useState("");
  const [blQuantity, setBlQuantity] = useState<number | "">("");
  const [nar, setNar] = useState<number | "">("");
  const [sulphur, setSulphur] = useState<number | "">("");
  const [ash, setAsh] = useState<number | "">("");

  const submit = (run: () => Promise<unknown>, message: string) => async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      await run();
      notifySuccess(message);
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  let form: ReactNode = null;
  if (shipment.state == null && active) {
    form = (
      <form
        onSubmit={submit(
          () => step.mutateAsync({ step: "nominate", body: { vesselName, imo, eta: date } }),
          "Vessel nominated.",
        )}
      >
        <Group align="flex-end">
          <TextInput
            label="Vessel"
            value={vesselName}
            onChange={(e) => setVesselName(e.currentTarget.value)}
          />
          <TextInput
            label="IMO number"
            value={imo}
            onChange={(e) => setImo(e.currentTarget.value)}
            w={120}
          />
          <TextInput
            type="date"
            label="ETA"
            value={date}
            onChange={(e) => setDate(e.currentTarget.value)}
          />
          <Button type="submit" loading={step.isPending}>
            Nominate vessel
          </Button>
        </Group>
      </form>
    );
  } else if (shipment.state === "Nominated") {
    form = (
      <form
        onSubmit={submit(
          () =>
            step.mutateAsync({
              step: "load",
              body: { blQuantity: blQuantity === "" ? 0 : blQuantity, blDate: date },
            }),
          "Loading recorded — the trade is delivered and invoiced provisionally.",
        )}
      >
        <Group align="flex-end">
          <NumberInput
            label="B/L quantity (t)"
            value={blQuantity}
            onChange={(v) => setBlQuantity(typeof v === "number" ? v : "")}
            decimalScale={3}
            thousandSeparator=","
            w={180}
          />
          <TextInput
            type="date"
            label="B/L date"
            value={date}
            onChange={(e) => setDate(e.currentTarget.value)}
          />
          <Button type="submit" loading={step.isPending}>
            Record loading
          </Button>
        </Group>
      </form>
    );
  } else if (shipment.state === "Loaded") {
    const value = (v: number | "") => (v === "" ? 0 : v);
    form = (
      <form
        onSubmit={submit(
          () =>
            step.mutateAsync({
              step: "certify",
              body: {
                measuredNarKcalKg: value(nar),
                sulphurPct: value(sulphur),
                ashPct: value(ash),
              },
            }),
          "Certificate recorded.",
        )}
      >
        <Group align="flex-end">
          <NumberInput
            label="NAR (kcal/kg)"
            value={nar}
            onChange={(v) => setNar(typeof v === "number" ? v : "")}
            decimalScale={1}
            w={140}
          />
          <NumberInput
            label="Sulphur (%)"
            value={sulphur}
            onChange={(v) => setSulphur(typeof v === "number" ? v : "")}
            decimalScale={2}
            w={120}
          />
          <NumberInput
            label="Ash (%)"
            value={ash}
            onChange={(v) => setAsh(typeof v === "number" ? v : "")}
            decimalScale={2}
            w={120}
          />
          <Button type="submit" loading={step.isPending}>
            Record certificate
          </Button>
        </Group>
      </form>
    );
  }

  if (!form) return null;
  return (
    <Stack gap="xs">
      {form}
      {error && (
        <Alert color="red" role="alert">
          {error}
        </Alert>
      )}
    </Stack>
  );
}

function CargoInvoices({ tradeId, awaitingFinal }: { tradeId: string; awaitingFinal: boolean }) {
  const invoices = useInvoices(tradeId, { pollUntil: awaitingFinal ? "Final" : "Provisional" });
  const rows: InvoiceEntry[] = invoices.data ?? [];

  return (
    <Stack gap="xs">
      <Title order={5}>Invoices</Title>
      <Table data-testid="cargo-invoices">
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Kind</Table.Th>
            <Table.Th ta="right">Quantity (t)</Table.Th>
            <Table.Th ta="right">Unit price</Table.Th>
            <Table.Th ta="right">Amount</Table.Th>
            <Table.Th ta="right">Balance</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {[...rows].reverse().map((invoice) => (
            <Table.Tr key={invoice.kind} data-testid="cargo-invoice" data-kind={invoice.kind}>
              <Table.Td>{invoice.kind}</Table.Td>
              <Table.Td ta="right">{formatVolume(invoice.quantity)}</Table.Td>
              <Table.Td ta="right">{formatPrice(invoice.unitPrice)}</Table.Td>
              <Table.Td ta="right" data-testid="invoice-amount">
                {formatAmount(invoice.amount)}
              </Table.Td>
              <Table.Td ta="right" data-testid="invoice-balance">
                {invoice.balance == null ? "" : formatAmount(invoice.balance)}
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Stack>
  );
}
