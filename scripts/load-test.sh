#!/usr/bin/env bash
set -euo pipefail

# Multi-tenant lifecycle load test with realistic chaos.
#
# Exercises the full trade pipeline across multiple tenants concurrently:
# counterparty → trade → submit → credit check → approve/reject →
# amend → deliver → invoice, with mid-run curve repricing (EOD simulation)
# and cross-tenant isolation verification.
#
# Usage:
#   ./scripts/load-test.sh                        # 100 trades/tenant, 3 tenants
#   ./scripts/load-test.sh --trades 1000           # 1000 trades/tenant
#   ./scripts/load-test.sh --tenants 5 --trades 500
#   ./scripts/load-test.sh --ramp                  # staircase: 10→100→1000→5000/tenant
#   ./scripts/load-test.sh --smoke                 # 1 trade, 1 tenant (CI)

BASE_URL="${BASE_URL:-http://localhost:5000}"
PGHOST="${PGHOST:-localhost}"
PGPORT="${PGPORT:-5432}"
PGUSER="${PGUSER:-seamline}"
PGPASSWORD="${PGPASSWORD:-seamline}"
PGDATABASE="${PGDATABASE:-seamline}"
export PGPASSWORD

TRADES_PER_TENANT=100
CONCURRENCY=10
NUM_TENANTS=3
SMOKE=false
RAMP=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --trades) TRADES_PER_TENANT="$2"; shift 2 ;;
    --concurrency) CONCURRENCY="$2"; shift 2 ;;
    --tenants) NUM_TENANTS="$2"; shift 2 ;;
    --smoke) SMOKE=true; TRADES_PER_TENANT=1; CONCURRENCY=1; NUM_TENANTS=1; shift ;;
    --ramp) RAMP=true; NUM_TENANTS=${NUM_TENANTS:-3}; shift ;;
    --url) BASE_URL="$2"; shift 2 ;;
    *) echo "Unknown arg: $1"; exit 1 ;;
  esac
done

TOTAL_TRADES=$((TRADES_PER_TENANT * NUM_TENANTS))
FAIL_FILE=$(mktemp)

echo "=== Seamline Multi-Tenant Load Test ==="
echo "  URL:              $BASE_URL"
echo "  Tenants:          $NUM_TENANTS"
echo "  Trades/tenant:    $TRADES_PER_TENANT"
echo "  Total trades:     $TOTAL_TRADES"
echo "  Concurrency:      $CONCURRENCY"
echo ""

# ---------- helpers ----------

api() {
  local method=$1 path=$2 token=$3; shift 3
  curl -s -X "$method" "$BASE_URL$path" \
    -H "Authorization: Bearer $token" \
    -H 'Content-Type: application/json' "$@" 2>/dev/null || echo ""
}

json_field() {
  python3 -c "import sys,json; print(json.load(sys.stdin)['$1'])"
}

json_len() {
  python3 -c "
import sys,json
data = sys.stdin.read().strip()
if not data: print(0)
else: print(len(json.loads(data)))
"
}

pbkdf2_hash() {
  local pw="$1"
  python3 -c "
import hashlib, os, base64, sys
salt = os.urandom(16)
h = hashlib.pbkdf2_hmac('sha256', sys.argv[1].encode(), salt, 100000, dklen=32)
print(f'100000.{base64.b64encode(salt).decode()}.{base64.b64encode(h).decode()}')
" "$pw"
}

# ---------- 1. Seed tenants ----------

TENANT_IDS=()
declare -A TRADER_TOKENS RISK_TOKENS BO_TOKENS

# Tenant 1 is always the pre-seeded demo tenant
TENANT_IDS+=("11111111-1111-1111-1111-111111111111")

echo "[1/9] Seeding $NUM_TENANTS tenants..."

if (( NUM_TENANTS > 1 )); then
  HASH=$(pbkdf2_hash "Demo-Password-123!")

  for i in $(seq 2 "$NUM_TENANTS"); do
    TID=$(python3 -c "import uuid; print(uuid.uuid4())")
    TENANT_IDS+=("$TID")

    psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -q -c "
      INSERT INTO identity.\"user\" (\"Id\", tenant_id, login, password_hash, role, created_at)
      VALUES
        (gen_random_uuid(), '$TID', 'trader',     '$HASH', 'FO', now()),
        (gen_random_uuid(), '$TID', 'risk',       '$HASH', 'MO', now()),
        (gen_random_uuid(), '$TID', 'backoffice', '$HASH', 'BO', now())
      ON CONFLICT (tenant_id, login) DO NOTHING;
    "
  done
