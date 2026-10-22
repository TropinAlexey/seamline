import { Alert, Button, Group, Modal, NumberInput, Stack, Textarea } from "@mantine/core";
import { useState, type FormEvent } from "react";
import { useAmendTrade, type TradeSummary } from "../../api/queries/trading";
import { errorMessage, notifySuccess } from "../../components/notify";

export function AmendModal({
  trade,
  opened,
  onClose,
}: {
  trade: TradeSummary;
  opened: boolean;
  onClose: () => void;
}) {
  const amend = useAmendTrade(trade.id);
  const [volume, setVolume] = useState<number | "">(trade.volume);
  const [price, setPrice] = useState<number | "">(trade.price);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    if (volume === "" || volume <= 0 || price === "" || price <= 0) {
      setError("Volume and price must be positive.");
      return;
    }
    if (!reason.trim()) {
      setError("A reason is required — it goes into the trade history.");
      return;
    }
    try {
      await amend.mutateAsync({ volume, price, reason: reason.trim() });
      notifySuccess("Trade amended — new version recorded.");
      setReason("");
      onClose();
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Modal opened={opened} onClose={onClose} title="Amend trade">
      <form onSubmit={submit}>
        <Stack>
          <Group grow>
            <NumberInput
              label="Volume"
              value={volume}
              onChange={(v) => setVolume(typeof v === "number" ? v : "")}
              decimalScale={3}
            />
            <NumberInput
              label="Price"
              value={price}
              onChange={(v) => setPrice(typeof v === "number" ? v : "")}
              decimalScale={4}
            />
          </Group>
          <Textarea
            label="Reason"
            value={reason}
            onChange={(e) => setReason(e.currentTarget.value)}
            required
          />
          {error && (
            <Alert color="red" role="alert">
              {error}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button type="submit" loading={amend.isPending}>
              Amend
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
