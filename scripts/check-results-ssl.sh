#!/usr/bin/env bash
# Vérifie DNS, ports et TLS selon RESULTS_TLS_MODE.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$ROOT/.env"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Fichier .env introuvable. Lance : ./scripts/init-env.sh" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

"$ROOT/scripts/validate-results-env.sh"

DOMAIN="${RESULTS_DOMAIN}"
MODE="${RESULTS_TLS_MODE:-letsencrypt}"

echo ""
echo "=== DNS / résolution : $DOMAIN ==="
if command -v dig >/dev/null 2>&1; then
  dig +short A "$DOMAIN" || true
  dig +short AAAA "$DOMAIN" || true
else
  getent ahosts "$DOMAIN" || true
fi

case "$MODE" in
  letsencrypt)
    echo ""
    echo "=== HTTP :80 (ACME) ==="
    curl -sS -o /dev/null -w "HTTP %{http_code} → %{url_effective}\n" --max-time 10 "http://$DOMAIN/" \
      || echo "Échec HTTP (port 80 fermé ou Caddy arrêté ?)"

    echo ""
    echo "=== HTTPS :443 (Let's Encrypt) ==="
    URL="https://$DOMAIN/api/v1/health"
    ;;
  custom|internal)
    echo ""
    echo "=== HTTPS :443 ($MODE) ==="
    URL="https://$DOMAIN/api/v1/health"
    ;;
  http)
    echo ""
    echo "=== HTTP :80 ==="
    URL="http://$DOMAIN/api/v1/health"
    ;;
esac

if curl -sS -o /dev/null -w "API %{http_code}\n" --max-time 15 "$URL"; then
  echo "OK — API health joignable."
else
  echo "Échec. Vérifie :"
  echo "  1. docker compose ps  (caddy + results Up)"
  echo "  2. sudo ./scripts/setup-podman-ports.sh  (rootless)"
  echo "  3. RESULTS_TLS_MODE=$MODE RESULTS_DOMAIN=$DOMAIN"
  echo "  4. logs : docker compose logs caddy --tail 50"
  exit 1
fi

if [[ "$MODE" != "http" ]]; then
  echo ""
  echo "=== Certificat (openssl) ==="
  echo | openssl s_client -connect "$DOMAIN:443" -servername "$DOMAIN" 2>/dev/null \
    | openssl x509 -noout -subject -issuer -dates 2>/dev/null \
    || echo "(openssl indisponible ou TLS refusé)"
fi
