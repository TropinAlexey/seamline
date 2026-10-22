import { Text } from "@mantine/core";
import { formatCountdown } from "../../format/date";
import { useNow } from "../../components/useNow";

// The saga's approval timeout (TradeApprovalStateMachine: 5 minutes).
export const approvalTimeoutMs = 5 * 60 * 1000;

// Informational only: the saga decides when the timeout fires, and message
// redelivery can push it later than this shows.
export function ApprovalCountdown({ since }: { since: string }) {
  const now = useNow();
  const remaining = new Date(since).getTime() + approvalTimeoutMs - now;
  return (
    <Text size="sm" c={remaining <= 60_000 ? "red" : undefined} title="Auto-reject if no decision">
      {remaining > 0 ? `${formatCountdown(remaining)} to auto-reject` : "Auto-reject due"}
    </Text>
  );
}