fi
echo "  OK (${#TENANT_IDS[@]} tenants: ${TENANT_IDS[*]})"

# ---------- 2. Authenticate all personas ----------

echo "[2/9] Authenticating all personas..."

for TID in "${TENANT_IDS[@]}"; do
  TRADER_TOKENS[$TID]=$(curl -sf -X POST "$BASE_URL/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"tenantId\":\"$TID\",\"login\":\"trader\",\"password\":\"Demo-Password-123!\"}" \
    | json_field token)

  RISK_TOKENS[$TID]=$(curl -sf -X POST "$BASE_URL/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"tenantId\":\"$TID\",\"login\":\"risk\",\"password\":\"Demo-Password-123!\"}" \
    | json_field token)

  BO_TOKENS[$TID]=$(curl -sf -X POST "$BASE_URL/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"tenantId\":\"$TID\",\"login\":\"backoffice\",\"password\":\"Demo-Password-123!\"}" \
    | json_field token)
done
echo "  OK ($((NUM_TENANTS * 3)) tokens)"

# ---------- 3. Counterparties per tenant ----------

echo "[3/9] Creating counterparties..."
declare -A TENANT_CPS

COMPANY_NAMES=("EnergyTrade AG" "NordPool GmbH" "Alpine Power SA" "Centrica Plc" "Vattenfall AB")

for TID in "${TENANT_IDS[@]}"; do
  CPS=()
  for j in $(seq 0 4); do
    CPS+=($(api POST "/counterparties/" "${TRADER_TOKENS[$TID]}" \
      -d "{\"name\":\"${COMPANY_NAMES[$j]} (${TID:0:8})\",\"creditLimit\":$((500000 + RANDOM * 10))}" \
      | json_field id))
  done
  TENANT_CPS[$TID]="${CPS[*]}"
done
echo "  OK ($((NUM_TENANTS * 5)) counterparties)"

# ---------- 4. Initial curve points ----------

echo "[4/9] Publishing initial curve points..."
PERIODS=("2027-01" "2027-02" "2027-03" "2027-04" "2027-05" "2027-06" "2027-07" "2027-08" "2027-09" "2027-10" "2027-11" "2027-12")

for TID in "${TENANT_IDS[@]}"; do
  for period in "${PERIODS[@]}"; do
    api POST "/curve-points/" "${TRADER_TOKENS[$TID]}" \
      -d "{\"commodityCode\":\"POWER\",\"deliveryPeriod\":\"$period\",\"price\":$((40 + RANDOM % 30))}" > /dev/null
    api POST "/curve-points/" "${TRADER_TOKENS[$TID]}" \
      -d "{\"commodityCode\":\"GAS\",\"deliveryPeriod\":\"$period\",\"price\":$((20 + RANDOM % 25))}" > /dev/null
  done
done
echo "  OK ($((NUM_TENANTS * 24)) curve points: POWER + GAS × 12 months × $NUM_TENANTS tenants)"

# ---------- 5. Trade lifecycle (per-tenant worker) ----------

