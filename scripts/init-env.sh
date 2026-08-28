#!/usr/bin/env bash
# Génère .env avec un mot de passe admin et un secret cookie aléatoires.
# Le mot de passe env n’est utilisé qu’au premier démarrage du serveur ;
# ensuite on le change dans /admin.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EXAMPLE="$ROOT/.env.example"
ENV_FILE="$ROOT/.env"

gen_secret() {
  if command -v python3 >/dev/null 2>&1; then
    python3 -c 'import secrets; print(secrets.token_urlsafe(32))'
    return
  fi
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 32
    return
  fi
  echo "python3 ou openssl est requis pour générer les secrets." >&2
  exit 1
}

set_kv() {
  local key="$1"
  local val="$2"
  local file="$3"
  local tmp
  tmp="$(mktemp)"
  awk -v k="$key" -v v="$val" '
    BEGIN { found = 0 }
    $0 ~ "^" k "=" {
      print k "=" v
      found = 1
      next
    }
    { print }
    END { if (!found) print k "=" v }
  ' "$file" > "$tmp"
  mv "$tmp" "$file"
}

if [[ ! -f "$EXAMPLE" ]]; then
  echo "Fichier introuvable : $EXAMPLE" >&2
  exit 1
fi

if [[ ! -f "$ENV_FILE" ]]; then
  cp "$EXAMPLE" "$ENV_FILE"
  echo "Créé $ENV_FILE à partir de .env.example"
else
  echo "Mise à jour de $ENV_FILE (RESULTS_DOMAIN / CADDY_EMAIL conservés)"
fi

ADMIN_PASSWORD="$(gen_secret)"
COOKIE_SECRET="$(gen_secret)"
set_kv RESULTS_ADMIN_PASSWORD "$ADMIN_PASSWORD" "$ENV_FILE"
set_kv RESULTS_SECRET "$COOKIE_SECRET" "$ENV_FILE"

cat <<EOF

.env prêt.

Mot de passe admin initial (à copier maintenant) :
  $ADMIN_PASSWORD

Après le premier docker compose up, ce mot de passe est hashé en base.
Le changer ensuite dans /admin — relancer ce script ne change plus le login.

Édite RESULTS_DOMAIN et CADDY_EMAIL dans .env avant de démarrer.

EOF
