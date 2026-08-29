# Archive — Modes TLS serveur de résultats

| Champ | Valeur |
|---|---|
| **Date** | 2026-08-29 |
| **Périmètre** | Déploiement `docker-compose`, Caddy, client sync WPF |
| **Baseline** | `system/baseline-v1.md` §2.9 |

## Contexte

Le serveur n’est pas toujours exposé sur Internet. Les déploiements LAN / datacenter fermé utilisent une PKI interne, un certificat auto-signé, ou parfois HTTP pur derrière un autre proxy.

## Livrables

- `RESULTS_TLS_MODE` : `letsencrypt` | `custom` | `internal` | `http`
- Caddyfiles dédiés : `server/caddy/*.Caddyfile`
- Répertoire `certs/` pour PEM (mode `custom`)
- `scripts/validate-results-env.sh`, `scripts/up-results.sh`
- `check-results-ssl.sh` adapté au mode
- Cookies Secure pilotés par `RESULTS_TLS_MODE` / `RESULTS_SECURE_COOKIES`
- Simulateur : `ResultsServerSkipTlsVerify` + conservation de `http://` dans l’URL

## Choix par mode

| Mode | Quand l’utiliser |
|---|---|
| `letsencrypt` | VPS public, DNS + ports 80/443 |
| `custom` | CA d’entreprise, certificat serveur signé par ta PKI |
| `internal` | PoC LAN, pas de PKI — HTTPS auto-signé Caddy |
| `http` | Réseau de confiance totale ou TLS terminé ailleurs |

## Client simulateur

- **PKI** : installer la CA racine Windows → pas d’option spéciale.
- **Auto-signé** : cocher « Ignorer les erreurs de certificat TLS » ou faire confiance au cert Caddy.
