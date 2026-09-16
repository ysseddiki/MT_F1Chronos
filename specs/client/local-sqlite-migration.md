# Migration locale JSON → SQLite (overlay)

Procédure à suivre **après mise à jour du dépôt / de l’exe** sur une machine qui avait déjà des chronos en JSON.

| Champ | Valeur |
|---|---|
| **Produit** | Overlay F1 Chronos (Windows) |
| **Fichier cible** | `%LOCALAPPDATA%\MT_F1Chronos\chronos.db` |
| **Automatique** | Oui — au premier lancement post-update |
| **Serveur web** | Non concerné (déjà SQLite) |

---

## 1. Ce qui change

| Avant | Après |
|---|---|
| `sessions/track-*.json` | table `laps` (`contest_id` NULL) |
| `contests/index.json` + `contests/{id}/track-*.json` | tables `contests` + `laps` |
| Cap **5 000** tours / circuit | Cap **50 000** tours / circuit |

Le sync HTTP vers le serveur de résultats **ne change pas** (toujours un snapshot JSON complet).

---

## 2. Après `git pull` / déploiement de l’exe

### Étapes

1. **Arrêter** l’overlay s’il tourne.
2. **Mettre à jour** le code / l’exe (`git pull`, `dotnet build`, ou remplacer `MT_F1Chronos.exe`).
3. **Lancer une fois** l’overlay (compte Windows habituel).
4. La migration s’exécute au `Load()` de `SessionStore` / `ContestStore` :
   - import des JSON existants dans `chronos.db` ;
   - marquage `meta.json_migrated = 1` ;
   - déplacement des dossiers JSON vers `archive-json/{yyyyMMdd-HHmmss}/`.
5. Vérifier (optionnel) :
   - présence de `%LOCALAPPDATA%\MT_F1Chronos\chronos.db` ;
   - dossier `archive-json\…` si tu avais des données JSON ;
   - classements Global / Concours inchangés dans l’UI.

Aucun script manuel n’est requis en usage normal.

### Build / test (dev Windows)

```powershell
cd MT_F1Chronos
dotnet build MT_F1Chronos.sln -c Release
dotnet test tests/MT_F1Chronos.Tests/MT_F1Chronos.Tests.csproj
.\build.ps1   # optionnel → dist\
```

Puis lancer `dist\MT_F1Chronos.exe` (ou `dotnet run` sur le projet App) **une fois** pour migrer les données locales.

---

## 3. Vérifications utiles

Dans PowerShell :

```powershell
$dir = "$env:LOCALAPPDATA\MT_F1Chronos"
Get-Item "$dir\chronos.db"
Get-ChildItem "$dir\archive-json" -ErrorAction SilentlyContinue
# Les anciens dossiers sessions\ et contests\ ne doivent plus être à la racine après migration réussie.
```

Compter les tours (outil `sqlite3` si installé) :

```powershell
sqlite3 "$env:LOCALAPPDATA\MT_F1Chronos\chronos.db" "SELECT COUNT(*) FROM laps WHERE deleted_at IS NULL;"
sqlite3 "$env:LOCALAPPDATA\MT_F1Chronos\chronos.db" "SELECT COUNT(*) FROM contests;"
```

---

## 4. Rollback / restauration

La migration **ne supprime pas** les JSON : ils sont déplacés sous `archive-json\…`.

Si besoin de repartir du JSON (support rare) :

1. Arrêter l’overlay.
2. Renommer ou supprimer `chronos.db` (+ fichiers `-wal` / `-shm` s’ils existent).
3. Remettre `sessions\` et `contests\` depuis le dossier d’archive à la racine `%LOCALAPPDATA%\MT_F1Chronos\`.
4. Relancer une version **pré-SQLite** de l’overlay, **ou** relancer une version SQLite après avoir retiré le flag (voir ci-dessous).

Pour **rejouer** la migration avec une build SQLite :

1. Arrêter l’overlay.
2. Supprimer `chronos.db` (+ `-wal` / `-shm`).
3. Restaurer `sessions\` / `contests\` depuis `archive-json\…`.
4. Relancer : `MigrateIfNeeded` réimporte (flag absent).

---

## 5. Multi-machines / sync

- Chaque PC a sa propre `chronos.db` sous LocalAppData.
- Le serveur Results reste l’archive web ; pas de migration côté VPS pour ce changement.
- Après migration locale, un sync serveur (si activé) republie le board comme avant.

---

## 6. Dépannage

| Symptôme | Action |
|---|---|
| `chronos.db` absent après lancement | Vérifier droits d’écriture LocalAppData ; relancer en tant qu’utilisateur normal |
| Classements vides mais `archive-json` présent | Restaurer JSON + supprimer `chronos.db` + relancer (rejouer migration) |
| Message « déjà migré » au 2ᵉ démarrage | Normal |
| Fichiers `chronos.db-wal` / `-shm` | Normaux (mode WAL) ; les laisser avec le `.db` |

Code : `LocalChronosDb`, `LocalChronosMigrator`, `SessionStore`, `ContestStore` sous `src/MT_F1Chronos.Core/Services/`.
