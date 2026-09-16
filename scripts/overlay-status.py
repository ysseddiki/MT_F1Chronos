#!/usr/bin/env python3
"""List local F1 Chronos overlay configuration and score store summary.

Usage:
  python3 scripts/overlay-status.py
  python3 scripts/overlay-status.py --data-dir "/path/to/MT_F1Chronos"
"""

from __future__ import annotations

import argparse
import json
import os
import sqlite3
import sys
from pathlib import Path


def default_data_dir() -> Path:
    if sys.platform == "win32":
        base = os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")
        return Path(base) / "MT_F1Chronos"
    # Dev on macOS/Linux: still point at the Windows-style path if present, else cwd hint.
    return Path.home() / "Library" / "Application Support" / "MT_F1Chronos"


def read_product_version(repo_root: Path) -> str:
    csproj = repo_root / "src" / "MT_F1Chronos.App" / "MT_F1Chronos.App.csproj"
    core = repo_root / "src" / "MT_F1Chronos.Core" / "ProductInfo.cs"
    if core.exists():
        for line in core.read_text(encoding="utf-8").splitlines():
            if "InformationalVersion" in line and "=" in line:
                return line.split("=", 1)[1].strip().strip('"; ')
    if csproj.exists():
        for line in csproj.read_text(encoding="utf-8").splitlines():
            if "InformationalVersion" in line:
                return line.split(">", 1)[1].split("<", 1)[0].strip()
    return "inconnu"


def load_settings(data_dir: Path) -> dict:
    path = data_dir / "settings.json"
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:  # noqa: BLE001
        return {"_error": str(exc)}


def summarize_sqlite(db_path: Path) -> dict:
    conn = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    conn.row_factory = sqlite3.Row
    try:
        meta = {
            r["key"]: r["value"]
            for r in conn.execute("SELECT key, value FROM meta").fetchall()
        }
        total = conn.execute(
            "SELECT COUNT(*) AS c FROM laps WHERE deleted_at IS NULL"
        ).fetchone()["c"]
        global_laps = conn.execute(
            "SELECT COUNT(*) AS c FROM laps WHERE contest_id IS NULL AND deleted_at IS NULL"
        ).fetchone()["c"]
        contest_laps = conn.execute(
            "SELECT COUNT(*) AS c FROM laps WHERE contest_id IS NOT NULL AND deleted_at IS NULL"
        ).fetchone()["c"]
        contests = conn.execute("SELECT COUNT(*) AS c FROM contests").fetchone()["c"]
        names = [
            r["name"]
            for r in conn.execute(
                """
                SELECT DISTINCT name FROM laps
                WHERE deleted_at IS NULL AND TRIM(name) != ''
                ORDER BY name COLLATE NOCASE
                """
            ).fetchall()
        ]
        tracks = conn.execute(
            """
            SELECT COUNT(DISTINCT track_id) AS c FROM laps
            WHERE deleted_at IS NULL
            """
        ).fetchone()["c"]
        custom = conn.execute(
            """
            SELECT COUNT(*) AS c FROM laps
            WHERE deleted_at IS NULL AND custom_setup != 0
            """
        ).fetchone()["c"]
        return {
            "engine": "SQLite",
            "path": str(db_path),
            "schema_version": meta.get("schema_version"),
            "json_migrated": meta.get("json_migrated") == "1",
            "laps_total": total,
            "laps_global": global_laps,
            "laps_contests": contest_laps,
            "contests": contests,
            "tracks": tracks,
            "custom_setup_laps": custom,
            "pseudos": names,
        }
    finally:
        conn.close()


def summarize_json(data_dir: Path) -> dict:
    sessions = data_dir / "sessions"
    contests = data_dir / "contests"
    files = list(sessions.glob("track-*.json")) if sessions.is_dir() else []
    names: set[str] = set()
    laps = 0
    for path in files:
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            for entry in data.get("sessions") or []:
                laps += 1
                n = (entry.get("name") or "").strip()
                if n:
                    names.add(n)
        except Exception:  # noqa: BLE001
            continue
    contest_count = 0
    if (contests / "index.json").exists():
        try:
            idx = json.loads((contests / "index.json").read_text(encoding="utf-8"))
            contest_count = len(idx.get("contests") or [])
        except Exception:  # noqa: BLE001
            contest_count = -1
    return {
        "engine": "JSON (legacy)",
        "sessions_dir": str(sessions) if sessions.is_dir() else None,
        "track_files": len(files),
        "laps_approx": laps,
        "contests": contest_count,
        "pseudos": sorted(names, key=str.casefold),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Résumé conf overlay F1 Chronos")
    parser.add_argument(
        "--data-dir",
        type=Path,
        default=None,
        help="Dossier LocalAppData MT_F1Chronos (défaut: détecté)",
    )
    args = parser.parse_args()
    repo_root = Path(__file__).resolve().parents[1]
    data_dir = args.data_dir or default_data_dir()

    print(f"Produit          : F1 Chronos")
    print(f"Version (repo)   : {read_product_version(repo_root)}")
    print(f"Data dir         : {data_dir}")
    print(f"Existe           : {'oui' if data_dir.is_dir() else 'non'}")

    settings = load_settings(data_dir)
    if settings.get("_error"):
        print(f"settings.json    : erreur ({settings['_error']})")
    elif settings:
        print(f"Pseudo session   : {settings.get('playerName') or '—'}")
        print(f"UDP port         : {settings.get('udpPort', '—')}")
        print(f"UDP format       : {settings.get('udpFormat', '—')}")
        print(
            f"Setup perso      : "
            f"{'comptés' if settings.get('countCustomSetupLaps', True) else 'exclus'}"
        )
        print(f"Sync serveur     : {'oui' if settings.get('resultsServerEnabled') else 'non'}")
        if settings.get("resultsServerUrl"):
            print(f"URL sync         : {settings.get('resultsServerUrl')}")
    else:
        print("settings.json    : absent")

    db_path = data_dir / "chronos.db"
    archive = data_dir / "archive-json"
    if db_path.exists():
        info = summarize_sqlite(db_path)
        print(f"Persistance      : {info['engine']}")
        print(f"Base             : {info['path']}")
        print(f"Schéma / migré   : {info.get('schema_version')} / {info.get('json_migrated')}")
        print(
            f"Tours            : {info['laps_total']} "
            f"(global {info['laps_global']}, concours {info['laps_contests']})"
        )
        print(f"Concours         : {info['contests']}")
        print(f"Circuits         : {info['tracks']}")
        print(f"Tours setup perso: {info['custom_setup_laps']}")
        print(f"Pseudos ({len(info['pseudos'])})   : {', '.join(info['pseudos']) or '—'}")
    else:
        info = summarize_json(data_dir)
        print(f"Persistance      : {info['engine']}")
        print(f"Fichiers track   : {info.get('track_files', 0)}")
        print(f"Tours (approx)   : {info.get('laps_approx', 0)}")
        print(f"Concours         : {info.get('contests', 0)}")
        print(f"Pseudos ({len(info['pseudos'])})   : {', '.join(info['pseudos']) or '—'}")

    if archive.is_dir():
        stamps = sorted(p.name for p in archive.iterdir() if p.is_dir())
        print(f"Archives JSON    : {', '.join(stamps) if stamps else '(vide)'}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
