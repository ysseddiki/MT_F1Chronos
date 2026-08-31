# Reprise du projet sur une nouvelle machine

Guide pour cloner, configurer, builder et vérifier **F1 Chronos** (`MT_F1Chronos`) depuis zéro.

| Champ | Valeur |
|---|---|
| **Dépôt** | `MT_F1Chronos` |
| **Branche de référence** | `main` |
| **Dernière mise à jour** | 2026-08-31 |

---

## 1. Vue d’ensemble

Le dépôt contient **deux produits** indépendants :

| Composant | OS | Techno | Obligatoire |
|---|---|---|---|
| **Overlay simulateur** | Windows 10/11 | .NET 8 + WPF | Cœur produit |
| **Serveur de résultats** | Linux (Docker/Podman) | FastAPI + SQLite + Caddy | Optionnel (archive web) |

L’overlay fonctionne **sans réseau**. Le serveur est une vitrine / archive : le simulateur initie toujours HTTP (NAT).

Documentation complémentaire :

- [`system/baseline-v1.md`](system/baseline-v1.md) — contrats, modèles, règles métier
- [`README.md`](../README.md) — guide utilisateur overlay
- [`AGENTS.md`](../AGENTS.md) — conventions pour agents IA

---

## 2. Prérequis par plateforme

### 2.1 Développement overlay (Windows)

| Outil | Version | Usage |
|---|---|---|
| Windows | 10/11 | WPF requis |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | 8.x | Build + tests |
| Git | récent | Clone |
| EA Sports F1 25/26 | — | Test UDP (optionnel en dev) |

### 2.2 Serveur de résultats (Linux / macOS pour dev)

| Outil | Version | Usage |
|---|---|---|
| Docker **ou** Podman + compose | récent | Stack prod-like |
| Python | 3.9+ (3.12 en prod Docker) | Tests unitaires serveur hors Docker |
| Git | récent | Clone |

> Sur **macOS**, le build WPF échoue (pas de `net8.0-windows`) — normal. Travailler sur `server/` et les specs.

---

## 3. Clone et structure du dépôt

```bash
git clone <url-du-depot> MT_F1Chronos
cd MT_F1Chronos
```

```
MT_F1Chronos/
├── src/MT_F1Chronos.Core/      # Domaine, UDP, stores, sync (net8.0)
├── src/MT_F1Chronos.App/       # WPF overlay (net8.0-windows)
├── tests/MT_F1Chronos.Tests/   # xUnit (Core uniquement)
├── server/                     # FastAPI + SPA + tests pytest
├── specs/                      # OpenSpec (baseline, archives, ce guide)
├── scripts/                    # init-env, up-results, TLS, Podman
├── docker-compose.yml
├── .env.example                # Modèle serveur (copier → .env)
├── build.ps1                   # Release Windows → dist/
└── MT_F1Chronos.sln
```

---

## 4. Overlay Windows — premier build

```powershell
cd MT_F1Chronos
dotnet build MT_F1Chronos.sln -c Release
dotnet test tests/MT_F1Chronos.Tests/MT_F1Chronos.Tests.csproj
.\build.ps1
```

Sortie : `dist\MT_F1Chronos.exe` (+ raccourcis Bureau / Démarrage si `build.ps1`).

### Données locales overlay (hors dépôt)

