# Archive — Évolution serveur de résultats (août 2026)

| Champ | Valeur |
|---|---|
| **Période** | 2026-08 |
| **Périmètre** | `server/` uniquement |
| **Commits repère** | `d7ac5bb` → `23ceead` |
| **Baseline mise à jour** | `system/baseline-v1.md` (2026-08-29) |

## Contexte

Le serveur de résultats est passé d’un rendu Jinja2 monolithique à une **API JSON + SPA vanilla** (ES modules, sans build). Objectifs : décorréler front/back, multi-utilisateurs, visibilité par organisation, classements live, rôle pilote simulateur.

## Jalons livrés

### 1. Organisations (tenants)

- Regroupement de simulateurs ; classement agrégé multi-sims.
- URL friendly `/t/{slug}` ; l’id hex reste valide.
- Visibilité `public` / `private` + réglage global `public_access`.
- Auto-enregistrement simu via `POST /api/v1/register`.

### 2. API JSON + SPA statique

- Routes lecture : tenants, sims, tracks, leaderboards paginés (20/page, `best=true` par défaut).
- Routes auth : session cookie, bootstrap admin, visiteurs, SimRacers.
- Routes admin : CRUD tenants/sims/users, gestion chronos, jobs, réglages.
- SPA : `server/app/static/` — routeur history, vues par écran (`views/*.js`).

### 3. Flux live (SSE)

- `GET /api/v1/stream` : compteur de version (sans payload métier).
- Les pages de classement s’abonnent via `subscribeChanges()` (`state.js`).
- **Correctif 2026-08-29** : rafraîchissement **partiel** du tableau (`loadBoard()` dans `board_page.js`) au lieu d’un re-render complet de la page — évite le blocage « Chargement du classement… » (race DOM / fetch obsolète ; compteur `loadGen`).
- Repli : intervalle 60 s si EventSource indisponible ; debounce SSE 1,5 s.

### 4. Rôle SimRacer

- Colonne `sim_pseudo` ; page `/profile` obligatoire à la première connexion.
- Bouton « Appliquer » sur les feuilles de temps → job `setPlayerName` (pseudo profil uniquement).
- Ligne surlignée si le nom correspond au profil (comme l’overlay).
- UI : distinction **Session** (télémétrie live) vs **Profil** (pseudo serveur).

### 5. Toolbar et actions admin sur les classements

- `simToolbarStrip` : panneau simulateur(s) sous le sélecteur de circuit ; badge « En piste ici » selon le **circuit affiché**.
- `board_manage.js` : menu « … » par ligne (admin) — renommer chrono, renommer partout, supprimer.
- Partagé entre pages tenant / sim / concours et admin simulateur.

### 6. Durcissement et correctifs

- Résolution tenant par slug puis id ; invalidation tours ; rate-limit login.
- Topbar : filtre `.filter(Boolean)` avant `append` (évite texte `null`).
- ACK jobs sync : uniquement commandes réellement appliquées côté simu.
- Build Docker : résilience PyPI lente sur VPS.

## Fichiers clés (référence)

| Zone | Chemins |
|---|---|
| API | `server/app/main.py`, `admin_api.py`, `auth.py`, `store.py`, `db.py` |
| Moteur classement SPA | `server/app/static/js/views/board_page.js` |
| Actions admin lignes | `server/app/static/js/board_manage.js` |
| Composants UI | `server/app/static/js/components.js` |
| État / SSE | `server/app/static/js/state.js` |
| Tests | `server/tests/` (61 tests au 2026-08-29) |

## Hors scope de cette archive

- Overlay WPF et client `ResultsSyncClient` (sauf contrat sync/jobs inchangé en surface).
- Installateur, throttle UDP→UI, multi-tenant cloud chiffré.
