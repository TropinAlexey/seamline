#!/usr/bin/env bash
set -euo pipefail

echo "Tearing down seamline k3s deployment..."
helm uninstall seamline --namespace seamline 2>/dev/null || true
kubectl delete namespace seamline seamline-observability --ignore-not-found

if command -v k3d >/dev/null 2>&1; then
  echo "To remove the cluster entirely: k3d cluster delete seamline"
else
  echo "To remove k3s entirely: /usr/local/bin/k3s-uninstall.sh"
fi
echo "Done."
