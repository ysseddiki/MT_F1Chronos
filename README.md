# MT_F1Chronos

Overlay PC pour **EA Sports F1 25/26** (format UDP **2026**) affichant le **TOP 5** par circuit, ton **tour en cours** et ton **meilleur tour**.

![Placement overlay](docs/overlay-preview.jpg)

## Fonctionnalités

- Overlay visible dans la **barre des tâches** (fenêtre principale, pas d'icône tray)
- **Nom du joueur** demandé une seule fois au premier lancement
- **TOP 5** des meilleurs chronos du circuit en cours
- **Tour en cours** synchronisé en direct via télémétrie UDP
- **Meilleur tour** de la session active
- **Menu burger (☰)** cliquable avec options complètes
- **Scores par circuit** avec navigation ◀ ▶ entre les circuits
- Déplacement de l'overlay par **glisser-déposer** sur l'en-tête

## Prérequis

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- F1 25 ou F1 26 en mode **Fenêtré** ou **Borderless** (pas plein écran exclusif)

## Configuration F1 25/26

Dans le jeu : **Settings → Telemetry Settings**

| Paramètre | Valeur |
|---|---|
| UDP Telemetry | **On** |
| UDP IP Address | `127.0.0.1` |
| UDP Port | `20777` |
| UDP Format | **`2026`** (valeur par défaut) |
| UDP Send Rate | 20–60 Hz |

> Le format **2026** est obligatoire — c'est celui utilisé par le parser de l'application.

## Compilation

```powershell
cd MT_F1Chronos
dotnet build -c Release
```

Ou avec le script fourni :

```powershell
.\build.ps1
```

L'exécutable se trouve dans :
`src\MT_F1Chronos.App\bin\Release\net8.0-windows\MT_F1Chronos.exe`

## Utilisation

1. Lancer `MT_F1Chronos.exe`
2. Saisir ton **nom de joueur** à la première ouverture (modifiable ensuite)
3. L'overlay apparaît en haut-droite de l'écran
4. Lancer F1 et démarrer une session **Chrono / Time Trial**
5. L'overlay affiche automatiquement :
   - le **circuit détecté**
   - le **tour en cours** (chrono live)
   - ton **meilleur tour**
   - le **TOP 5** du circuit

Aucune popup ne s'affiche à chaque session — le nom du joueur est réutilisé automatiquement.

### Affichage overlay

| Zone | Contenu |
|---|---|
| En-tête | Nom du circuit + menu ☰ |
| TOP 5 | 5 meilleurs chronos du circuit en cours |
| Tour en cours | Chrono live du tour actuel |
| Meilleur tour | Meilleur temps enregistré cette session |

### Menu burger (☰)

| Action | Description |
|---|---|
| Changer le nom du joueur | Modifie ton identité pour les chronos |
| Scores par circuit | Tous les scores du circuit, navigation ◀ ▶ |
| Taille de l'overlay | Petit (220 px) / Moyen (268 px) / Grand (340 px) |
| Quitter | Ferme l'application |

### Raccourcis

| Action | Raccourci |
|---|---|
| Changer le nom du joueur | `Ctrl+Shift+N` |
| Déplacer l'overlay | Glisser l'en-tête (nom du circuit) |

## Personnalisation

Fichier `%LOCALAPPDATA%\MT_F1Chronos\settings.json` :

```json
{
  "udpPort": 20777,
  "overlayTop": 195,
  "overlayRight": 12,
  "overlayWidth": 268,
  "playerName": "TonNom"
}
```

| Clé | Description |
|---|---|
| `overlayTop` | Distance depuis le haut de l'écran (px) |
| `overlayRight` | Distance depuis le bord droit (px) |
| `overlayWidth` | Largeur de l'overlay (px) |
| `playerName` | Nom du joueur utilisé pour tous les chronos |

Ajuste `overlayTop` / `overlayRight` pour caler l'overlay sous le panneau de chrono du jeu.

## Données

Les chronos sont sauvegardés dans :
`%LOCALAPPDATA%\MT_F1Chronos\sessions.json`

Les scores sont regroupés **par circuit**. Chaque session de chrono crée une entrée avec le nom du joueur, le circuit et le meilleur tour enregistré.

## Architecture

```
MT_F1Chronos.Core   → Télémétrie UDP F1 2026, parsing paquets, stockage JSON
MT_F1Chronos.App    → Overlay WPF, fenêtre nom joueur, menu burger, hotkeys
```

## Limites

- Ne modifie pas l'UI native du jeu (overlay externe uniquement)
- Nécessite la télémétrie UDP activée avec le format **2026**
- L'overlay couvre une petite zone de l'écran (boutons cliquables, non click-through)
- Le TOP 5 affiche les sessions avec un meilleur tour enregistré sur le circuit en cours
