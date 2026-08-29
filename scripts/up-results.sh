#!/usr/bin/env bash
# Valide .env puis démarre le stack résultats (docker / podman compose).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

"$ROOT/scripts/validate-results-env.sh"

if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  COMPOSE=(docker compose)
elif command -v podman >/dev/null 2>&1 && podman compose version >/dev/null 2>&1; then
  COMPOSE=(podman compose)
else
  echo "docker compose ou podman compose requis." >&2
  exit 1
fi

exec "${COMPOSE[@]}" up -d --build "$@"
