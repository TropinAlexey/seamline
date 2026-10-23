import {
  Alert,
  Button,
  Card,
  Group,
  Progress,
  SimpleGrid,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { useState, type ReactNode } from "react";
import { useUnitOf } from "../../api/queries/reference";
import { useFixPrice, useTradePricing, type TradeSummary } from "../../api/queries/trading";
import { useSession } from "../../auth/AuthContext";
import { errorMessage, notifySuccess } from "../../components/notify";
import { QueryState } from "../../components/QueryState";
import { formatDate } from "../../format/date";
import { formatPrice, formatVolume } from "../../format/money";

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

// Index-priced trade (ADR-0035): k of N pricing days fixed, the expected
// price against today's forward, fixed vs unfixed quantity. Risk computes
// every number; back office confirms the final price once the window has
// fully fixed (ADR-0038) — until then the formula's final price shows as
// "to confirm".
export function PricingPanel({ trade }: { trade: TradeSummary }) {
  const session = useSession();
  const unitOf = useUnitOf();
  const pricing = useTradePricing(
    trade.id,
    trade.state === "Active" || trade.state === "Delivered",
  );
  const fixPrice = useFixPrice(trade.id);
  const [error, setError] = useState<string | null>(null);
  const terms = trade.pricing!;
  const unit = unitOf(trade.commodityCode) ?? "";

  const confirm = async () => {
    setError(null);
    try {
      const fixed = await fixPrice.mutateAsync();
      notifySuccess(`Final price ${formatPrice(fixed.price)} written to the trade.`, "Price fixed");
      await pricing.refetch();
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Card withBorder data-testid="pricing-panel">
      <Stack gap="sm">
        <Group justify="space-between">
          <Title order={4}>Pricing</Title>
          <Text size="sm" c="dimmed">
            Average of {terms.indexCode}, {formatDate(terms.windowFrom)} –{" "}
            {formatDate(terms.windowTo)}, premium {formatPrice(terms.premium)}
          </Text>
        </Group>
        {trade.state !== "Active" && trade.state !== "Delivered" ? (
          <Text size="sm" c="dimmed">
            The pricing view starts when the trade is active.
          </Text>
        ) : (
          <QueryState isLoading={pricing.isLoading} error={pricing.error}>
            {pricing.data && (
              <>
                <div>
                  <Group justify="space-between" mb={4}>
                    <Text size="sm" data-testid="pricing-progress">
                      {pricing.data.fixedDays} of {pricing.data.pricingDays} pricing days fixed
                    </Text>
                    {pricing.data.confirmedPrice != null && (
                      <Text size="sm" c="green" fw={600}>
                        Price fixed
                      </Text>
                    )}
                  </Group>
                  <Progress
                    value={(pricing.data.fixedDays / pricing.data.pricingDays) * 100}
                    aria-label="Pricing days fixed"
                  />
                </div>
                <SimpleGrid cols={{ base: 2, md: 6 }}>
                  <Field label="Average fixing">{formatPrice(pricing.data.averageFixing)}</Field>
                  <Field label="Forward">{formatPrice(pricing.data.forward)}</Field>
                  <Field label="Expected price">
                    <span data-testid="pricing-expected">
                      {formatPrice(pricing.data.expectedPrice)}
                    </span>
                  </Field>
                  <Field label={`Fixed (${unit})`}>
                    {formatVolume(pricing.data.fixedQuantity)}
                  </Field>
                  <Field label={`Unfixed (${unit})`}>
                    {formatVolume(pricing.data.unfixedQuantity)}
                  </Field>
                  <Field label="Final price">
                    <span data-testid="pricing-final">
                      {formatPrice(pricing.data.confirmedPrice ?? pricing.data.formulaFinalPrice)}
                    </span>
                    {pricing.data.confirmedPrice == null &&
                      pricing.data.formulaFinalPrice != null && (
                        <Text span size="xs" c="dimmed" ml={4}>
                          to confirm
                        </Text>
                      )}
                  </Field>
                </SimpleGrid>
                {session.role === "BO" &&
                  pricing.data.formulaFinalPrice != null &&
                  pricing.data.confirmedPrice == null && (
                    <Group>
                      <Button onClick={confirm} loading={fixPrice.isPending}>
                        Fix price at {formatPrice(pricing.data.formulaFinalPrice)}
                      </Button>
                    </Group>
                  )}
                {error && (
                  <Alert color="red" role="alert">
                    {error}
                  </Alert>
                )}
              </>
            )}
          </QueryState>
        )}
      </Stack>
    </Card>
  );
}
