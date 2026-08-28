# OpenSpec — F1 Chronos System Baseline

| Champ | Valeur |
|---|---|
| **Spec ID** | `system/baseline` |
| **Version** | `v1` |
| **Produit** | F1 Chronos (`MT_F1Chronos`) |
| **Statut** | `baseline` (état actuel du code) |
| **Date** | 2026-07-22 |
| **Portée** | Domaine Core + orchestration App (hors détail XAML pixel-perfect) |
| **Source de vérité** | Code sous `src/` et `tests/` |

---

## 0. Résumé produit

**F1 Chronos** est un overlay Windows (WPF) pour **EA Sports F1 25/26**. Il écoute la télémétrie UDP officielle, enregistre les tours valides dans un classement **global** et optionnellement des **concours**, et affiche un panneau always-on-top (circuit, pseudo, chrono live, TOP 3/5/10).

```
F1 (UDP :20888)
  → UdpTelemetryListener
  → F1UdpPacketParser (+ UdpFormatProfile 2025|2026)
  → TelemetryUpdate (snapshot cloné)
  → AppController (UI Dispatcher)
       ├─ SessionStore / ContestStore
       ├─ OverlayCoordinator → OverlaySnapshot
       └─ OverlayWindow / Admin / Scores / Export
```

---

## 1. Modèles de données

### 1.1 Chrono & classements

#### `ChronoEntry` — unité de score persistée

| Champ | Type | Défaut | Notes |
|---|---|---|---|
| `Id` | `string` | `Guid` | Identifiant stable (suppression unitaire) |
| `Name` | `string` | `""` | Pseudo au moment de l’enregistrement |
| `TrackId` | `int` | `-1` | ID circuit F1 UDP |
| `TrackName` | `string` | `"Inconnu"` | Libellé résolu |
| `BestLapMs` | `uint?` | — | Temps du tour (ms) ; seuls `> 0` comptent |
| `StartedAt` | `DateTime` | `UtcNow` | Horodatage d’enregistrement |
| `EndedAt` | `DateTime?` | — | Généralement égal à `StartedAt` à l’écriture |

#### `ChronoDatabase` — enveloppe JSON

- `Sessions: List<ChronoEntry>` — fichier `track-{id}.json`

#### `LeaderboardRow` — ligne de classement (lecture)

| Champ | Type | Notes |
|---|---|---|
| `EntryId` | `string` | Lien vers `ChronoEntry.Id` |
| `Rank` | `int` | Rang 1-based |
| `Name` | `string` | |
| `BestLapMs` | `uint` | |
| `FormattedTime` | `string` | `MM:SS.mmm` ou `--:--.---` |

#### `TrackSummary`

| Champ | Type |
|---|---|
| `TrackId` | `int` |
| `TrackName` | `string` |
| `ScoreCount` | `int` |

#### `OverlaySnapshot` — contrat UI overlay

| Champ | Type | Défaut |
|---|---|---|
| `TrackName` | `string` | `"—"` |
| `PlayerName` | `string` | `"Joueur"` |
| `CurrentLapFormatted` | `string` | `"--:--.---"` |
| `HasCurrentLap` | `bool` | |
| `LeaderboardSize` | `int` | `5` |
| `Leaderboard` | `IReadOnlyList<LeaderboardRow>` | `[]` |
| `ShowGlobalLeaderboard` | `bool` | `true` |
| `ShowContestLeaderboard` | `bool` | `false` |
| `ContestLabel` | `string` | `""` |
| `ContestLeaderboardSize` | `int` | `10` |
| `ContestLeaderboard` | `IReadOnlyList<LeaderboardRow>` | `[]` |
| `BestPerPlayer` | `bool` | |
| `IsConnected` | `bool` | dernier paquet &lt; 3 s |
| `IsTimeTrial` | `bool` | |

#### `LeaderboardSizes` (constantes)

| Nom | Valeur |
|---|---|
| `Compact` | `3` |
| `Default` | `5` |
| `Extended` | `10` |

