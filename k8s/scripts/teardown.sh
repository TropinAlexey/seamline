#!/usr/bin/env bash
set -euo pipefail

echo "Tearing down seamline k3s deployment..."
helm uninstall seamline --namespace seamline 2>/dev/null || true
kubectl delete namespace seamline seamline-observability --ignore-not-found
echo "Done. To remove k3s entirely: /usr/local/bin/k3s-uninstall.sh"
