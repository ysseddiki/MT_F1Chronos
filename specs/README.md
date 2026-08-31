# OpenSpec — F1 Chronos

Documentation produit et technique du dépôt `MT_F1Chronos`.

## Par où commencer

| Profil | Document |
|---|---|
| **Reprise sur une nouvelle machine** | [`onboarding.md`](onboarding.md) |
| **État actuel du produit** | [`system/baseline-v1.md`](system/baseline-v1.md) |
| **Serveur : variables d’env** | [`server/env.md`](server/env.md) |
| **Serveur : routes SPA** | [`server/spa-routes.md`](server/spa-routes.md) |
| **Historique des jalons** | [`archives/INDEX.md`](archives/INDEX.md) |

## Structure

```
specs/
├── onboarding.md              # Clone, build, deploy, checklist reprise machine
├── README.md                    # Ce fichier
├── system/
│   └── baseline-v1.md           # Baseline : modèles, contrats, règles métier
├── server/
│   ├── env.md                   # Variables RESULTS_* / Caddy
│   └── spa-routes.md            # Routes frontend résultats
└── archives/
    ├── INDEX.md
    └── YYYY-MM-*.md             # Fiches jalon
```

## Règles de maintenance

1. **Changement de comportement ou de contrat** → `system/baseline-v1.md` (+ date en tête).
2. **Nouvelle variable d’env ou route SPA** → `server/env.md` ou `server/spa-routes.md`.
3. **Jalon livré** → entrée dans `archives/INDEX.md` + fiche si utile.
4. **Procédure install / reprise machine** → `onboarding.md`.
5. **Ne pas dupliquer** la baseline dans les archives.

## Hors scope OpenSpec

- Détail pixel-perfect XAML
- Secrets (`.env`, `admin.secret.json`, PEM)
- Données runtime (`%LOCALAPPDATA%`, volumes Docker, `server/data-smoke/`)

## Périmètre code

| Zone | Chemin |
|---|---|
| Overlay WPF | `src/MT_F1Chronos.Core/`, `src/MT_F1Chronos.App/` |
| Tests overlay | `tests/MT_F1Chronos.Tests/` |
| Serveur résultats | `server/` |
| Scripts déploiement | `scripts/` |

Voir aussi [`AGENTS.md`](../AGENTS.md).
