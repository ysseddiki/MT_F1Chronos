#!/usr/bin/env bash
# Vérifie DNS, ports et certificat Let's Encrypt avant/après déploiement.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$ROOT/.env"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Fichier .env introuvable. Lance : ./scripts/init-env.sh" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

DOMAIN="${RESULTS_DOMAIN:-}"
if [[ -z "$DOMAIN" || "$DOMAIN" == "localhost" ]]; then
  echo "RESULTS_DOMAIN invalide dans .env : « $DOMAIN »" >&2
  echo "Mets ton hostname public, ex. simracing-dc.yseddiki.fr" >&2
  exit 1
fi

echo "=== DNS : $DOMAIN ==="
if command -v dig >/dev/null 2>&1; then
  dig +short A "$DOMAIN" || true
  dig +short AAAA "$DOMAIN" || true
else
  getent ahosts "$DOMAIN" || true
fi

echo ""
echo "=== HTTP :80 (ACME + redirect) ==="
curl -sS -o /dev/null -w "HTTP %{http_code} → %{url_effective}\n" --max-time 10 "http://$DOMAIN/" || echo "Échec HTTP (port 80 fermé ou Caddy arrêté ?)"

echo ""
echo "=== HTTPS :443 (Let's Encrypt) ==="
if curl -sS -o /dev/null -w "HTTPS %{http_code}\n" --max-time 15 "https://$DOMAIN/api/v1/health"; then
  echo "OK — TLS et API health."
else
  echo "Échec HTTPS. Vérifie :"
  echo "  1. podman compose ps  (caddy + results Up)"
  echo "  2. sudo ./scripts/setup-podman-ports.sh  (rootless)"
  echo "  3. pare-feu : 80 et 443 ouverts"
  echo "  4. RESULTS_DOMAIN=$DOMAIN dans .env"
  echo "  5. logs : podman compose logs caddy --tail 50"
  exit 1
fi

echo ""
echo "=== Certificat (openssl) ==="
echo | openssl s_client -connect "$DOMAIN:443" -servername "$DOMAIN" 2>/dev/null | openssl x509 -noout -subject -issuer -dates 2>/dev/null || echo "(openssl indisponible)"
