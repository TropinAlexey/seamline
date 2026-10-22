import { useEffect, useRef, useState } from "react";
import { useTrade, type TradeState } from "../../api/queries/trading";

export const sagaPollIntervalMs = 1000;
export const sagaPollTimeoutMs = 30_000;

export type SagaStatus = "idle" | "processing" | "stalled";

interface Wait {
  from: TradeState;
  startedAt: number;
  timedOut: boolean;
}

// The trade, plus targeted polling after a 202. The transition happens
// later, in a saga consumer, so poll every second until the state moves off
// the one we started from — for at most 30s, then stop and say so without
// calling it an error: MassTransit may simply still be retrying (spec §4.4).
export function useTradeWithSagaPolling(id: string, onResolved: () => void) {
  const [wait, setWait] = useState<Wait | null>(null);
  const [currentState, setCurrentState] = useState<TradeState | undefined>(undefined);

  const resolved = wait !== null && currentState !== undefined && currentState !== wait.from;
  const status: SagaStatus = !wait || resolved ? "idle" : wait.timedOut ? "stalled" : "processing";

  const trade = useTrade(id, {
    refetchInterval: status === "processing" ? sagaPollIntervalMs : false,
  });
  if (trade.data?.state !== currentState) setCurrentState(trade.data?.state);

  // Fire onResolved once per wait — it refreshes history and notifies, i.e.
  // it syncs things outside this hook's state.
  const notifiedFor = useRef<Wait | null>(null);
  useEffect(() => {
    if (resolved && notifiedFor.current !== wait) {
      notifiedFor.current = wait;
      onResolved();
    }
  }, [resolved, wait, onResolved]);

  useEffect(() => {
    if (status !== "processing" || !wait) return;
    const timeout = setTimeout(
      () => setWait((current) => (current === wait ? { ...wait, timedOut: true } : current)),
      Math.max(0, wait.startedAt + sagaPollTimeoutMs - Date.now()),
    );
    return () => clearTimeout(timeout);
  }, [status, wait]);

  return {
    trade,
    saga: {
      status,
      start(from: TradeState) {
        setWait({ from, startedAt: Date.now(), timedOut: false });
      },
    },
  };
}