`Normalize(size)` : `3` → 3, `10` → 10, sinon → `5`.

---

### 1.2 Concours

#### `ContestStatus`

| Valeur | Code |
|---|---|
| `Draft` | `0` |
| `Active` | `1` |
| `Stopped` | `2` |

#### `Contest`

| Champ | Type | Notes |
|---|---|---|
| `Id` | `string` | `Guid("N")` |
| `Name` | `string` | Requis à la création |
| `Status` | `ContestStatus` | |
| `CreatedAt` | `DateTime` | UTC |
| `StartedAt` / `StoppedAt` | `DateTime?` | |
| `TrackFilter` | `int?` | `null` = tous circuits ; sinon filtre strict |

#### `ContestIndex`

- `Contests: List<Contest>` — fichier `contests/index.json`

---

### 1.3 Paramètres applicatifs

#### `AppSettings` (`settings.json`)

| Champ | Défaut | Contrainte |
|---|---|---|
| `UdpPort` | `20888` | |
| `UdpFormat` | `2025` | Migrés : `2025` ou `2026` uniquement |
| `OverlayTop` | `195` | Offset depuis le haut de la work area |
| `OverlayRight` | `12` | Distance au bord droit |
| `OverlayWidth` | `300` | Clamp `[300, 440]` |
| `LeaderboardSize` | `5` | Normalisé TOP 3/5/10 |
| `PlayerName` | `""` | Max **20** caractères |
| `OverlayContestId` | `""` | Concours principal overlay |
| `ShowContestOnOverlay` | `true` | |
| `ContestLeaderboardSize` | `10` | |
| `HideGlobalWhenContest` | `false` | |
| `BestPerPlayer` | `false` | |

#### Serveur de résultats (optionnel)

| Champ | Défaut | Contrainte |
|---|---|---|
| `ResultsServerEnabled` | `false` | Off = aucun réseau, fonctionnement local inchangé |
| `ResultsServerUrl` | `""` | HTTPS, port **443** implicite (`https://hostname`). Les `:80` / `:443` / `:8080` sont retirés au chargement. |
| `ResultsServerToken` | `""` | Jeton créé sur le VPS |
| `SimulatorId` | généré | Guid local stable |
| `SimulatorLabel` | `""` | Nom affiché sur le site |
| `ResultsSyncIntervalSeconds` | `120` | Clamp 15–600 ; le VPS déclare hors ligne après **2 ×** cet intervalle |

Le logiciel du simulateur **n’a pas besoin** du serveur : UDP, overlay, stores et admin locaux restent la source de vérité.

#### `OverlayDisplayMode` (dérivé)

| Mode | Mapping settings |
|---|---|
| `GlobalAndContest` | `ShowContest=true`, `HideGlobal=false` |
| `ContestOnly` | `ShowContest=true`, `HideGlobal=true` |
| `GlobalOnly` | `ShowContest=false` |

#### `OverlaySizes`

| Constante | Valeur |
|---|---|
| `Default` | `300` |
| `Max` | `440` |
| `MaxPlayerNameLength` | `20` |

---

### 1.4 Télémétrie

#### `TelemetryState` (état de travail mutable → publication via `Clone()`)

Champs principaux : `IsReceiving`, `LastPacketUtc`, `SessionUid`, `TrackId` / `RawTrackId`, `TrackLengthMeters`, `SessionType`, `GameMode`, `PlayerCarIndex` / `ResolvedCarIndex`, `DriverStatus`, `CurrentLapInvalid`, formats paquet, `SessionBestLapMs` / `PersonalBestLapMs` / `CurrentLastLapMs` / `CurrentLapTimeMs`, `LastEventCode`.

| Propriété calculée | Règle |
|---|---|
| `IsOnTrack` | `DriverStatus ∈ {1, 2, 4}` |
| `IsTimeTrial` | `SessionType == 13` **ou** `GameMode == 5` |
| `TrackName` | `F1UdpConstants.GetTrackName` |
| `EffectiveBestLapMs` | session ?? personal ?? last |

