# SPA — routes et modules

Frontend : `server/app/static/` — ES modules, **sans build**, servi par FastAPI + fallback `index.html`.

Bootstrap : `static/js/main.js` → `router.js` (history API).

---

## Routes publiques

| Pattern | Vue | Description |
|---|---|---|
| `/` | `home.js` | Accueil, redirection tenant si un seul |
| `/t/{slug\|id}` | `tenant.js` | Classement agrégé organisation |
| `/sim/{id}` | `sim.js` | Classement **global** du simulateur |
| `/sim/{id}?contest={cid}` | `sim.js` | Classement **concours** (lié à ce simu uniquement) |
| `/login` | `login.js` | Connexion |
| `/profile` | `profile.js` | Profil SimRacer (`sim_pseudo`) |
| `/admin` | `admin.js` | Administration (rôle admin) |

### Redirections compatibilité

| Ancienne URL | Nouvelle |
|---|---|
| `/contests` | `/` ou `/sim/{sim}` si `?sim=` |
| `/sim/{id}/contests/{cid}` | `/sim/{id}?contest={cid}` |
| `/t/{id}/tracks/{n}` | `/t/{id}?track={n}` |
| `/admin/login` | `/login` |

**Pas de page `/contests` dédiée** : les concours sont choisis sur la page simulateur via le sélecteur « Tableau ».

---

## Moteur classement partagé

`views/board_page.js` — utilisé par `tenant.js`, `sim.js` :

| Query | Effet |
|---|---|
| `?track=` | Circuit affiché (onglet Classement) |
| `?view=recent` | Onglet **Derniers chronos** (admin uniquement ; défaut : Classement) |
| `?best=false` | Tous les tours (défaut : meilleur / joueur) |
| `?page=` | Pagination (20 lignes) |

| Composant | Fichier |
|---|---|
| Tableau + pagination | `components.js` → `boardTable`, `pagination` |
| Derniers chronos | `components.js` → `recentLapsPanel` ; API `GET …/recent-laps` |
| Toolbar simus | `components.js` → `simToolbarStrip` (tuiles simu + pilote session) |
| Actions admin « … » | `board_manage.js` → `actionMenu` (fixe, opaque, exclusif) |
| Live SSE | `state.js` → `subscribeChanges` → `loadBoard()` + `loadRecent()` |

---

## Arborescence JS

```
static/js/
├── main.js           # routes + bootstrap
├── router.js         # history, setQuery, cleanup
├── api.js            # fetch JSON
├── state.js          # session, tenants, SSE
├── components.js     # topbar, tableaux, menus
├── board_manage.js   # actions admin lignes
├── dom.js
├── paths.js
└── views/
    ├── home.js
    ├── tenant.js
    ├── sim.js          # global + concours (contestBoardSelect)
    ├── login.js
    ├── profile.js
    ├── admin.js
    └── notfound.js
```

---

## Concours côté serveur

- Créés sur le **simulateur** (overlay WPF) → synchronisés via `POST /api/v1/sync` dans le payload `contests[]`
- Stockés SQLite : table `contests` clé `(simulator_id, id)`
- Affichage web : **uniquement** via `/sim/{id}?contest=…` — pas d’agrégation inter-simus pour les concours
- API : `GET /api/v1/sims/{id}/contests`, `GET …/contests/{cid}`, leaderboard avec `contest_id`

---

## API — derniers chronos

| Endpoint | Paramètres | Accès | Réponse |
|---|---|---|---|
| `GET /api/v1/sims/{id}/recent-laps` | `limit` (15 déf., max 50), `contest_id` (optionnel) | **admin** | `{ rows: Lap[] }` triées par `startedAt` DESC, tous circuits |
| `GET /api/v1/tenants/{id}/recent-laps` | `limit` | **admin** | Idem, global multi-sims (`simLabel` renseigné) |

Affichage : onglet **Derniers chronos** sur `/t/…` et `/sim/…` (`?view=recent`, visible seulement si connecté en admin).
