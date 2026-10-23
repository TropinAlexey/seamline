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
import { useIndices } from "../../api/queries/marketData";
import { useCommodities, useCounterparties, useUnitOf } from "../../api/queries/reference";
import { runTradeAction, useCreateTrade, type TradeSummary } from "../../api/queries/trading";
import { errorMessage, notifySuccess } from "../../components/notify";

// Mirrors Trade.CreateDraft's guards so obvious mistakes never leave the
// browser; the server stays authoritative and its 400 text is shown inline.
const schema = z
  .object({
    commodityCode: z.string().trim().min(1, "Commodity is required").max(20),
    deliveryPeriod: z.string().regex(/^\d{4}-(0[1-9]|1[0-2])$/, "Pick a delivery month"),
    direction: z.enum(["Buy", "Sell"]),
    volume: z.number({ error: "Volume is required" }).positive("Volume must be positive"),
    pricingMode: z.enum(["Fixed", "Index"]),
    price: z.number().positive("Price must be positive").optional(),
    counterpartyId: z.string().min(1, "Counterparty is required"),
    // Cargo commodities only (ADR-0034); the server rejects them on flow ones.
    tolerancePct: z.number().min(0).max(20).optional(),
    laycanFrom: z.string().optional(),
    laycanTo: z.string().optional(),
    // Index pricing (ADR-0035), cargo commodities only as well.
    indexCode: z.string().optional(),
    windowFrom: z.string().optional(),
    windowTo: z.string().optional(),
    premium: z.number().optional(),
  })
  .superRefine((v, ctx) => {
    if (v.pricingMode === "Fixed" && v.price === undefined)
      ctx.addIssue({ code: "custom", path: ["price"], message: "Price is required" });
    if (v.pricingMode === "Index") {
      if (!v.indexCode)
        ctx.addIssue({ code: "custom", path: ["indexCode"], message: "Pick an index" });
      if (!v.windowFrom || !v.windowTo)
        ctx.addIssue({ code: "custom", path: ["windowTo"], message: "Pick the pricing window" });
    }
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
  const { data: indices } = useIndices();
  const [serverError, setServerError] = useState<string | null>(null);

  const { control, register, handleSubmit, reset, formState } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      commodityCode: "",
      deliveryPeriod: "",
      direction: "Buy",
      pricingMode: "Fixed",
      counterpartyId: "",
      premium: 0,
    },
  });

  // Volume and price are in the selected commodity's unit (ADR-0033).
  const commodityCode = useWatch({ control, name: "commodityCode" });
  const unit = unitOf(commodityCode) ?? "unit";
  const isCargo = commodities?.find((c) => c.code === commodityCode)?.deliveryModel === "Cargo";
  const pricingMode = useWatch({ control, name: "pricingMode" });
  const indexPriced = isCargo && pricingMode === "Index";

  const submit = (andSubmit: boolean) =>
    handleSubmit(async (values) => {
      setServerError(null);
      try {
        const {
          tolerancePct,
          laycanFrom,
          laycanTo,
          indexCode,
          windowFrom,
          windowTo,
          premium,
          price,
        } = values;
        const { id } = await createTrade.mutateAsync({
          commodityCode: values.commodityCode,
          deliveryPeriod: values.deliveryPeriod,
          direction: values.direction,
          volume: values.volume,
          counterpartyId: values.counterpartyId,
          price: indexPriced ? null : (price ?? null),
          indexPricing: indexPriced
            ? {
                indexCode: indexCode ?? "",
                windowFrom: windowFrom ?? "",
                windowTo: windowTo ?? "",
                premium: premium ?? 0,
              }
            : null,
          cargoTerms: isCargo
            ? {
                tolerancePct: tolerancePct ?? 0,
                laycanFrom: laycanFrom ?? "",
                laycanTo: laycanTo ?? "",
              }
            : null,
        });
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
          {isCargo && (
            <Controller
              control={control}
              name="pricingMode"
              render={({ field }) => (
                <SegmentedControl
                  aria-label="Pricing"
                  data={[
                    { value: "Fixed", label: "Fixed price" },
                    { value: "Index", label: "Index average" },
                  ]}
                  value={field.value}
                  onChange={field.onChange}
                />
              )}
            />
          )}
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
            {!indexPriced && (
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
            )}
          </Group>
          {indexPriced && (
            <Group grow align="flex-start">
              <Controller
                control={control}
                name="indexCode"
                render={({ field, fieldState }) => (
                  <Select
                    label="Index"
                    data={(indices ?? [])
                      .filter((i) => i.commodityCode === commodityCode)
                      .map((i) => ({ value: i.code, label: i.code }))}
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v ?? undefined)}
                    error={fieldState.error?.message}
                  />
                )}
              />
              <TextInput type="date" label="Window from" {...register("windowFrom")} />
              <TextInput
                type="date"
                label="Window to"
                {...register("windowTo")}
                error={formState.errors.windowTo?.message}
              />
              <Controller
                control={control}
                name="premium"
                render={({ field }) => (
                  <NumberInput
                    label={`Premium (EUR/${unit})`}
                    description="May be negative"
                    value={field.value ?? ""}
                    onChange={(v) => field.onChange(typeof v === "number" ? v : undefined)}
                    decimalScale={4}
                  />
                )}
              />
            </Group>
          )}
          {isCargo && (
            <Group grow align="flex-start">
              <Controller
                control={control}
                name="tolerancePct"
                render={({ field, fieldState }) => (
                  <NumberInput
                    label="Tolerance (%)"
                    description="Seller's option, FOB"
                    value={field.value ?? ""}
                    onChange={(v) => field.onChange(typeof v === "number" ? v : undefined)}
                    decimalScale={2}
                    error={fieldState.error?.message}
                  />
                )}
              />
              <TextInput type="date" label="Laycan from" {...register("laycanFrom")} />
              <TextInput type="date" label="Laycan to" {...register("laycanTo")} />
            </Group>
          )}
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
