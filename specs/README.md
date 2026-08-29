# OpenSpec — F1 Chronos

Documentation produit et technique du dépôt `MT_F1Chronos`.

## Structure

| Chemin | Rôle |
|---|---|
| [`system/baseline-v1.md`](system/baseline-v1.md) | **État actuel** — modèles, contrats, règles métier, dépendances. Source de vérité pour les agents et les revues. |
| [`archives/INDEX.md`](archives/INDEX.md) | **Historique** — jalons livrés, correctifs notables, liens vers les entrées détaillées. |
| [`archives/*.md`](archives/) | Fiches d’archive par thème ou période (ne remplacent pas la baseline). |

## Règles de maintenance

1. **Changement de comportement ou de contrat** → mettre à jour `system/baseline-v1.md` (section concernée + date en tête).
2. **Jalon livré ou vague de correctifs** → ajouter une entrée dans `archives/INDEX.md` et, si utile, une fiche `archives/YYYY-MM-*.md`.
3. **Ne pas dupliquer** la baseline dans les archives : les archives expliquent le *pourquoi* et le *quand* ; la baseline décrit le *quoi* actuel.
4. **Hors scope OpenSpec** : détail pixel-perfect XAML, secrets, données LocalAppData, bases SQLite locales de dev (`server/data-smoke/`).

## Périmètre

- **Overlay WPF** : `src/MT_F1Chronos.Core/`, `src/MT_F1Chronos.App/`, `tests/`
- **Serveur de résultats** (optionnel) : `server/` — API JSON + SPA statique

Voir aussi [`AGENTS.md`](../AGENTS.md) pour les conventions agents IA.
