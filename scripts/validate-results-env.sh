#!/usr/bin/env bash
# Vérifie .env avant démarrage du stack résultats (TLS, certificats, domaine).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$ROOT/.env"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Fichier .env introuvable. Lance : ./scripts/init-env.sh" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

MODE="${RESULTS_TLS_MODE:-letsencrypt}"
DOMAIN="${RESULTS_DOMAIN:-}"

if [[ -z "$DOMAIN" ]]; then
  echo "RESULTS_DOMAIN est obligatoire dans .env" >&2
  exit 1
fi

case "$MODE" in
  letsencrypt)
    if [[ -z "${CADDY_EMAIL:-}" ]]; then
      echo "RESULTS_TLS_MODE=letsencrypt requiert CADDY_EMAIL dans .env" >&2
      exit 1
    fi
    if [[ "$DOMAIN" == "localhost" ]]; then
      echo "Let's Encrypt refuse localhost — utilise RESULTS_TLS_MODE=internal ou http en LAN." >&2
      exit 1
    fi
    CADDYFILE="$ROOT/server/caddy/letsencrypt.Caddyfile"
    ;;
  custom)
    CERT_DIR="${RESULTS_TLS_CERT_DIR:-$ROOT/certs}"
    if [[ ! -f "$CERT_DIR/fullchain.pem" || ! -f "$CERT_DIR/privkey.pem" ]]; then
      echo "Mode custom : place fullchain.pem et privkey.pem dans $CERT_DIR" >&2
      exit 1
    fi
    CADDYFILE="$ROOT/server/caddy/custom.Caddyfile"
    ;;
  internal)
    CADDYFILE="$ROOT/server/caddy/internal.Caddyfile"
    ;;
  http)
    CADDYFILE="$ROOT/server/caddy/http.Caddyfile"
    ;;
  *)
    echo "RESULTS_TLS_MODE invalide : $MODE (letsencrypt | custom | internal | http)" >&2
    exit 1
    ;;
esac

if [[ ! -f "$CADDYFILE" ]]; then
  echo "Caddyfile introuvable : $CADDYFILE" >&2
  exit 1
fi

echo "TLS=$MODE domain=$DOMAIN caddyfile=$CADDYFILE"
