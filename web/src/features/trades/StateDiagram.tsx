import type { TradeState } from "../../api/queries/trading";
import { nextStates } from "./allowedActions";

// Static picture of Trade.cs's state machine (spec §3.3). The current state
// is filled, states reachable in one step are outlined; everything else is
// muted. Submitted is dashed: a submit passes through it inside a single
// request, so a trade is never seen resting there.
const box = { w: 128, h: 38 };

const nodes: Record<TradeState, { x: number; y: number }> = {
  Draft: { x: 80, y: 60 },
  Submitted: { x: 260, y: 60 },
  CreditPending: { x: 440, y: 170 },
  Active: { x: 620, y: 60 },
  Delivered: { x: 800, y: 60 },
  Rejected: { x: 620, y: 260 },
  Cancelled: { x: 260, y: 260 },
};

const edges: { from: TradeState; to: TradeState; label: string }[] = [
  { from: "Draft", to: "Submitted", label: "submit" },
  { from: "Submitted", to: "Active", label: "within limit" },
  { from: "Submitted", to: "CreditPending", label: "limit breached" },
  { from: "CreditPending", to: "Active", label: "approve" },
  { from: "CreditPending", to: "Rejected", label: "reject / 5 min timeout" },
  { from: "Draft", to: "Cancelled", label: "cancel" },
  { from: "CreditPending", to: "Cancelled", label: "cancel" },
  { from: "Active", to: "Delivered", label: "deliver" },
];

// Where the segment from a's centre towards b leaves a's box.
function exitPoint(a: { x: number; y: number }, b: { x: number; y: number }) {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const scale = Math.min(
    dx === 0 ? Infinity : box.w / 2 / Math.abs(dx),
    dy === 0 ? Infinity : box.h / 2 / Math.abs(dy),
  );
  return { x: a.x + dx * scale, y: a.y + dy * scale };
}

export function StateDiagram({ current }: { current: TradeState }) {
  const reachable = new Set(nextStates[current]);

  return (
    <svg
      viewBox="0 0 880 300"
      role="img"
      aria-label={`Trade state machine, current state ${current}`}
      style={{ width: "100%", maxWidth: 880, height: "auto" }}
    >
      <defs>
        <marker
          id="arrow"
          viewBox="0 0 10 10"
          refX="9"
          refY="5"
          markerWidth="7"
          markerHeight="7"
          orient="auto-start-reverse"
        >
          <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--mantine-color-dimmed)" />
        </marker>
      </defs>

      {edges.map(({ from, to, label }) => {
        const start = exitPoint(nodes[from], nodes[to]);
        const end = exitPoint(nodes[to], nodes[from]);
        const live = from === current || (current === "Draft" && from === "Submitted");
        return (
          <g key={`${from}-${to}`}>
            <line
              x1={start.x}
              y1={start.y}
              x2={end.x}
              y2={end.y}
              stroke={
                live ? "var(--mantine-color-blue-filled)" : "var(--mantine-color-default-border)"
              }
              strokeWidth={live ? 2 : 1.5}
              markerEnd="url(#arrow)"
            />
            <text
              x={(start.x + end.x) / 2}
              y={(start.y + end.y) / 2 - 6}
              textAnchor="middle"
              fontSize="11"
              fill="var(--mantine-color-dimmed)"
            >
              {label}
            </text>
          </g>
        );
      })}

      {/* Amend keeps the trade Active and bumps its version. */}
      <path
        d={`M ${nodes.Active.x - 24} ${nodes.Active.y - box.h / 2} C ${nodes.Active.x - 30} 0, ${nodes.Active.x + 30} 0, ${nodes.Active.x + 24} ${nodes.Active.y - box.h / 2}`}
        fill="none"
        stroke={
          current === "Active"
            ? "var(--mantine-color-blue-filled)"
            : "var(--mantine-color-default-border)"
        }
        strokeWidth={1.5}
        markerEnd="url(#arrow)"
      />
      <text x={nodes.Active.x + 44} y={14} fontSize="11" fill="var(--mantine-color-dimmed)">
        amend
      </text>

      {(Object.keys(nodes) as TradeState[]).map((state) => {
        const { x, y } = nodes[state];
        const isCurrent = state === current;
        const isNext = reachable.has(state) && !isCurrent;
        return (
          <g key={state} data-state={state} data-current={isCurrent || undefined}>
            <rect
              x={x - box.w / 2}
              y={y - box.h / 2}
              width={box.w}
              height={box.h}
              rx={8}
              fill={isCurrent ? "var(--mantine-color-blue-filled)" : "var(--mantine-color-body)"}
              stroke={
                isCurrent || isNext
                  ? "var(--mantine-color-blue-filled)"
                  : "var(--mantine-color-default-border)"
              }
              strokeWidth={isNext ? 2 : 1.5}
              strokeDasharray={state === "Submitted" ? "5 4" : undefined}
            />
            <text
              x={x}
              y={y + 4}
              textAnchor="middle"
              fontSize="13"
              fontWeight={isCurrent ? 700 : 400}
              fill={isCurrent ? "var(--mantine-color-white)" : "var(--mantine-color-text)"}
            >
              {state}
            </text>
          </g>
        );
      })}
    </svg>
  );
}
