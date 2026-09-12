# SPA — routes et modules

Frontend : `server/app/static/` — ES modules, **sans build**, servi par FastAPI + fallback `index.html`.

Bootstrap : `static/js/main.js` → `router.js` (history API).

---

## Routes publiques

| Pattern | Vue | Description |
|---|---|---|
| `/` | `home.js` | Accueil, redirection tenant si un seul |
| `/t/{slug\|id}` | `tenant.js` | **Classement** agrégé organisation |
| `/t/{slug\|id}/championship` | `championship.js` | **Championnat** à points (global org, web only) |
| `/t/{slug\|id}/recent` | `recent.js` | **Derniers chronos** (admin) — journal tous circuits |
| `/sim/{id}` | `sim.js` | Classement **global** du simulateur |
| `/sim/{id}?contest={cid}` | `sim.js` | Classement **concours** (lié à ce simu uniquement) |
| `/championship` | `championship.js` | Sélecteur d’org (ou redirect si une seule) |
| `/recent` | `recent.js` | Idem (admin) |
| `/login` | `login.js` | Connexion |
| `/account` | `account.js` | Compte connecté (mot de passe, pseudo SimRacer) |
| `/profile` | `profile.js` | Profil SimRacer obligatoire (`sim_pseudo`) |
| `/admin` | `admin.js` | Administration (rôle admin) |

### Topbar

| Lien | Qui | Cible |
|---|---|---|
| Classement | tous | org courante ou `/` |
| Championnat | tous | `/t/…/championship` |
| Derniers chronos | admin | `/t/…/recent` |
| Administration | admin | `/admin` |
| Menu user (clic) | connecté | Mon compte / Pseudo / Admin / Déconnexion |

### Redirections compatibilité

| Ancienne URL | Nouvelle |
|---|---|
| `/contests` | `/` ou `/sim/{sim}` si `?sim=` |
| `/sim/{id}/contests/{cid}` | `/sim/{id}?contest={cid}` |
| `/t/{id}/tracks/{n}` | `/t/{id}?track={n}` |
| `/t/{id}?view=recent` | `/t/{id}/recent` |
| `/admin/login` | `/login` |

**Pas de page `/contests` dédiée** : les concours sont choisis sur la page simulateur via le sélecteur « Tableau ».

---

## Moteur classement partagé

`views/board_page.js` — utilisé par `tenant.js`, `sim.js` :

| Query | Effet |
|---|---|
| `?track=` | Circuit affiché |
| `?best=false` | Tous les tours (défaut : meilleur / joueur) |
| `?page=` | Pagination (20 lignes) |

| Composant | Fichier |
|---|---|
| Tableau + pagination | `components.js` → `boardTable`, `pagination` |
| Derniers chronos | page `recent.js` + `recentLapsPanel` ; API `GET …/recent-laps` |
| Championnat | page `championship.js` ; API `GET …/championship` |
| Toolbar simus | `components.js` → `simToolbarStrip` |
| Actions admin « … » | `board_manage.js` → `actionMenu` |
| Live SSE | `state.js` → `subscribeChanges` |

---

## Arborescence JS

```
static/js/
├── main.js
├── router.js
├── api.js
├── state.js
├── components.js     # topbar (Classement / Championnat / …), menus
├── board_manage.js
├── dom.js
├── paths.js
└── views/
    ├── home.js
    ├── tenant.js
    ├── sim.js
    ├── championship.js
    ├── recent.js
    ├── account.js
    ├── login.js
    ├── profile.js
    ├── admin.js
    ├── admin_settings.js   # accès public + barème points
    └── notfound.js
```

---

## Concours côté serveur

- Créés sur le **simulateur** (overlay WPF) → synchronisés via `POST /api/v1/sync`
- Affichage web : **uniquement** via `/sim/{id}?contest=…`
- Pas d’agrégation inter-simus pour les concours ; le **championnat** agrège uniquement le **global** org

---

## API — derniers chronos

| Endpoint | Accès | Notes |
|---|---|---|
| `GET /api/v1/sims/{id}/recent-laps` | **admin** | limit 15 déf., max 50 ; `contest_id` optionnel |
| `GET /api/v1/tenants/{id}/recent-laps` | **admin** | global multi-sims |

---

## API — championnat (web only)

| Endpoint | Accès | Notes |
|---|---|---|
| `GET /api/v1/tenants/{id}/championship` | même visibilité que le classement | points par place × meilleur / pilote / circuit |
| `POST /api/v1/admin/settings` | admin | `points_by_place` : `"25,18,15,…"` (places scorées = nb de valeurs) |

Règle : pour chaque circuit du global org, classement « meilleur tour / joueur » → P1 reçoit le 1er chiffre, etc. Somme sur tous les circuits. **Hors scope overlay WPF.**