#### `TelemetryUpdate` (événement publié)

| Champ | Type |
|---|---|
| `State` | `TelemetryState` (snapshot) |
| `SessionStarted` / `SessionEnded` / `TrackChanged` / `LapCompleted` | `bool` |
| `CompletedLapMs` | `uint?` |

Types debug associés : `TelemetryDebugSnapshot`, `SessionStoreDebugInfo`, `PacketLogEntry`, `CarLapDebugRow`.

---

### 1.5 Persistance — layout disque

Racine : `%LOCALAPPDATA%\MT_F1Chronos\`

| Chemin | Contenu |
|---|---|
| `settings.json` | `AppSettings` |
| `admin.secret.json` | Salt + hash PBKDF2 admin |
| `sessions/track-{id}.json` | Scores globaux |
| `sessions.json` (+ `.bak`) | Legacy → migration |
| `contests/index.json` | Métadonnées concours |
| `contests/{contestId}/track-{id}.json` | Scores concours |

Écriture atomique : `*.tmp` → `File.Move(overwrite)`. Flush différé : **2 s**.

---

## 2. Contrats d’API / Interfaces

### 2.1 Score boards (Core)

```csharp
interface IScoreBoardQuery
{
    string BoardLabel { get; }
    IReadOnlyList<TrackSummary> GetTracksWithScores();
    IReadOnlyList<LeaderboardRow> GetScoresForTrack(
        int trackId, bool bestPerPlayer = false, string? playerName = null);
    IReadOnlyList<string> GetPlayerNamesForTrack(int trackId);
}

interface IScoreBoardMutator
{
    bool DeleteEntry(string entryId);
    int DeletePlayerOnTrack(string playerName, int trackId);
    int ClearTrack(int trackId);
    int ClearAll();
}

interface IScoreBoardView : IScoreBoardQuery, IScoreBoardMutator;
```

| Implémentation | `BoardLabel` |
|---|---|
| `SessionStore` | `"Global"` |
| `ContestStore.AsScoreBoard(id)` | `"Concours — {name}"` |

---

### 2.2 `LeaderboardQuery` (statique)

```csharp
IEnumerable<ChronoEntry> Filter(
    IEnumerable<ChronoEntry> entries,
    bool bestPerPlayer = false,
    string? playerName = null);

IReadOnlyList<LeaderboardRow> ToRows(IEnumerable<ChronoEntry> ranked);
```

---

### 2.3 `TrackScoreBoard` — moteur de tableau par circuit

| Constante | Valeur |
|---|---|
| `MaxEntriesPerTrack` | `5000` |

API clé : `LoadFromDirectory`, `Record`, `GetLeaderboard`, `GetScoresForTrack`, `Delete*`, `Clear*`, `PersistDirty`, `DrainDirty`, `GetRecentPlayerNames(max=10)`.

Événement : `BecameDirty`.

---

### 2.4 `SessionStore` : `IScoreBoardView`, `IDisposable`

```csharp
void Load() / void Save()
void EnsureTrackContext(int trackId, string trackName)
void RecordCompletedLap(string playerName, int trackId, string trackName, uint lapMs)
void CloseActiveSession()
IReadOnlyList<LeaderboardRow> GetLeaderboard(int trackId, int count = 5, bool bestPerPlayer = false)
OverlaySnapshot BuildSnapshot(TelemetryState state, string playerName, /* options overlay */)
int ResolveOverlayTrackId(TelemetryState state)  // state → live → dernier scoré
```

---

### 2.5 `ContestStore` : `IDisposable`

```csharp
IReadOnlyList<Contest> List()
Contest? Get(string contestId)
Contest Create(string name, bool startImmediately = true)  // nom requis
bool Start(string contestId) / bool Stop(string contestId) / bool Delete(string contestId)
void RecordCompletedLap(...)  // tous les concours Active (+ TrackFilter)
// Requêtes / mutations scorées (préfixe contestId)
IScoreBoardView AsScoreBoard(string contestId)
```

---

### 2.6 Export & formatage

```csharp
// ScoreExporter
void ExportCsv / ExportJson / ExportHtml(IReadOnlyList<ChronoEntry>, string filePath)
string NeutralizeFormula(string value)

