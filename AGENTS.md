# AGENTS.md — F1 Chronos

Guide pour les agents IA travaillant sur ce dépôt.

## Produit

**F1 Chronos** (`MT_F1Chronos`) — overlay WPF .NET 8 pour la télémétrie UDP **EA Sports F1 25/26**.

- UI en **français**
- Port UDP par défaut : **20888**
- Formats UDP : **2025** / **2026**
- Données locales : `%LOCALAPPDATA%\MT_F1Chronos\`

Spécification système (modèles, contrats, règles métier, dépendances) :

→ [`specs/onboarding.md`](specs/onboarding.md) — **reprise machine** (clone, build, deploy, checklist)  
→ [`specs/system/baseline-v1.md`](specs/system/baseline-v1.md) — état actuel  
→ [`specs/README.md`](specs/README.md) — structure OpenSpec  
→ [`specs/archives/INDEX.md`](specs/archives/INDEX.md) — historique des jalons

## Architecture

```
src/MT_F1Chronos.Core/     # Domaine, UDP, stores, export, contrat sync résultats (net8.0, sans NuGet)
src/MT_F1Chronos.App/      # WPF + orchestration (net8.0-windows)
server/                    # Serveur de résultats Linux (FastAPI + SQLite, Docker/Podman)
tests/MT_F1Chronos.Tests/  # xUnit, référence Core uniquement
specs/                     # OpenSpec : baseline + archives
```

Flux principal :

```
UDP → UdpTelemetryListener → F1UdpPacketParser
    → TelemetryUpdate (snapshot cloné)
    → AppController → SessionStore / ContestStore → Overlay
         └─ (optionnel) ResultsSyncClient → HTTP POST /api/v1/sync → serveur FastAPI (jobs en réponse)
```

### Où mettre le code

| Besoin | Emplacement |
|---|---|
| Modèles / ranking / persistance scores | `Core/Models`, `Core/Services` |
| Parsing UDP / états télémétrie | `Core/Telemetry` |
| Fenêtres, styles, orchestration UI | `App/Windows`, `App/Services` |
| Réglages + migrations légères | `App/Services/SettingsStore` + `AppSettings` |
| Sync serveur de résultats (client) | `App/Services/ResultsSyncClient` — **optionnel**, ne remplace pas les stores locaux |
| Serveur de résultats | `server/` (FastAPI, SQLite, jobs pull-only) |
| Mot de passe admin | `App/Services/AdminPassword` (hash local, jamais en dur) |
| Tests métier | `tests/MT_F1Chronos.Tests` |

Ne pas mettre de logique métier lourde dans le code-behind XAML : passer par stores / query / `AppController`.

## Règles métier à ne pas casser

Avant de modifier l’enregistrement de tours ou l’overlay, relire **§3** de la baseline. Points critiques :

1. Tour enregistré seulement si valide (pas cut) + `TrackId >= 0` + pseudo non vide
2. Un tour valide va dans le **global** et **tous** les concours `Active`
3. Overlay concours : uniquement si concours principal **Active**
4. Global + concours affichés ensemble → global forcé **TOP 3**
5. Pseudo max **20** caractères ; TOP autorisés **3 / 5 / 10**
6. Export CSV : neutraliser les formules (`=`, `+`, `-`, `@`, …)
7. `TelemetryState` publié via **`Clone()`** — ne jamais partager l’objet mutable du parser avec l’UI
8. Admin : mot de passe choisi / changeable par l’utilisateur, stocké en PBKDF2 sous LocalAppData

## Conventions de code

- C# moderne, nullable activé, `ImplicitUsings` activé
- Noms de types / API en **anglais** ; chaînes UI en **français**
- Persistance JSON camelCase, écriture atomique (`*.tmp` → move)
- Flush différé stores : **~2 s** (`DeferredFlush` / `TrackScoreBoard`)
- Styles ComboBox sombres partagés : `App/Themes/DarkControls.xaml`
- Lignes de classement Scores / Manage : `LeaderboardRowUi`
- Ne pas ajouter de NuGet dans Core/App sans besoin réel
- Pas d’over-engineering : pas de nouveaux frameworks, VMs uniquement si ça clarifie

## Build & tests

Build/tests sur **Windows** (WPF). Sur macOS, le SDK peut être absent — ne pas conclure à un échec produit.

```powershell
dotnet build MT_F1Chronos.sln -c Release
dotnet test tests/MT_F1Chronos.Tests/MT_F1Chronos.Tests.csproj
.\build.ps1   # Release → dist\MT_F1Chronos.exe + raccourcis
```

Serveur de résultats (Docker / Podman) :

```bash
./scripts/init-env.sh   # .env : mdp admin + secret cookie aléatoires
# Édite RESULTS_DOMAIN, RESULTS_TLS_MODE (letsencrypt|custom|internal|http) et CADDY_EMAIL si besoin
sudo ./scripts/setup-podman-ports.sh  # rootless Podman : ports 80/443
./scripts/up-results.sh               # valide .env puis compose up
# ou : docker compose up -d --build
./scripts/check-results-ssl.sh
# Internet : 443 (HTTPS LE) + 80 (ACME). LAN : custom PKI, tls internal ou http.
```

## Git

- **Commit / push uniquement si l’utilisateur le demande**
- Messages concis, style existant du dépôt (impératif, focus « pourquoi »)
- Pas de force-push sur `main`, pas de `--no-verify`, pas de modification de `git config`
- Ne pas committer secrets, `admin.secret.json`, ni données LocalAppData

## Hors scope (sauf demande explicite)

- Throttle / coalesce UDP→UI (point architecture #3, différé)
- Installateur / auto-update
- Backend cloud / multi-utilisateur distant (agrégation multi-simu = v2 ; le VPS Results est une archive optionnelle)
- Refactors massifs hors de la tâche demandée

## Checklist rapide avant PR / commit

- [ ] Règles BR baseline respectées (tours, concours, TOP 3 forcé, admin)
- [ ] UI FR cohérente
- [ ] Pas de secret embarqué
- [ ] `dotnet build` / tests Core OK sur Windows
- [ ] Spec baseline mise à jour si modèles / contrats / règles changent
