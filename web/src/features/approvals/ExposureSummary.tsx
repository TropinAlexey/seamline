import { Stack, Text } from "@mantine/core";
import { useExposure } from "../../api/queries/risk";
import { formatAmount } from "../../format/money";

// The four-eyes context (spec §4.5): limit, current exposure (computed by
// Risk exactly as the credit check does) and this trade's notional — all
// server numbers, shown side by side, never combined here (ADR-0030).
export function ExposureSummary({
  counterpartyId,
  notional,
}: {
  counterpartyId: string;
  notional: number;
}) {
  const exposure = useExposure(counterpartyId);
  return (
    <Stack gap={0} data-testid="exposure-summary">
      {exposure.data && (
        <Text size="xs">
          Exposure {formatAmount(exposure.data.currentExposure)} / limit{" "}
          {formatAmount(exposure.data.creditLimit)}
        </Text>
      )}
      <Text size="xs" c="dimmed">
        This trade&apos;s notional {formatAmount(notional)}
      </Text>
    </Stack>
  );
}