Tout est sous `%LOCALAPPDATA%\MT_F1Chronos\` :

| Fichier / dossier | Contenu |
|---|---|
| `settings.json` | Réglages (UDP, overlay, serveur optionnel) |
| `admin.secret.json` | Hash mot de passe admin local |
| `sessions/track-{id}.json` | Scores globaux |
| `contests/index.json` | Métadonnées concours |
| `contests/{id}/track-{id}.json` | Scores par concours |

**Ne jamais committer** ces fichiers ni les secrets serveur.

### Configuration jeu (test)

| Paramètre F1 | Valeur |
|---|---|
| UDP Telemetry | On |
| IP | `127.0.0.1` |
| Port | `20888` |
| Format | `2025` ou `2026` (identique à `settings.json` → `udpFormat`) |

---

## 5. Serveur de résultats — premier démarrage

### 5.1 Fichiers à créer localement

```bash
cp .env.example .env
./scripts/init-env.sh    # génère RESULTS_ADMIN_PASSWORD + RESULTS_SECRET
```

Éditer `.env` :

| Variable | Description |
|---|---|
| `RESULTS_DOMAIN` | FQDN public, nom LAN ou IP |
| `RESULTS_TLS_MODE` | `letsencrypt` \| `custom` \| `internal` \| `http` |
| `CADDY_EMAIL` | Obligatoire si `letsencrypt` |
| `RESULTS_ADMIN_PASSWORD` | Seed premier admin (puis hashé en SQLite) |
| `RESULTS_SECRET` | Signature cookies session (stable en prod) |

Voir [`server/env.md`](server/env.md) pour le détail.

### 5.2 Lancer le stack

```bash
./scripts/validate-results-env.sh   # vérifie domaine, certs, mode TLS
./scripts/up-results.sh             # docker compose up -d --build
./scripts/check-results-ssl.sh      # health selon le mode TLS
```

Podman rootless (une fois) : `sudo ./scripts/setup-podman-ports.sh`

### 5.3 Données serveur (hors dépôt)

| Emplacement | Contenu |
|---|---|
| Volume Docker `results-data` | SQLite `results.sqlite` sous `/data` |
| `./certs/*.pem` | Certificats mode `custom` (gitignored) |
| `server/data-smoke/` | Dev local éventuel (gitignored) |

### 5.4 Tests serveur sans Docker

```bash
cd server
python3 -m venv .venv && source .venv/bin/activate   # optionnel
pip install -r requirements.txt
python3 -m pytest tests/ -q
```

Les tests utilisent `tmp_path` — pas de DB persistante.

### 5.5 Première utilisation web

1. Ouvrir `https://<RESULTS_DOMAIN>/admin` (ou `http://` en mode `http`)
2. Se connecter avec le mot de passe généré par `init-env.sh`
3. Créer un simulateur → copier le **jeton**
4. Sur le PC overlay : Administration → Serveur de résultats → URL + jeton → Tester / Activer

---

## 6. Vérifications rapides (checklist reprise)

| Étape | Commande / action | Attendu |
|---|---|---|
| Clone OK | `git status` | working tree clean |
| Tests Core | `dotnet test tests/...` | tous verts (Windows) |
| Tests serveur | `cd server && python3 -m pytest tests/` | 61 passed |
| Build overlay | `.\build.ps1` | `dist\MT_F1Chronos.exe` |
| Stack serveur | `./scripts/up-results.sh` | `caddy` + `results` Up |
| Health API | `curl …/api/v1/health` | `{"ok":true}` |
| SPA | navigateur `/` | page Résultats charge |

---

## 7. Ce qui n’est **pas** dans Git

| Élément | Raison |
|---|---|
| `.env` | Secrets (mdp admin seed, cookie secret) |
| `certs/*.pem` | Certificats TLS privés |
| `%LOCALAPPDATA%\MT_F1Chronos\` | Données utilisateur overlay |
| `dist/`, `bin/`, `obj/` | Artefacts build |
| `server/.venv/`, `server/data*` | Dev local |

Après clone : toujours recréer `.env` via `.env.example` + `init-env.sh`.

---

## 8. Dépannage fréquent

| Symptôme | Piste |
|---|---|
| Overlay sans données UDP | Format 2025/2026, port 20888, jeu en fenêtré |
| Build WPF sur macOS | Impossible — utiliser Windows ou CI |
| `ERR_SSL_*` en prod | `RESULTS_TLS_MODE`, logs `docker compose logs caddy` |
| Sync simu échoue TLS LAN | Cocher « Ignorer certificat TLS » ou installer CA PKI |
| Classement web bloqué « Chargement… » | Hard refresh ; vérifier API leaderboard + SSE |
| Menu « … » invisible | Corrigé (position fixe) — déployer SPA à jour |
| Tests serveur 401 session | `RESULTS_DOMAIN` vide en test = cookies non Secure (normal) |

---

## 9. Prochaine lecture

1. [`system/baseline-v1.md`](system/baseline-v1.md) — règles métier §3 avant toute modif scores/overlay
2. [`archives/INDEX.md`](archives/INDEX.md) — historique des jalons
3. [`AGENTS.md`](../AGENTS.md) — où placer le code
