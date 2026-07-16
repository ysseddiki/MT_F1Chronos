# Installateur F1 Chronos

## Prérequis

1. [Inno Setup 6](https://jrsoftware.org/isinfo.php)
2. .NET 8 SDK

## Build

Depuis la racine du repo (Windows) :

```powershell
.\build.ps1
```

Sorties :
- `publish\` — application self-contained
- `artifacts\F1Chronos-Setup-<version>.exe` — setup

## Upgrade

Le script utilise un `AppId` fixe. Relancer un setup plus récent met à jour l’installation existante sans écraser `settings.json` / `sessions.json` (stockés séparément dans `%LOCALAPPDATA%\MT_F1Chronos`).

## Version

Synchroniser `#define MyAppVersion` avec `Directory.Build.props` à chaque release.