run_trade() {
  local tenant_idx=$1 trade_num=$2
  local tid=${ALL_TIDS[$tenant_idx]}
  local token=${ALL_TRADER_TOKENS[$tenant_idx]}
  local risk_token=${ALL_RISK_TOKENS[$tenant_idx]}

  IFS=' ' read -ra cps <<< "${ALL_CPS[$tenant_idx]}"
  local cp_id=${cps[$((trade_num % ${#cps[@]}))]}
  local commodity=$( (( trade_num % 2 == 0 )) && echo "POWER" || echo "GAS" )
  local direction=$( (( trade_num % 3 == 0 )) && echo "Sell" || echo "Buy" )
  local month=$(( (trade_num % 12) + 1 ))
  local period=$(printf "2027-%02d" "$month")
  local volume=$(( (RANDOM % 400) + 50 ))
  local price=$(( (RANDOM % 80) + 20 ))

  # Create trade
  local trade_id
  trade_id=$(curl -sf -X POST "$BASE_URL/trades/" \
    -H "Authorization: Bearer $token" \
    -H 'Content-Type: application/json' \
    -d "{\"commodityCode\":\"$commodity\",\"deliveryPeriod\":\"$period\",\"direction\":\"$direction\",\"volume\":$volume,\"price\":$price,\"counterpartyId\":\"$cp_id\"}" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])" 2>/dev/null) || { echo 1 >> "$FAIL_FILE"; return 0; }

  # Submit → credit check
  local state
  state=$(curl -sf -X POST "$BASE_URL/trades/$trade_id/submit" \
    -H "Authorization: Bearer $token" \
    | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('state',''))" 2>/dev/null) || return 0

  # If credit-pending, risk officer approves 80% / rejects 20%
  if [[ "$state" == "CreditPending" ]]; then
    sleep 0.2
    if (( trade_num % 5 != 0 )); then
      curl -sf -X POST "$BASE_URL/trades/$trade_id/approve" \
        -H "Authorization: Bearer $risk_token" > /dev/null 2>&1 || true
    else
      curl -sf -X POST "$BASE_URL/trades/$trade_id/reject" \
        -H "Authorization: Bearer $risk_token" > /dev/null 2>&1 || true
      return 0
    fi
  fi

  # Amend 30% of trades (price renegotiation)
  if (( trade_num % 3 == 1 )); then
    sleep 0.1
    curl -sf -X POST "$BASE_URL/trades/$trade_id/amend" \
      -H "Authorization: Bearer $token" \
      -H 'Content-Type: application/json' \
      -d "{\"volume\":$((volume + RANDOM % 50)),\"price\":$((price + RANDOM % 5)),\"reason\":\"Price renegotiation\"}" > /dev/null 2>&1 || true
  fi

  # Deliver 40% of trades
  if (( trade_num % 5 <= 1 )); then
    sleep 0.1
    curl -sf -X POST "$BASE_URL/trades/$trade_id/deliver" \
      -H "Authorization: Bearer $token" > /dev/null 2>&1 || true
  fi

  # Cancel 5% of trades (chaos)
  if (( trade_num % 20 == 13 )); then
    sleep 0.05
    curl -sf -X POST "$BASE_URL/trades/$trade_id/cancel" \
      -H "Authorization: Bearer $token" > /dev/null 2>&1 || true
  fi
}

export BASE_URL

# Flatten associative arrays into indexed arrays for export
ALL_TIDS=()
ALL_TRADER_TOKENS=()
ALL_RISK_TOKENS=()
ALL_CPS=()

for i in "${!TENANT_IDS[@]}"; do
  tid=${TENANT_IDS[$i]}
  ALL_TIDS+=("$tid")
  ALL_TRADER_TOKENS+=("${TRADER_TOKENS[$tid]}")
  ALL_RISK_TOKENS+=("${RISK_TOKENS[$tid]}")
  ALL_CPS+=("${TENANT_CPS[$tid]}")
done

# No export needed: run_trade runs as a forked subshell (&), not exec,
# so it inherits these arrays from the parent process directly.

# ---------- run_phase: execute N trades at concurrency C ----------

run_phase() {
  local trades_per_tenant=$1 concurrency=$2 label=$3
  local total=$((trades_per_tenant * NUM_TENANTS))

  echo "  [$label] $total trades ($trades_per_tenant/tenant × $NUM_TENANTS tenants, concurrency=$concurrency)..."
  local phase_start
  phase_start=$(date +%s)

  local work_file
  work_file=$(mktemp)
  for tenant_idx in $(seq 0 $((NUM_TENANTS - 1))); do
    for trade_num in $(seq 1 "$trades_per_tenant"); do
      echo "$tenant_idx $trade_num"
    done
  done | sort -R > "$work_file"

  # Throttle: keep at most $concurrency background jobs alive.
  # `wait -n` (bash 4.3+) waits for any single child; on older bash
  # (macOS default is 3.2) we fall back to `wait` (all children).
  local active=0
  while IFS=' ' read -r tidx tnum; do
    run_trade "$tidx" "$tnum" &
    active=$((active + 1))
    if (( active >= concurrency )); then
      if wait -n 2>/dev/null; then
        active=$((active - 1))
      else
        wait
        active=0
      fi
    fi
  done < "$work_file"
  wait
  rm -f "$work_file"

  local phase_end elapsed tps
  phase_end=$(date +%s)
  elapsed=$((phase_end - phase_start))
  tps=$(( total / (elapsed > 0 ? elapsed : 1) ))
  echo "    OK (${elapsed}s, ~$tps trades/s)"
}

# ---------- 5. Trade execution ----------

START=$(date +%s)

if [[ "$RAMP" == "true" ]]; then
  # Staircase (step-load) test: each phase increases trades ×10 and
  # concurrency ×2-3, with 15s cooldowns so Grafana renders clean steps.
  # The pattern reveals where the system transitions from linear scaling
  # to saturation (latency inflection, pool exhaustion, thread starvation).
  echo "[5/9] Staircase load test (10 → 100 → 1000 → 5000 trades/tenant)..."
  TOTAL_TRADES=0

  # Phase 1: baseline — everything should be instant, no resource pressure.
  # Establishes the "idle" floor on every Grafana panel.
  run_phase 10    5  "Phase 1: warm-up"
  TOTAL_TRADES=$((TOTAL_TRADES + 10 * NUM_TENANTS))
  echo "    Cooldown 15s..."
  sleep 15

  # Phase 2: normal business day — moderate concurrency, no contention expected.
  # Latency should stay flat; connection pool well below max.
  run_phase 100   15 "Phase 2: normal load"
  TOTAL_TRADES=$((TOTAL_TRADES + 100 * NUM_TENANTS))
  echo "    Cooldown 15s..."
  sleep 15

  # EOD repricing: simulate end-of-day market data feed — all curve points
  # updated for all tenants. In production this triggers MtM re-valuation
  # across the entire book; here it exercises the upsert path under load.
  echo "    EOD curve repricing (simulating market close)..."
  for TID in "${TENANT_IDS[@]}"; do
    for period in "${PERIODS[@]}"; do
      api POST "/curve-points/" "${TRADER_TOKENS[$TID]}" \
        -d "{\"commodityCode\":\"POWER\",\"deliveryPeriod\":\"$period\",\"price\":$((35 + RANDOM % 40))}" > /dev/null
      api POST "/curve-points/" "${TRADER_TOKENS[$TID]}" \
        -d "{\"commodityCode\":\"GAS\",\"deliveryPeriod\":\"$period\",\"price\":$((18 + RANDOM % 30))}" > /dev/null
    done
  done
  echo "    Cooldown 15s..."
  sleep 15

  # Phase 3: high load — p95 latency should start climbing; pool active
  # connections approach max. This is the "normal peak" a production
  # system would see on a volatile trading day.
  run_phase 1000  30 "Phase 3: high load"
  TOTAL_TRADES=$((TOTAL_TRADES + 1000 * NUM_TENANTS))

  # Realistic chaos: "valuation ran overnight, found pricing errors in POWER
  # curves, risk desk corrected them, valuation re-runs." This is a real
  # scenario — at DTEK we'd spend days re-running valuations after curve fixes.
  # Exercises the upsert idempotency of curve points under concurrent reads.
  echo "    Curve correction (simulating post-valuation pricing fix)..."
  for TID in "${TENANT_IDS[@]}"; do
    for period in "${PERIODS[@]}"; do
      api POST "/curve-points/" "${TRADER_TOKENS[$TID]}" \
        -d "{\"commodityCode\":\"POWER\",\"deliveryPeriod\":\"$period\",\"price\":$((50 + RANDOM % 20))}" > /dev/null
    done
  done
  echo "    Cooldown 15s..."
  sleep 15

  # Phase 4: stress to failure — push past saturation. Background curve
  # churn simulates live market movement during peak trading (intraday
  # price ticks arriving while traders book deals). Connection pool
  # exhaustion, 5xx errors, and exception spikes are EXPECTED here —
  # the point is to see the system degrade gracefully and recover.
  echo "  [Phase 4: stress + live market] Starting background curve churn..."
  (
    # ~30 random curve point updates over ~90s, hitting random tenants
    # and periods — simulates a live price feed or manual corrections
    # happening concurrently with heavy trade booking.
    for _ in $(seq 1 30); do
      sleep 3
      TID=${TENANT_IDS[$((RANDOM % NUM_TENANTS))]}
      period=${PERIODS[$((RANDOM % 12))]}
      commodity=$( (( RANDOM % 2 == 0 )) && echo "POWER" || echo "GAS" )
      api POST "/curve-points/" "${TRADER_TOKENS[$TID]}" \
        -d "{\"commodityCode\":\"$commodity\",\"deliveryPeriod\":\"$period\",\"price\":$((30 + RANDOM % 60))}" > /dev/null
    done
  ) &
  CHURN_PID=$!

  run_phase 5000  50 "Phase 4: stress + live market"
  TOTAL_TRADES=$((TOTAL_TRADES + 5000 * NUM_TENANTS))

  wait $CHURN_PID 2>/dev/null || true
else
  TOTAL_TRADES=$((TRADES_PER_TENANT * NUM_TENANTS))
  echo "[5/9] Running $TOTAL_TRADES trade lifecycles across $NUM_TENANTS tenants ($CONCURRENCY concurrent)..."
  run_phase "$TRADES_PER_TENANT" "$CONCURRENCY" "flat"

  # EOD curve repricing
  echo "[6/9] EOD curve repricing — simulating market close..."
  for TID in "${TENANT_IDS[@]}"; do
    for period in "${PERIODS[@]}"; do
      api POST "/curve-points/" "${TRADER_TOKENS[$TID]}" \
        -d "{\"commodityCode\":\"POWER\",\"deliveryPeriod\":\"$period\",\"price\":$((35 + RANDOM % 40))}" > /dev/null
      api POST "/curve-points/" "${TRADER_TOKENS[$TID]}" \
        -d "{\"commodityCode\":\"GAS\",\"deliveryPeriod\":\"$period\",\"price\":$((18 + RANDOM % 30))}" > /dev/null
    done
  done
  echo "  OK (prices updated — MtM positions will reflect new marks)"
fi

END=$(date +%s)
ELAPSED=$((END - START))

# ---------- 7. Verify positions per tenant ----------

echo "[7/9] Checking positions per tenant..."
TOTAL_POS=0
for TID in "${TENANT_IDS[@]}"; do
  POS=$(api GET "/positions" "${TRADER_TOKENS[$TID]}" | json_len)
  TOTAL_POS=$((TOTAL_POS + POS))
  echo "  Tenant ${TID:0:8}: $POS positions"
done

# ---------- 8. Verify invoices per tenant ----------

echo "[8/9] Checking invoices per tenant..."
sleep 2
TOTAL_INV=0
for TID in "${TENANT_IDS[@]}"; do
  INV=$(api GET "/invoices" "${BO_TOKENS[$TID]}" | json_len)
  TOTAL_INV=$((TOTAL_INV + INV))
  echo "  Tenant ${TID:0:8}: $INV invoices"
done

# ---------- 9. Cross-tenant isolation check ----------

echo "[9/9] Cross-tenant isolation verification..."
ISOLATION_OK=true

if (( NUM_TENANTS > 1 )); then
  # Tenant 1's trader should not see Tenant 2's positions
  T1_POS=$(api GET "/positions" "${TRADER_TOKENS[${TENANT_IDS[0]}]}" | json_len)
  T2_POS=$(api GET "/positions" "${TRADER_TOKENS[${TENANT_IDS[1]}]}" | json_len)

  T1_INV=$(api GET "/invoices" "${BO_TOKENS[${TENANT_IDS[0]}]}" | json_len)
  T2_INV=$(api GET "/invoices" "${BO_TOKENS[${TENANT_IDS[1]}]}" | json_len)

  # Equal counts don't prove a leak (positions = commodity × period, not 1:1 with trades).
  # Real check: tenant 1 must see fewer positions than the global total.
  if (( T1_POS > 0 && T1_POS == TOTAL_POS )); then
    echo "  FAIL: Tenant ${TENANT_IDS[0]:0:8} sees ALL $T1_POS positions — isolation leak!"
    ISOLATION_OK=false
  else
    echo "  OK (Tenant ${TENANT_IDS[0]:0:8}: $T1_POS pos / $T1_INV inv, Tenant ${TENANT_IDS[1]:0:8}: $T2_POS pos / $T2_INV inv)"
  fi
else
  echo "  SKIP (single tenant mode)"
fi

# ---------- Summary ----------

FAIL_COUNT=$(wc -l < "$FAIL_FILE" | tr -d ' ')
rm -f "$FAIL_FILE"

echo ""
echo "=== Summary ==="
echo "  Tenants:       $NUM_TENANTS"
echo "  Total trades:  $TOTAL_TRADES (${ELAPSED}s, ~$(( TOTAL_TRADES / (ELAPSED > 0 ? ELAPSED : 1) ))/s)"
echo "  Failed:        $FAIL_COUNT"
echo "  Positions:     $TOTAL_POS"
echo "  Invoices:      $TOTAL_INV"
echo "  Isolation:     $( [[ $ISOLATION_OK == true ]] && echo 'OK' || echo 'FAILED' )"

if [[ "$SMOKE" == "true" ]]; then
  if (( TOTAL_POS > 0 )); then
    echo "  Result:        PASS"
  else
    echo "  Result:        FAIL (no positions created)"
    exit 1
  fi
fi