// LapTimeFormatter
string Format(uint lapMs)  // 0 → "--:--.---"
```

CSV : `rank,name,trackId,trackName,lapMs,lapTime,recordedAt` (rang recalculé **par circuit**).

---

### 2.7 Télémétrie UDP

```csharp
// UdpTelemetryListener
event Action<TelemetryUpdate>? UpdateReceived
TelemetryState State { get; }          // dernier snapshot publié
void SetFormat(ushort format)
void Start(int port = 20888) / void Stop()

// F1UdpPacketParser
bool TryParse(ReadOnlySpan<byte> buffer, TelemetryState state, out TelemetryUpdate? update)
void SetFormat(ushort format)
TelemetryDebugSnapshot BuildDebugSnapshot(...)
```

Paquets traités : Session=`1`, LapData=`2`, Event=`3`, SessionHistory=`11`, TimeTrial=`14`.

| Profil | Header | MaxCars | LapData | TT dataset / offset |
|---|---|---|---|---|
| **2025** | 29 | 22 | 57 | 24 / 2 |
| **2026** | 29 | 24 | 57 | 25 / 3 |

`UdpFormatProfile.For(format)` : `2025` → 2025 ; **sinon** → 2026.

---

### 2.8 Orchestration App (`AppController`)

Responsabilités publiques (contrat UI) :

| Domaine | Méthodes |
|---|---|
| Cycle de vie | `CreateOverlay`, `Start`, `Dispose` |
| Overlay | `PositionOverlay`, `SetOverlayWidth`, `SaveOverlayPosition`, `SetLeaderboardSize`, `SetContestLeaderboardSize`, `SetBestPerPlayer`, `PromptPlayerName` |
| Admin / fenêtres | `ShowAdminWindow` (password), `ShowManageScores`, `ShowAllScores`, `ShowContestScores`, `ShowDebugWindow`, `ChangeAdminPassword` |
| Concours | `ListContests`, `CreateContest`, `StartContest`, `StopContest`, `DeleteContest`, `SetOverlayContest`, `Get/SetOverlayDisplayMode` |
| Export | `ExportScores(format, contestId?, trackId?)`, `ListExportTracks` |
| Sync | `NotifyScoresChanged` (+ `ResultsSyncClient.RequestSync` si activé) |
| Serveur résultats | `SaveResultsServerSettings`, `TestResultsServerAsync`, `GetResultsSyncStatus` |

Services satellitaires :

| Service | Contrat |
|---|---|
| `SettingsStore` | `Load()` / `Save(AppSettings)` + migrations légères |
| `AdminPassword` (internal) | `EnsureConfigured`, `TrySetPassword`, `Verify` |
| `OverlayCoordinator` (internal) | `Position`, `Refresh` |
| `ScoreExportService` | `Export(owner, entries, format, filePrefix)` |
| `ResultsSyncClient` (internal) | Push optionnel HTTP ; no-op si désactivé |

---

### 2.9 Serveur de résultats (`server/` — FastAPI / Linux)

Le simulateur **initie** toujours `POST /api/v1/sync` (NAT). Le VPS n’ouvre aucune connexion vers le simu.

**Architecture** : backend = API JSON pure ; frontend = SPA statique en vanilla JS (modules ES, **sans build ni CDN**) servie depuis `server/app/static/`. Toute route non-`/api` renvoie `index.html` (routage history côté client). Plus de rendu Jinja.

API simulateur (contrat figé) :

| Méthode | Chemin | Rôle |
|---|---|---|
| `GET` | `/api/v1/health` | Test de visibilité |
| `POST` | `/api/v1/register` | Auto-enregistrement (crée simu + tenant, retourne jeton) |
| `POST` | `/api/v1/sync` | Snapshot + `deletedEntryIds` + ACK jobs → jobs `pending` en réponse |

API web (JSON, camelCase) :

| Préfixe | Accès | Rôle |
|---|---|---|
| `POST /api/v1/auth/{login,logout,setup,change-password}`, `GET /auth/me` | public | Session cookie signé (`SessionMiddleware`, SameSite=lax, Secure si `RESULTS_DOMAIN`) |
| `GET /api/v1/tenants…`, `GET /api/v1/sims…` | filtré par visibilité | Lecture classements (pagination `page`/`page_size`, 20/défaut, 100 max) |
| `/api/v1/admin/*` | rôle `admin` | CRUD tenants/sims/users, gestion chronos, jobs, réglages |

**Comptes** : table `users` (email unique, hash PBKDF2, rôle `admin`/`visitor`, `disabled`) + `user_tenant_access` (visiteur → tenants assignés). Premier compte : hash legacy migré, sinon seed `RESULTS_ADMIN_PASSWORD` → `admin@localhost`, sinon formulaire de bootstrap (`/auth/setup`, refusé dès qu’un compte existe). Le dernier admin actif ne peut être ni rétrogradé, ni désactivé, ni supprimé. Login rate-limité (5 échecs / 5 min / IP).

**Visibilité** : tenant `public` (lisible anonymement si `public_access` global actif, sinon compte requis) ou `private` (admin + visiteurs assignés). `public_access` = réglage global admin.

Jobs (`deleteEntry`, `renameEntry`, `renamePlayer`, `restoreEntry`, `setPlayerName`) : créés **uniquement** par action admin. Wipe DB VPS ≠ job. Revert pending = annule + restore replica ; revert applied = job inverse (`setPlayerName` non revertible). `setPlayerName` demande au simu d’adopter un nouveau pseudo de session (le simu reste maître de `PlayerName`, appliqué à la réception puis renvoyé aux syncs suivantes).

Présence simu : hors ligne si `now - lastSeen > 2 × syncIntervalSeconds`.

Sécurité HTTP : CSP stricte `default-src 'self'` (aucune ressource externe), `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy: same-origin`, `Cache-Control: no-store` sur `/api/*`. `RESULTS_SECRET` signe les sessions ; absent → secret aléatoire par boot (sessions perdues au redémarrage, jamais de constante connue).

Docker : `docker compose up --build` / `podman compose up --build`. Caddy public **443** (et **80** ACME) ; FastAPI interne **8080** (non publié). Volume `/data`. Hostname `RESULTS_DOMAIN` obligatoire pour Let’s Encrypt.

---

## 3. Règles métier critiques

### BR-01 — Enregistrement d’un tour

Un tour n’est persisté que si **toutes** les conditions sont vraies :

1. Le parser signale `LapCompleted` (changement de `lastLapMs` du joueur)
2. Le tour **précédent** n’était **pas** invalide (`CurrentLapInvalid == 0` sur le paquet précédent)
3. `CompletedLapMs > 0`
4. `TrackId >= 0`
5. Pseudo joueur non vide (trim)

Effets : écriture dans **SessionStore (global)** **et** dans **chaque concours `Active`** éligible.

### BR-02 — Anti faux-positif session

- Au `SessionUid` change / event `SSTA` : reset contexte tour ; **premier** `lastLap` broadcasté est **seedé sans enregistrement**
- Event `SEND` : `CloseActiveSession`

### BR-03 — Concours

- Création : démarrage immédiat (`Active`) par défaut
- Enregistrement uniquement si `Status == Active`
- `TrackFilter` optionnel : si défini, seul ce circuit est accepté
- Overlay : panneau concours affiché seulement si le concours principal existe **et** est `Active` (Draft/Stopped invisibles sur l’overlay)

### BR-04 — Classement & mode BEST

- Entrées retenues : `BestLapMs > 0`
- Tri : temps croissant, puis `StartedAt`
- **Best-per-player** : groupement par nom (insensible à la casse), conserve le meilleur temps (égalité → plus ancien)
- Tailles autorisées : TOP **3 / 5 / 10**

### BR-05 — Overlay global+concours → TOP 3 forcé

Quand le panneau concours **et** le global sont affichés simultanément, la taille du global est **forcée à TOP 3** (`LeaderboardSizes.Compact`), indépendamment du réglage admin.

### BR-06 — Résolution circuit overlay

Priorité : `TelemetryState.TrackId` → contexte live session → circuit le plus récemment scoré. Connexion = réception + dernier paquet &lt; **3 s**.

### BR-07 — Garde anti-ghost Melbourne

Tant que le joueur est en piste (`IsOnTrack` ou chrono courant &gt; 0), un basculement de track id positif → `0` est rejeté (faux Melbourne).

### BR-08 — Capacité & flush

- Max **5000** entrées scorées par circuit (global et par concours) — conservation des meilleurs
- Flush différé **2 s** après dirty ; flush immédiat sur dispose / mutations destructives

### BR-09 — Pseudo

- Max **20** caractères (troncature)
- Demande à **chaque lancement** (prérempli)
- Fallback `"Joueur"` si vide requis

### BR-10 — Administration

- Ouverture Admin **conditionnée** à `AdminPassword.Verify`
- Premier run : l’utilisateur **choisit** son mot de passe (min **4** caractères, confirmation) — pas de secret embarqué
- Changement possible depuis Admin (section Sécurité)
- Stockage : PBKDF2-SHA256, **100 000** itérations, salt 16 / hash 32 octets → `admin.secret.json`

### BR-11 — Export CSV sécurisé

Toute cellule commençant par `=`, `+`, `-`, `@`, `\t`, `\r` est préfixée par `'` (`NeutralizeFormula`) pour bloquer l’exécution de formules tableur.

### BR-12 — Formats UDP

- Le format jeu (`Telemetry Settings`) et `AppSettings.UdpFormat` doivent correspondre (`2025` ou `2026`)
- Port par défaut **20888**, IP typique `127.0.0.1`
- Send rate recommandé (docs produit) : **20–60 Hz**

### BR-13 — UI refresh

- Timer overlay **250 ms** pour snapshot / classements
- Chrono live mis à jour aussi à chaque paquet UDP pertinent
- Top-most réaffirmé périodiquement (~2 s)

### BR-14 — Serveur de résultats optionnel

- Désactivé par défaut. Aucun appel réseau tant que `ResultsServerEnabled` est faux.
- Un échec de sync **ne bloque jamais** l’enregistrement local (BR-01) ni l’overlay.
- Le simu **pull** (NAT) : snapshot + jobs à chaque mutation (debounce 1 s) **et** selon `ResultsSyncIntervalSeconds`.
- Jobs admin revertibles ; wipe DB serveur **sans** job vers le simu.
- Job `setPlayerName` : renomme le pseudo de session du simu (`AppSettings.PlayerName` mis à jour à réception, cf. BR-09).
- Overlay : LED serveur à côté de la télémétrie.
- Transport : `ResultsSyncClient` utilise `ResultsServerUrl` normalisée en `https://host` (TCP **443**). Pas de sync sur le 8080 public.
- VPS : simu hors ligne après **2 ×** l’intervalle annoncé, sans sync.
- Admin web : comptes en SQLite (`users`, PBKDF2, rôles admin/visiteur) ; `RESULTS_ADMIN_PASSWORD` = seed initial seulement ; visiteur limité à ses tenants assignés ; accès public anonyme = option globale + visibilité par tenant.

---

## 4. Dépendances techniques

### 4.1 Solution

| Projet | TFM | Rôle |
|---|---|---|
| `MT_F1Chronos.Core` | `net8.0` | Domaine, UDP, stores, export, contrat sync |
| `MT_F1Chronos.App` | `net8.0-windows` + WPF | UI, orchestration, client sync optionnel |
| `MT_F1Chronos.Tests` | `net8.0` | Tests xUnit (Core) |
| `server/` | Python 3.12 | FastAPI + SQLite (hors solution .NET) |

- AssemblyName App : `MT_F1Chronos` / Product : **F1 Chronos** / Version : **1.1.0**
- Sortie Release : `dist\MT_F1Chronos.exe`

### 4.2 Packages NuGet

| Projet | Package | Version |
|---|---|---|
| Core / App | — | Aucun (BCL uniquement) |
| Tests | `Microsoft.NET.Test.Sdk` | `17.11.1` |
| Tests | `xunit` | `2.9.2` |
| Tests | `xunit.runner.visualstudio` | `2.8.2` |

### 4.3 Plateforme & runtime

| Dépendance | Exigence |
|---|---|
| OS | **Windows 10/11** (WPF) |
| SDK build | **.NET 8** |
| Jeu | EA Sports **F1 25** ou **F1 26** |
| Affichage jeu | Fenêtré / Borderless recommandé (fullscreen exclusif peut masquer l’overlay) |
| Protocole | Télémétrie UDP officielle F1 (localhost) |

### 4.4 Dépendances système BCL (notables)

- `System.Net.Sockets` (`UdpClient`)
- `System.Text.Json` (persistance)
- `System.Security.Cryptography` (PBKDF2 admin)
- `System.Windows` / WPF (App uniquement)
- `Microsoft.Win32` (`SaveFileDialog` export)

### 4.5 Intégrations externes

| Système | Contrat |
|---|---|
| F1 UDP | Port **20888**, formats **2025/2026**, paquets 1/2/3/11/14 |
| Système de fichiers local | `%LOCALAPPDATA%\MT_F1Chronos\` |
| Shell | Ouverture post-export (`Process.Start` + `UseShellExecute`) |

### 4.6 Non-objectifs de cette baseline

- Pas d’authentification cloud / comptes distants (les comptes du serveur Results restent locaux à ce serveur)
- Pas d’installeur MSI documenté ici (script `build.ps1` + raccourcis)
- Throttle UDP→UI (#3 architecture) **non implémenté** dans cette baseline
- Le serveur Results reste une archive optionnelle : les tenants y sont des regroupements logiques de simus, pas une isolation multi-clients chiffrée

---

## 5. Glossaire rapide

| Terme | Sens |
|---|---|
| Global | Tableau `SessionStore` (tous joueurs / tous concours confondus) |
| Concours | Tableau parallèle isolé, lifecycle Draft/Active/Stopped |
| BEST | Mode meilleur chrono par joueur |
| Snapshot | Copie immuable de `TelemetryState` publiée hors thread UDP |
| Principal | Concours sélectionné pour l’overlay (`OverlayContestId`) |
| Tenant / Organisation | Regroupement de simulateurs côté serveur Results (visibilité public/privé) |
| Visiteur | Compte serveur en lecture seule, limité à ses tenants assignés |

---

## 6. Traçabilité fichiers (référence)

| Domaine | Chemins |
|---|---|
| Modèles | `src/MT_F1Chronos.Core/Models/` |
| Stores | `src/MT_F1Chronos.Core/Services/{TrackScoreBoard,SessionStore,ContestStore,ScoreExporter}.cs` |
| UDP | `src/MT_F1Chronos.Core/Telemetry/` |
| Orchestration | `src/MT_F1Chronos.App/Services/{AppController,SettingsStore,AdminPassword,OverlayCoordinator,ScoreExportService,ResultsSyncClient}.cs` |
| Settings | `src/MT_F1Chronos.App/AppSettings.cs`, `OverlaySizes.cs` |
| Serveur résultats | `server/` |
| Tests | `tests/MT_F1Chronos.Tests/` |

---

*Fin de `specs/system/baseline-v1.md`.*
