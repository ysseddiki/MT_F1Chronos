# Variables d’environnement — serveur de résultats

Référence pour `docker-compose.yml` et exécution locale FastAPI.

Fichier modèle : [`.env.example`](../../.env.example) à la racine du dépôt.

---

## Application (`results`)

| Variable | Défaut | Description |
|---|---|---|
| `RESULTS_DATA` | `/data` (Docker) | Répertoire SQLite et données persistées |
| `RESULTS_SECRET` | aléatoire si absent | Secret signature cookies session — **fixer en prod** |
| `RESULTS_ADMIN_PASSWORD` | vide | Mot de passe admin initial (hashé au 1er boot) |
| `RESULTS_DOMAIN` | — | Nom d’hôte servi par Caddy (FQDN, LAN, IP) |
| `RESULTS_TLS_MODE` | `letsencrypt` | `letsencrypt` \| `custom` \| `internal` \| `http` |
| `RESULTS_SECURE_COOKIES` | auto | `true`/`false` — override cookies `Secure` |
| `RESULTS_STREAM_POLL` | `1` | Intervalle SSE interne (secondes) |
| `RESULTS_STREAM_MAX_AGE` | `300` | Durée max connexion SSE (secondes) |

Cookies `Secure` : activés si `RESULTS_DOMAIN` est défini **et** `RESULTS_TLS_MODE` ≠ `http` (sauf override).

---

## Caddy (`caddy`)

| Variable | Requis si | Description |
|---|---|---|
| `RESULTS_DOMAIN` | toujours | Bloc `host` du Caddyfile |
| `RESULTS_TLS_MODE` | toujours | Sélectionne `server/caddy/{mode}.Caddyfile` |
| `CADDY_EMAIL` | `letsencrypt` | Contact ACME Let's Encrypt |
| `RESULTS_TLS_CERT_DIR` | `custom` | Hôte monté sur `/certs` (défaut `./certs`) |

### Modes TLS

| Mode | Fichier | Certificat |
|---|---|---|
| `letsencrypt` | `server/caddy/letsencrypt.Caddyfile` | ACME auto (ports 80+443) |
| `custom` | `server/caddy/custom.Caddyfile` | `/certs/fullchain.pem` + `privkey.pem` |
| `internal` | `server/caddy/internal.Caddyfile` | Auto-signé Caddy |
| `http` | `server/caddy/http.Caddyfile` | Aucun TLS |

Scripts : `scripts/validate-results-env.sh`, `scripts/up-results.sh`, `scripts/check-results-ssl.sh`.

---

## Hors Docker (dev Python)

```bash
export RESULTS_DATA=/tmp/f1chronos-dev
export RESULTS_STREAM_POLL=0.02
export RESULTS_STREAM_MAX_AGE=0.1   # tests pytest
python3 -m uvicorn app.main:app --host 127.0.0.1 --port 8765
# depuis server/
```

Ne pas committer `RESULTS_DATA` local ni `.env`.
