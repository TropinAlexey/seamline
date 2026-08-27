#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

CLUSTER_NAME="seamline"

IMAGES=(
  "local/seamline-api:src/Seamline.Api/Dockerfile"
  "local/seamline-valuation-worker:src/Seamline.Valuation.Worker/Dockerfile"
  "local/seamline-reporting-worker:src/Seamline.Reporting.Worker/Dockerfile"
  "local/seamline-acer-stub:src/Seamline.AcerStub/Dockerfile"
  "local/seamline-rabbitmq:docker/rabbitmq/Dockerfile"
)

# --- Detect runtime: k3d (macOS/Linux) or native k3s (Linux only) ---

if command -v k3d >/dev/null 2>&1; then
  RUNTIME="k3d"
elif command -v k3s >/dev/null 2>&1 || [ -f /usr/local/bin/k3s ]; then
  RUNTIME="k3s"
else
  echo "ERROR: neither k3d nor k3s found."
  echo "  macOS:  brew install k3d"
  echo "  Linux:  curl -sfL https://get.k3s.io | sh -"
  exit 1
fi

for cmd in docker kubectl helm; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "ERROR: $cmd not found. Install it first."; exit 1; }
done

echo "Runtime: $RUNTIME"

# --- Ensure cluster exists ---

if [ "$RUNTIME" = "k3d" ]; then
  if ! k3d cluster list 2>/dev/null | grep -q "$CLUSTER_NAME"; then
    echo "=== Creating k3d cluster ==="
    k3d cluster create "$CLUSTER_NAME" \
      --port "80:80@loadbalancer" \
      --port "443:443@loadbalancer" \
      --wait
  fi
  kubectl config use-context "k3d-$CLUSTER_NAME"
else
  export KUBECONFIG="${KUBECONFIG:-/etc/rancher/k3s/k3s.yaml}"
fi

# --- Build images ---

echo "=== Building Docker images ==="
for entry in "${IMAGES[@]}"; do
  tag="${entry%%:*}"
  dockerfile="${entry#*:}"
  echo "  → $tag"
  docker build -t "$tag" -f "$dockerfile" . --quiet
done

# --- Import images into cluster ---

echo "=== Importing images into cluster ==="
if [ "$RUNTIME" = "k3d" ]; then
  image_tags=()
  for entry in "${IMAGES[@]}"; do
    image_tags+=("${entry%%:*}")
  done
  k3d image import "${image_tags[@]}" -c "$CLUSTER_NAME"
else
  for entry in "${IMAGES[@]}"; do
    tag="${entry%%:*}"
    echo "  → $tag"
    docker save "$tag" | sudo k3s ctr images import -
  done
fi

# --- Deploy via Helm ---

echo "=== Installing Helm chart ==="
helm upgrade --install seamline ./k8s/seamline \
  -f ./k8s/seamline/values-local.yaml \
  --namespace seamline --create-namespace \
  --wait --timeout 5m

echo "=== Waiting for infrastructure pods ==="
kubectl wait --for=condition=ready pod -l app=seamline-postgres \
  -n seamline --timeout=120s
kubectl wait --for=condition=ready pod -l app=seamline-rabbitmq \
  -n seamline --timeout=120s

echo "=== Waiting for migration job ==="
kubectl wait --for=condition=complete job/seamline-migration \
  -n seamline --timeout=120s

echo "=== Waiting for application rollout ==="
for deploy in seamline-api seamline-valuation-worker seamline-reporting-worker seamline-acer-stub; do
  kubectl rollout status deployment/"$deploy" -n seamline --timeout=180s
done

echo "=== Waiting for observability rollout ==="
for deploy in otel-collector jaeger prometheus grafana kube-state-metrics; do
  kubectl rollout status deployment/"$deploy" -n seamline-observability --timeout=120s 2>/dev/null || true
done

echo ""
echo "=== Seamline k3s deployment ready ==="
echo ""
echo "Add to hosts file (if not already there):"
echo "  127.0.0.1 seamline.local grafana.seamline.local jaeger.seamline.local"
echo "  (macOS/Linux: /etc/hosts  |  Windows: C:\\Windows\\System32\\drivers\\etc\\hosts)"
echo ""
echo "URLs:"
echo "  API:     http://seamline.local"
echo "  Grafana: http://grafana.seamline.local  (admin/admin)"
echo "  Jaeger:  http://jaeger.seamline.local"
echo ""
echo "--- seamline namespace ---"
kubectl get pods -n seamline
echo ""
echo "--- seamline-observability namespace ---"
kubectl get pods -n seamline-observability
