import { zodResolver } from "@hookform/resolvers/zod";
import {
  Alert,
  Button,
  Group,
  Modal,
  NumberInput,
  SegmentedControl,
  Select,
  Stack,
  TextInput,
} from "@mantine/core";
import { useState } from "react";
import { Controller, useForm, useWatch } from "react-hook-form";
import { useNavigate } from "react-router";
import { z } from "zod";
import { useCommodities, useCounterparties, useUnitOf } from "../../api/queries/reference";
import { runTradeAction, useCreateTrade, type TradeSummary } from "../../api/queries/trading";
import { errorMessage, notifySuccess } from "../../components/notify";

// Mirrors Trade.CreateDraft's guards so obvious mistakes never leave the
// browser; the server stays authoritative and its 400 text is shown inline.
const schema = z.object({
  commodityCode: z.string().trim().min(1, "Commodity is required").max(20),
  deliveryPeriod: z.string().regex(/^\d{4}-(0[1-9]|1[0-2])$/, "Pick a delivery month"),
  direction: z.enum(["Buy", "Sell"]),
  volume: z.number({ error: "Volume is required" }).positive("Volume must be positive"),
  price: z.number({ error: "Price is required" }).positive("Price must be positive"),
  counterpartyId: z.string().min(1, "Counterparty is required"),
});

type FormValues = z.infer<typeof schema>;

function submitOutcomeMessage(state: TradeSummary["state"]): string {
  return state === "Active"
    ? "Activated — within credit limit."
    : "Pending risk approval — credit limit exceeded.";
}

export function NewTradeModal({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const navigate = useNavigate();
  const createTrade = useCreateTrade();
  const { data: commodities } = useCommodities();
  const unitOf = useUnitOf();
  const { data: counterparties } = useCounterparties();
  const [serverError, setServerError] = useState<string | null>(null);

  const { control, register, handleSubmit, reset, formState } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { commodityCode: "", deliveryPeriod: "", direction: "Buy", counterpartyId: "" },
  });

  // Volume and price are in the selected commodity's unit (ADR-0033).
  const unit = unitOf(useWatch({ control, name: "commodityCode" })) ?? "unit";

  const submit = (andSubmit: boolean) =>
    handleSubmit(async (values) => {
      setServerError(null);
      try {
        const { id } = await createTrade.mutateAsync(values);
        if (andSubmit) {
          const { data } = await runTradeAction(id, "submit");
          notifySuccess(
            submitOutcomeMessage((data as { state: TradeSummary["state"] }).state),
            "Trade submitted",
          );
        } else {
          notifySuccess("Draft created.");
        }
        reset();
        onClose();
        navigate(`/trades/${id}`);
      } catch (e) {
        setServerError(errorMessage(e));
      }
    });

  return (
    <Modal opened={opened} onClose={onClose} title="New trade" size="lg">
      <form onSubmit={submit(false)}>
        <Stack>
          <Controller
            control={control}
            name="commodityCode"
            render={({ field, fieldState }) => (
              <Select
                label="Commodity"
                data={(commodities ?? []).map((c) => ({
                  value: c.code,
                  label: `${c.code} — ${c.name}`,
                }))}
                value={field.value || null}
                onChange={(v) => field.onChange(v ?? "")}
                searchable
                error={fieldState.error?.message}
              />
            )}
          />
          <TextInput
            type="month"
            label="Delivery period"
            {...register("deliveryPeriod")}
            error={formState.errors.deliveryPeriod?.message}
          />
          <Controller
            control={control}
            name="direction"
            render={({ field }) => (
              <SegmentedControl
                aria-label="Direction"
                data={["Buy", "Sell"]}
                value={field.value}
                onChange={field.onChange}
              />
            )}
          />
          <Group grow>
            <Controller
              control={control}
              name="volume"
              render={({ field, fieldState }) => (
                <NumberInput
                  label={`Volume (${unit})`}
                  value={field.value ?? ""}
                  onChange={(v) => field.onChange(typeof v === "number" ? v : undefined)}
                  decimalScale={3}
                  error={fieldState.error?.message}
                />
              )}
            />
            <Controller
              control={control}
              name="price"
              render={({ field, fieldState }) => (
                <NumberInput
                  label={`Price (EUR/${unit})`}
                  value={field.value ?? ""}
                  onChange={(v) => field.onChange(typeof v === "number" ? v : undefined)}
                  decimalScale={4}
                  error={fieldState.error?.message}
                />
              )}
            />
          </Group>
          <Controller
            control={control}
            name="counterpartyId"
            render={({ field, fieldState }) => (
              <Select
                label="Counterparty"
                data={(counterparties ?? []).map((c) => ({ value: c.id, label: c.name }))}
                value={field.value || null}
                onChange={(v) => field.onChange(v ?? "")}
                searchable
                error={fieldState.error?.message}
                nothingFoundMessage="No counterparties — create one first"
              />
            )}
          />
          {serverError && (
            <Alert color="red" role="alert">
              {serverError}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button type="submit" variant="default" loading={formState.isSubmitting}>
              Create
            </Button>
            <Button type="button" onClick={submit(true)} loading={formState.isSubmitting}>
              Create &amp; submit
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
