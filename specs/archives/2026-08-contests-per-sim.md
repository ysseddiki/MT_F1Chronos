# Archive — Concours par simulateur + menu actions

| Champ | Valeur |
|---|---|
| **Date** | 2026-08-31 |
| **Commit repère** | `43e4d2a` |
| **Baseline** | §2.9 SPA, §3 BR-15 |

## Contexte

Les concours sont créés et gérés sur le **simulateur** (overlay WPF), synchronisés vers le serveur. Une page `/contests` globale était redondante et prêtait à confusion.

## Livrables

- Suppression page `/contests` et entrée menu topbar
- Sélecteur « Tableau » sur `/sim/{id}` : global ou concours de ce simu
- URL : `/sim/{id}?contest={contestId}`
- Redirections anciennes URLs (`/contests`, `/sim/…/contests/…`)
- Menu « … » admin : position **fixe** (plus coupé par `overflow` du tableau)

## Règle produit

**Un concours web appartient toujours à un simulateur** — pas d’agrégation concours multi-simus au niveau tenant.
