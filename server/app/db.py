from __future__ import annotations

import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SCHEMA = """
CREATE TABLE IF NOT EXISTS tenants (
    id TEXT PRIMARY KEY,
    label TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS simulators (
    id TEXT PRIMARY KEY,
    label TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    client_id TEXT,
    last_seen_utc TEXT,
    sync_interval_seconds INTEGER NOT NULL DEFAULT 120,
    player_name TEXT NOT NULL DEFAULT '',
    current_track_id INTEGER NOT NULL DEFAULT -1,
    current_track_name TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS contests (
    simulator_id TEXT NOT NULL,
    id TEXT NOT NULL,
    name TEXT NOT NULL,
    status TEXT NOT NULL,
    track_filter INTEGER,
    created_at TEXT,
    started_at TEXT,
    stopped_at TEXT,
    PRIMARY KEY (simulator_id, id)
);

CREATE TABLE IF NOT EXISTS laps (
    simulator_id TEXT NOT NULL,
    id TEXT NOT NULL,
    contest_id TEXT,
    track_id INTEGER NOT NULL,
    track_name TEXT NOT NULL,
    name TEXT NOT NULL,
    best_lap_ms INTEGER NOT NULL,
    started_at TEXT NOT NULL,
    deleted_at TEXT,
    PRIMARY KEY (simulator_id, id)
);

CREATE INDEX IF NOT EXISTS idx_laps_sim_track ON laps (simulator_id, contest_id, track_id, deleted_at);

CREATE TABLE IF NOT EXISTS jobs (
    id TEXT PRIMARY KEY,
    simulator_id TEXT NOT NULL,
    type TEXT NOT NULL,
    status TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    revert_of_job_id TEXT,
    created_at TEXT NOT NULL,
    delivered_at TEXT,
    applied_at TEXT,
    reverted_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_jobs_sim_status ON jobs (simulator_id, status);

CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL CHECK (role IN ('admin', 'visitor', 'simracer')),
    disabled INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    sim_pseudo TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS user_tenant_access (
    user_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    PRIMARY KEY (user_id, tenant_id)
);
"""


def utcnow() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat()


def _slugify(text: str) -> str:
    import re
    import unicodedata

    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode("ascii")
    text = re.sub(r"[^a-zA-Z0-9]+", "-", text.lower()).strip("-")
    return (text[:48] if text else "") or "organisation"


def _backfill_tenant_slugs(conn: sqlite3.Connection) -> None:
    used = {
        r["slug"]
        for r in conn.execute(
            "SELECT slug FROM tenants WHERE slug IS NOT NULL AND slug != ''"
        ).fetchall()
    }
    rows = conn.execute(
        "SELECT id, label FROM tenants WHERE slug IS NULL OR slug = ''"
    ).fetchall()
    for row in rows:
        base = _slugify(row["label"] or "organisation")
        slug = base
        n = 2
        while slug in used:
            slug = f"{base}-{n}"
            n += 1
        used.add(slug)
        conn.execute("UPDATE tenants SET slug = ? WHERE id = ?", (slug, row["id"]))


def _migrate_users_simracer(conn: sqlite3.Connection) -> None:
    """Allow role simracer + column sim_pseudo on existing databases."""
    user_cols = {row[1] for row in conn.execute("PRAGMA table_info(users)").fetchall()}
    if "sim_pseudo" not in user_cols:
        conn.execute("ALTER TABLE users ADD COLUMN sim_pseudo TEXT NOT NULL DEFAULT ''")

    try:
        conn.execute("SAVEPOINT migrate_simracer_role")
        conn.execute(
            """INSERT INTO users (id, email, password_hash, role, disabled, created_at, sim_pseudo)
               VALUES ('_migrate_test', '_migrate@test.local', 'x', 'simracer', 1, ?, '')""",
            (utcnow(),),
        )
        conn.execute("DELETE FROM users WHERE id = '_migrate_test'")
        conn.execute("RELEASE SAVEPOINT migrate_simracer_role")
        return
    except sqlite3.IntegrityError:
        conn.execute("ROLLBACK TO SAVEPOINT migrate_simracer_role")
        conn.execute("RELEASE SAVEPOINT migrate_simracer_role")

    conn.execute(
        """CREATE TABLE users_new (
            id TEXT PRIMARY KEY,
            email TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL,
            role TEXT NOT NULL CHECK (role IN ('admin', 'visitor', 'simracer')),
            disabled INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            sim_pseudo TEXT NOT NULL DEFAULT ''
        )"""
    )
    conn.execute(
        """INSERT INTO users_new (id, email, password_hash, role, disabled, created_at, sim_pseudo)
           SELECT id, email, password_hash, role, disabled, created_at,
                  COALESCE(sim_pseudo, '') FROM users"""
    )
    conn.execute("DROP TABLE users")
    conn.execute("ALTER TABLE users_new RENAME TO users")


def migrate(conn: sqlite3.Connection) -> None:
    """Light migrations for existing VPS databases."""
    import uuid

    _migrate_users_simracer(conn)

    cols = {row[1] for row in conn.execute("PRAGMA table_info(simulators)").fetchall()}
    if "tenant_id" not in cols:
        conn.execute("ALTER TABLE simulators ADD COLUMN tenant_id TEXT")

    tenant_cols = {row[1] for row in conn.execute("PRAGMA table_info(tenants)").fetchall()}
    if "visibility" not in tenant_cols:
        conn.execute("ALTER TABLE tenants ADD COLUMN visibility TEXT NOT NULL DEFAULT 'public'")
    if "slug" not in tenant_cols:
        conn.execute("ALTER TABLE tenants ADD COLUMN slug TEXT")
        conn.execute(
            """CREATE UNIQUE INDEX IF NOT EXISTS idx_tenants_slug
               ON tenants(slug) WHERE slug IS NOT NULL AND slug != ''"""
        )
        _backfill_tenant_slugs(conn)

    conn.execute(
        "CREATE INDEX IF NOT EXISTS idx_user_tenant_access_tenant ON user_tenant_access (tenant_id)"
    )

    conn.execute("CREATE INDEX IF NOT EXISTS idx_simulators_tenant ON simulators (tenant_id)")
    conn.execute("CREATE INDEX IF NOT EXISTS idx_simulators_client ON simulators (client_id)")
    conn.execute(
        """CREATE UNIQUE INDEX IF NOT EXISTS idx_simulators_client_unique
           ON simulators(client_id) WHERE client_id IS NOT NULL AND client_id != ''"""
    )

    orphans = conn.execute(
        "SELECT id, label FROM simulators WHERE tenant_id IS NULL OR tenant_id = ''"
    ).fetchall()
    for row in orphans:
        tenant_id = uuid.uuid4().hex
        label = (row["label"] or "Organisation").strip() or "Organisation"
        conn.execute(
            "INSERT INTO tenants (id, label, created_at) VALUES (?, ?, ?)",
            (tenant_id, label, utcnow()),
        )
        conn.execute("UPDATE simulators SET tenant_id = ? WHERE id = ?", (tenant_id, row["id"]))
    conn.commit()


def connect(path: Path) -> sqlite3.Connection:
    path.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(path, check_same_thread=False)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    conn.execute("PRAGMA journal_mode = WAL")
    conn.executescript(SCHEMA)
    migrate(conn)
    return conn


def row_to_dict(row: sqlite3.Row | None) -> dict[str, Any] | None:
    return dict(row) if row is not None else None
