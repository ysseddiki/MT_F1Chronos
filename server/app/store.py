from __future__ import annotations

import hashlib
import json
import re
import secrets
import sqlite3
import unicodedata
import uuid
from typing import Any

from . import db
from .online import is_simulator_connected


DEFAULT_PAGE_SIZE = 20
MAX_PAGE_SIZE = 100
DEFAULT_RECENT_LAPS = 15
MAX_RECENT_LAPS = 50
MAX_PLAYER_NAME_LENGTH = 20
# Points F1-like (P1→Pn) pour le championnat web uniquement — pas l’overlay.
DEFAULT_POINTS_BY_PLACE = "25,18,15,12,10,8,6,4,2,1"
MAX_POINTS_PLACES = 40

TENANT_VISIBILITIES = ("public", "private")
# Org virtuelle : agrège tous les simulateurs visibles (pas une ligne SQLite).
ALL_TENANT_KEY = "all"
RESERVED_TENANT_SLUGS = frozenset({ALL_TENANT_KEY})


def make_all_tenant(*, sim_count: int = 0, org_count: int = 0) -> dict[str, Any]:
    return {
        "id": ALL_TENANT_KEY,
        "slug": ALL_TENANT_KEY,
        "label": "Toutes les organisations",
        "visibility": "public",
        "created_at": None,
        "sim_count": sim_count,
        "org_count": org_count,
        "is_aggregate": True,
    }


def parse_points_by_place(raw: str | None) -> list[int]:
    """Parse « 25,18,10,… » → liste de points par place (longueur = places scorées)."""
    text = (raw or "").strip()
    if not text:
        raise ValueError("Indiquez au moins une valeur (ex. 25,18,15,12,10,8,6,4,2,1).")
    parts = [p.strip() for p in text.split(",") if p.strip()]
    if not parts:
        raise ValueError("Indiquez au moins une valeur (ex. 25,18,15,12,10,8,6,4,2,1).")
    if len(parts) > MAX_POINTS_PLACES:
        raise ValueError(f"Maximum {MAX_POINTS_PLACES} places.")
    out: list[int] = []
    for part in parts:
        if not re.fullmatch(r"\d+", part):
            raise ValueError(f"Valeur invalide : « {part} » (entier ≥ 0 attendu).")
        out.append(int(part))
    return out


def format_points_by_place(points: list[int]) -> str:
    return ",".join(str(p) for p in points)

# Chronos affichables : pas de circuit inconnu, pseudo vide, ou temps nul.
LAP_VALID_SQL = "deleted_at IS NULL AND track_id >= 0 AND name != '' AND best_lap_ms > 0"


def slugify(text: str) -> str:
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode("ascii")
    text = re.sub(r"[^a-zA-Z0-9]+", "-", text.lower()).strip("-")
    return (text[:48] if text else "") or "organisation"


def hash_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def _normalize_page(page: int, page_size: int) -> tuple[int, int]:
    page = max(1, int(page))
    page_size = max(1, min(int(page_size), MAX_PAGE_SIZE))
    return page, page_size


def format_lap(ms: int) -> str:
    if ms <= 0:
        return "--:--.---"
    minutes, rem = divmod(ms, 60_000)
    seconds, millis = divmod(rem, 1000)
    return f"{minutes:02d}:{seconds:02d}.{millis:03d}"


class ResultsStore:
    def __init__(self, conn: sqlite3.Connection):
        self._conn = conn
        self._data_version = 0

    @property
    def data_version(self) -> int:
        """Compteur monotone bumpé à chaque mutation — pilote le flux live (SSE)."""
        return self._data_version

    def _touch(self) -> None:
        self._data_version += 1

    # --- tenants ---

    def list_tenants(self) -> list[dict[str, Any]]:
        rows = self._conn.execute(
            """SELECT t.*, COUNT(s.id) AS sim_count
               FROM tenants t
               LEFT JOIN simulators s ON s.tenant_id = t.id
               GROUP BY t.id
               ORDER BY t.label COLLATE NOCASE"""
        ).fetchall()
        return [dict(r) for r in rows]

    def get_tenant(self, tenant_id: str) -> dict[str, Any] | None:
        row = self._conn.execute("SELECT * FROM tenants WHERE id = ?", (tenant_id,)).fetchone()
        return dict(row) if row else None

    def resolve_tenant(self, key: str) -> dict[str, Any] | None:
        """Résout une organisation par id interne, puis par slug friendly."""
        if not key:
            return None
        row = self._conn.execute("SELECT * FROM tenants WHERE id = ?", (key,)).fetchone()
        if row is not None:
            return dict(row)
        row = self._conn.execute("SELECT * FROM tenants WHERE slug = ?", (key,)).fetchone()
        return dict(row) if row else None

    def _resolve_tenant_id(self, key: str) -> str:
        tenant = self.resolve_tenant(key)
        if tenant is None:
            raise ValueError("Organisation introuvable.")
        return tenant["id"]

    def _unique_slug(self, base: str, exclude_id: str | None = None) -> str:
        slug = slugify(base)
        if slug in RESERVED_TENANT_SLUGS:
            slug = f"{slug}-org"
        candidate = slug
        n = 2
        while True:
            if candidate in RESERVED_TENANT_SLUGS:
                candidate = f"{slug}-{n}"
                n += 1
                continue
            if exclude_id:
                row = self._conn.execute(
                    "SELECT id FROM tenants WHERE slug = ? AND id != ?",
                    (candidate, exclude_id),
                ).fetchone()
            else:
                row = self._conn.execute(
                    "SELECT id FROM tenants WHERE slug = ?", (candidate,)
                ).fetchone()
            if row is None:
                return candidate
            candidate = f"{slug}-{n}"
            n += 1

    def create_tenant(self, label: str, visibility: str = "public") -> dict[str, Any]:
        tenant_id = uuid.uuid4().hex
        label = (label or "Organisation").strip() or "Organisation"
        if visibility not in TENANT_VISIBILITIES:
            visibility = "public"
        slug = self._unique_slug(label)
        self._conn.execute(
            "INSERT INTO tenants (id, label, visibility, slug, created_at) VALUES (?, ?, ?, ?, ?)",
            (tenant_id, label, visibility, slug, db.utcnow()),
        )
        self._conn.commit()
        self._touch()
        tenant = self.get_tenant(tenant_id)
        assert tenant is not None
        return tenant

    def update_tenant(
        self,
        tenant_id: str,
        label: str | None = None,
        visibility: str | None = None,
        slug: str | None = None,
    ) -> dict[str, Any]:
        tenant = self.get_tenant(tenant_id)
        if tenant is None:
            raise ValueError("Organisation introuvable.")
        if label is not None:
            label = label.strip()
            if not label:
                raise ValueError("Le nom ne peut pas être vide.")
            self._conn.execute(
                "UPDATE tenants SET label = ? WHERE id = ?", (label[:60], tenant_id)
            )
        if visibility is not None:
            if visibility not in TENANT_VISIBILITIES:
                raise ValueError("Visibilité invalide.")
            self._conn.execute(
                "UPDATE tenants SET visibility = ? WHERE id = ?", (visibility, tenant_id)
            )
        if slug is not None:
            slug = slugify(slug.strip() or (label or tenant["label"]))
            if slug in RESERVED_TENANT_SLUGS:
                raise ValueError("Ce slug est réservé.")
            existing = self._conn.execute(
                "SELECT id FROM tenants WHERE slug = ? AND id != ?", (slug, tenant_id)
            ).fetchone()
            if existing is not None:
                raise ValueError("Ce slug est déjà utilisé.")
            self._conn.execute(
                "UPDATE tenants SET slug = ? WHERE id = ?", (slug, tenant_id)
            )
        self._conn.commit()
        self._touch()
        updated = self.get_tenant(tenant_id)
        assert updated is not None
        return updated

    def delete_tenant(self, tenant_id: str) -> None:
        if self.get_tenant(tenant_id) is None:
            raise ValueError("Organisation introuvable.")
        count = self._conn.execute(
            "SELECT COUNT(*) FROM simulators WHERE tenant_id = ?", (tenant_id,)
        ).fetchone()[0]
        if count:
            raise ValueError(
                "Organisation non vide : déplace ou supprime d'abord ses simulateurs."
            )
        self._conn.execute("DELETE FROM user_tenant_access WHERE tenant_id = ?", (tenant_id,))
        self._conn.execute("DELETE FROM tenants WHERE id = ?", (tenant_id,))
        self._conn.commit()
        self._touch()

    def list_simulators_for_tenant(self, tenant_id: str) -> list[dict[str, Any]]:
        rows = self._conn.execute(
            "SELECT * FROM simulators WHERE tenant_id = ? ORDER BY label COLLATE NOCASE",
            (tenant_id,),
        ).fetchall()
        return [self._with_presence(dict(r)) for r in rows]

    def list_simulators_for_tenants(self, tenant_ids: list[str]) -> list[dict[str, Any]]:
        if not tenant_ids:
            return []
        placeholders = ",".join("?" * len(tenant_ids))
        rows = self._conn.execute(
            f"""SELECT s.*, t.label AS tenant_label
                FROM simulators s
                JOIN tenants t ON t.id = s.tenant_id
                WHERE s.tenant_id IN ({placeholders})
                ORDER BY t.label COLLATE NOCASE, s.label COLLATE NOCASE""",
            tenant_ids,
        ).fetchall()
        return [self._with_presence(dict(r)) for r in rows]

    def _sims_for_scope(
        self,
        tenant_id: str,
        scope_tenant_ids: list[str] | None = None,
    ) -> list[dict[str, Any]]:
        if tenant_id == ALL_TENANT_KEY:
            return self.list_simulators_for_tenants(scope_tenant_ids or [])
        return self.list_simulators_for_tenant(tenant_id)

    def _sim_ids_and_labels(
        self,
        tenant_id: str,
        scope_tenant_ids: list[str] | None = None,
    ) -> tuple[list[str], dict[str, str]]:
        sims = self._sims_for_scope(tenant_id, scope_tenant_ids)
        sim_ids = [s["id"] for s in sims]
        labels: dict[str, str] = {}
        aggregate = tenant_id == ALL_TENANT_KEY
        for s in sims:
            label = s.get("label") or ""
            org = (s.get("tenant_label") or "").strip()
            labels[s["id"]] = f"{org} · {label}" if aggregate and org else label
        return sim_ids, labels

    def assign_simulator_to_tenant(self, sim_id: str, tenant_id: str) -> bool:
        if self.get_simulator(sim_id) is None:
            return False
        try:
            tid = self._resolve_tenant_id(tenant_id)
        except ValueError:
            return False
        self._conn.execute(
            "UPDATE simulators SET tenant_id = ? WHERE id = ?",
            (tid, sim_id),
        )
        self._conn.commit()
        self._touch()
        return True

    def update_simulator(
        self, sim_id: str, label: str | None = None, tenant_id: str | None = None
    ) -> dict[str, Any]:
        sim = self.get_simulator(sim_id)
        if sim is None:
            raise ValueError("Simulateur introuvable.")
        if label is not None:
            label = label.strip()
            if not label:
                raise ValueError("Le nom ne peut pas être vide.")
            self._conn.execute(
                "UPDATE simulators SET label = ? WHERE id = ?", (label[:40], sim_id)
            )
        if tenant_id is not None and tenant_id != sim.get("tenant_id"):
            tid = self._resolve_tenant_id(tenant_id)
            self._conn.execute(
                "UPDATE simulators SET tenant_id = ? WHERE id = ?", (tid, sim_id)
            )
        self._conn.commit()
        self._touch()
        updated = self.get_simulator(sim_id)
        assert updated is not None
        return updated

    def delete_simulator(self, sim_id: str) -> bool:
        if self.get_simulator(sim_id) is None:
            return False
        self._conn.execute("DELETE FROM laps WHERE simulator_id = ?", (sim_id,))
        self._conn.execute("DELETE FROM contests WHERE simulator_id = ?", (sim_id,))
        self._conn.execute("DELETE FROM jobs WHERE simulator_id = ?", (sim_id,))
        self._conn.execute("DELETE FROM simulators WHERE id = ?", (sim_id,))
        self._conn.commit()
        self._touch()
        return True

    def regenerate_token(self, sim_id: str) -> str | None:
        if self.get_simulator(sim_id) is None:
            return None
        token = secrets.token_urlsafe(32)
        self._conn.execute(
            "UPDATE simulators SET token_hash = ? WHERE id = ?",
            (hash_token(token), sim_id),
        )
        self._conn.commit()
        return token

    # --- simulators ---

    def list_simulators(self, tenant_id: str | None = None) -> list[dict[str, Any]]:
        if tenant_id:
            rows = self._conn.execute(
                "SELECT * FROM simulators WHERE tenant_id = ? ORDER BY label COLLATE NOCASE",
                (tenant_id,),
            ).fetchall()
        else:
            rows = self._conn.execute(
                "SELECT * FROM simulators ORDER BY label COLLATE NOCASE"
            ).fetchall()
        return [self._with_presence(dict(r)) for r in rows]

    def get_simulator(self, sim_id: str) -> dict[str, Any] | None:
        row = self._conn.execute("SELECT * FROM simulators WHERE id = ?", (sim_id,)).fetchone()
        return self._with_presence(dict(row)) if row else None

    def get_simulator_by_client_id(self, client_id: str) -> dict[str, Any] | None:
        if not client_id:
            return None
        row = self._conn.execute(
            "SELECT * FROM simulators WHERE client_id = ?", (client_id.strip(),)
        ).fetchone()
        return dict(row) if row else None

    def get_by_token(self, token: str) -> dict[str, Any] | None:
        if not token:
            return None
        row = self._conn.execute(
            "SELECT * FROM simulators WHERE token_hash = ?", (hash_token(token),)
        ).fetchone()
        return dict(row) if row else None

    def _insert_simulator(
        self,
        label: str,
        tenant_id: str,
        token: str,
        client_id: str | None = None,
    ) -> dict[str, Any]:
        sim_id = uuid.uuid4().hex
        self._conn.execute(
            """INSERT INTO simulators (id, tenant_id, label, token_hash, client_id)
               VALUES (?, ?, ?, ?, ?)""",
            (sim_id, tenant_id, label.strip() or "Simulateur", hash_token(token), client_id),
        )
        self._conn.commit()
        self._touch()
        sim = self.get_simulator(sim_id)
        assert sim is not None
        return sim

    def create_simulator(self, label: str, tenant_id: str | None = None) -> tuple[dict[str, Any], str]:
        token = secrets.token_urlsafe(32)
        if tenant_id:
            tid = self._resolve_tenant_id(tenant_id)
        else:
            tid = self.create_tenant(label)["id"]
        sim = self._insert_simulator(label, tid, token)
        return sim, token

    def register_simulator(self, label: str, client_id: str) -> tuple[dict[str, Any], dict[str, Any], str]:
        """Auto-provision: 1 tenant + 1 sim on first connect (or re-issue token for known client_id)."""
        label = (label or "Simulateur").strip() or "Simulateur"
        client_id = (client_id or "").strip()
        if not client_id:
            raise ValueError("simulatorId requis")

        token = secrets.token_urlsafe(32)
        existing = self.get_simulator_by_client_id(client_id)
        if existing:
            self._conn.execute(
                "UPDATE simulators SET token_hash = ?, label = ? WHERE id = ?",
                (hash_token(token), label, existing["id"]),
            )
            self._conn.commit()
            self._touch()
            sim = self.get_simulator(existing["id"])
            tenant = self.get_tenant(existing["tenant_id"])
            assert sim is not None and tenant is not None
            return tenant, sim, token

        tenant = self.create_tenant(label)
        sim = self._insert_simulator(label, tenant["id"], token, client_id)
        return tenant, sim, token

    def _with_presence(self, sim: dict[str, Any]) -> dict[str, Any]:
        sim["connected"] = is_simulator_connected(
            sim.get("last_seen_utc"), sim.get("sync_interval_seconds")
        )
        return sim

    # --- ingest (sim → server, never wipes history by omission) ---

    def ingest(self, sim: dict[str, Any], payload: dict[str, Any]) -> list[dict[str, Any]]:
        sim_id = sim["id"]
        applied = payload.get("appliedCommandIds") or []
        self._ack_jobs(sim_id, applied)

        for entry_id in payload.get("deletedEntryIds") or []:
            self._soft_delete_lap(sim_id, entry_id)

        self._upsert_board(sim_id, None, payload.get("global") or {})
        for contest in payload.get("contests") or []:
            self._upsert_contest(sim_id, contest)
            self._upsert_board(sim_id, contest.get("id"), {"tracks": contest.get("tracks") or []})

        interval = int(payload.get("syncIntervalSeconds") or 120)
        interval = max(15, min(interval, 600))
        label = (payload.get("simulatorLabel") or sim["label"]).strip() or sim["label"]
        self._conn.execute(
            """UPDATE simulators
               SET last_seen_utc = ?, sync_interval_seconds = ?, player_name = ?,
                   current_track_id = ?, current_track_name = ?, client_id = ?, label = ?
               WHERE id = ?""",
            (
                db.utcnow(),
                interval,
                payload.get("playerName") or "",
                int(payload.get("currentTrackId") if payload.get("currentTrackId") is not None else -1),
                payload.get("currentTrackName") or "",
                payload.get("simulatorId") or sim.get("client_id"),
                label,
                sim_id,
            ),
        )
        self._conn.commit()
        self._touch()
        return self.pending_jobs(sim_id)

    def _upsert_contest(self, sim_id: str, contest: dict[str, Any]) -> None:
        cid = contest.get("id")
        if not cid:
            return
        self._conn.execute(
            """INSERT INTO contests (simulator_id, id, name, status, track_filter, created_at, started_at, stopped_at)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?)
               ON CONFLICT(simulator_id, id) DO UPDATE SET
                 name = excluded.name,
                 status = excluded.status,
                 track_filter = excluded.track_filter,
                 started_at = excluded.started_at,
                 stopped_at = excluded.stopped_at""",
            (
                sim_id,
                cid,
                contest.get("name") or "Concours",
                contest.get("status") or "draft",
                contest.get("trackFilter"),
                contest.get("createdAt"),
                contest.get("startedAt"),
                contest.get("stoppedAt"),
            ),
        )

    def _upsert_board(self, sim_id: str, contest_id: str | None, board: dict[str, Any]) -> None:
        for track in board.get("tracks") or []:
            track_id = int(track.get("trackId", -1))
            if track_id < 0:
                continue
            track_name = track.get("trackName") or f"Circuit {track_id}"
            for entry in track.get("entries") or []:
                entry_id = entry.get("id")
                name = (entry.get("name") or "").strip()[:MAX_PLAYER_NAME_LENGTH]
                if not entry_id or not entry.get("bestLapMs") or not name:
                    continue
                self._conn.execute(
                    """INSERT INTO laps (simulator_id, id, contest_id, track_id, track_name, name, best_lap_ms, started_at, deleted_at)
                       VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL)
                       ON CONFLICT(simulator_id, id) DO UPDATE SET
                         contest_id = excluded.contest_id,
                         track_id = excluded.track_id,
                         track_name = excluded.track_name,
                         name = excluded.name,
                         best_lap_ms = excluded.best_lap_ms,
                         started_at = excluded.started_at
                       WHERE laps.deleted_at IS NULL""",
                    (
                        sim_id,
                        entry_id,
                        contest_id,
                        track_id,
                        track_name,
                        name,
                        int(entry["bestLapMs"]),
                        entry.get("startedAt") or db.utcnow(),
                    ),
                )

    def _soft_delete_lap(self, sim_id: str, entry_id: str) -> dict[str, Any] | None:
        row = self._conn.execute(
            "SELECT * FROM laps WHERE simulator_id = ? AND id = ?", (sim_id, entry_id)
        ).fetchone()
        if row is None:
            return None
        self._conn.execute(
            "UPDATE laps SET deleted_at = COALESCE(deleted_at, ?) WHERE simulator_id = ? AND id = ?",
            (db.utcnow(), sim_id, entry_id),
        )
        return dict(row)

    def _restore_lap_row(self, sim_id: str, entry_id: str) -> None:
        self._conn.execute(
            "UPDATE laps SET deleted_at = NULL WHERE simulator_id = ? AND id = ?",
            (sim_id, entry_id),
        )

    # --- queries ---

    def list_contests(self, sim_id: str) -> list[dict[str, Any]]:
        rows = self._conn.execute(
            "SELECT * FROM contests WHERE simulator_id = ? ORDER BY created_at DESC",
            (sim_id,),
        ).fetchall()
        out = []
        for row in rows:
            item = dict(row)
            item["score_count"] = self._conn.execute(
                f"""SELECT COUNT(*) FROM laps
                   WHERE simulator_id = ? AND contest_id = ? AND {LAP_VALID_SQL}""",
                (sim_id, item["id"]),
            ).fetchone()[0]
            out.append(item)
        return out

    def get_contest(self, sim_id: str, contest_id: str) -> dict[str, Any] | None:
        row = self._conn.execute(
            "SELECT * FROM contests WHERE simulator_id = ? AND id = ?",
            (sim_id, contest_id),
        ).fetchone()
        return dict(row) if row else None

    def track_summaries(self, sim_id: str, contest_id: str | None = None) -> list[dict[str, Any]]:
        if contest_id:
            rows = self._conn.execute(
                f"""SELECT track_id, track_name, COUNT(*) AS score_count
                   FROM laps
                   WHERE simulator_id = ? AND contest_id = ? AND {LAP_VALID_SQL}
                   GROUP BY track_id
                   ORDER BY track_name COLLATE NOCASE""",
                (sim_id, contest_id),
            ).fetchall()
        else:
            rows = self._conn.execute(
                f"""SELECT track_id, track_name, COUNT(*) AS score_count
                   FROM laps
                   WHERE simulator_id = ? AND contest_id IS NULL AND {LAP_VALID_SQL}
                   GROUP BY track_id
                   ORDER BY track_name COLLATE NOCASE""",
                (sim_id,),
            ).fetchall()
        return [dict(r) for r in rows]

    def _sim_ids_for_tenant(self, tenant_id: str) -> list[str]:
        rows = self._conn.execute(
            "SELECT id FROM simulators WHERE tenant_id = ?",
            (tenant_id,),
        ).fetchall()
        return [r["id"] for r in rows]

    def tenant_track_summaries(
        self,
        tenant_id: str,
        scope_tenant_ids: list[str] | None = None,
    ) -> list[dict[str, Any]]:
        sim_ids, _ = self._sim_ids_and_labels(tenant_id, scope_tenant_ids)
        if not sim_ids:
            return []
        placeholders = ",".join("?" * len(sim_ids))
        rows = self._conn.execute(
            f"""SELECT track_id, MIN(track_name) AS track_name, COUNT(*) AS score_count
                FROM laps
                WHERE simulator_id IN ({placeholders})
                  AND contest_id IS NULL AND {LAP_VALID_SQL}
                GROUP BY track_id
                ORDER BY track_name COLLATE NOCASE""",
            sim_ids,
        ).fetchall()
        return [dict(r) for r in rows]

    def tenant_leaderboard(
        self,
        tenant_id: str,
        track_id: int,
        best_per_player: bool = False,
        page: int = 1,
        page_size: int = DEFAULT_PAGE_SIZE,
        scope_tenant_ids: list[str] | None = None,
    ) -> dict[str, Any]:
        sim_ids, labels = self._sim_ids_and_labels(tenant_id, scope_tenant_ids)
        if not sim_ids:
            return self._paginate([], page, page_size)
        placeholders = ",".join("?" * len(sim_ids))
        rows = self._conn.execute(
            f"""SELECT * FROM laps
                WHERE simulator_id IN ({placeholders})
                  AND contest_id IS NULL AND track_id = ? AND {LAP_VALID_SQL}
                ORDER BY best_lap_ms ASC, started_at ASC""",
            (*sim_ids, track_id),
        ).fetchall()
        entries = [dict(r) for r in rows]
        for e in entries:
            e["sim_label"] = labels.get(e["simulator_id"], "")
        entries = self._dedupe_best(entries) if best_per_player else entries
        return self._paginate(entries, page, page_size)

    @staticmethod
    def _dedupe_best(entries: list[dict[str, Any]]) -> list[dict[str, Any]]:
        best: dict[str, dict[str, Any]] = {}
        for e in entries:
            name = (e.get("name") or "").strip()
            if not name:
                continue
            key = name.casefold()
            prev = best.get(key)
            if prev is None or e["best_lap_ms"] < prev["best_lap_ms"]:
                best[key] = e
        return sorted(best.values(), key=lambda x: (x["best_lap_ms"], x["started_at"]))

    def _paginate(
        self, entries: list[dict[str, Any]], page: int, page_size: int
    ) -> dict[str, Any]:
        page, page_size = _normalize_page(page, page_size)
        total = len(entries)
        start = (page - 1) * page_size
        return {
            "rows": self._rank_entries(entries[start : start + page_size], start),
            "total": total,
            "page": page,
            "page_size": page_size,
            "pages": max(1, -(-total // page_size)),
        }

    def _rank_entries(
        self, entries: list[dict[str, Any]], offset: int = 0
    ) -> list[dict[str, Any]]:
        ranked = []
        for i, e in enumerate(entries, start=1):
            e["rank"] = offset + i
            e["formatted"] = format_lap(int(e["best_lap_ms"]))
            ranked.append(e)
        return ranked

    def leaderboard(
        self,
        sim_id: str,
        track_id: int,
        contest_id: str | None = None,
        best_per_player: bool = False,
        page: int = 1,
        page_size: int = DEFAULT_PAGE_SIZE,
    ) -> dict[str, Any]:
        if contest_id:
            rows = self._conn.execute(
                f"""SELECT * FROM laps
                   WHERE simulator_id = ? AND contest_id = ? AND track_id = ? AND {LAP_VALID_SQL}
                   ORDER BY best_lap_ms ASC, started_at ASC""",
                (sim_id, contest_id, track_id),
            ).fetchall()
        else:
            rows = self._conn.execute(
                f"""SELECT * FROM laps
                   WHERE simulator_id = ? AND contest_id IS NULL AND track_id = ? AND {LAP_VALID_SQL}
                   ORDER BY best_lap_ms ASC, started_at ASC""",
                (sim_id, track_id),
            ).fetchall()

        entries = [dict(r) for r in rows]
        entries = self._dedupe_best(entries) if best_per_player else entries
        return self._paginate(entries, page, page_size)

    @staticmethod
    def _normalize_recent_limit(limit: int) -> int:
        if limit < 1:
            return DEFAULT_RECENT_LAPS
        return min(limit, MAX_RECENT_LAPS)

    def _prepare_recent_rows(
        self,
        rows: list[Any],
        sim_labels: dict[str, str] | None = None,
    ) -> list[dict[str, Any]]:
        entries: list[dict[str, Any]] = []
        for row in rows:
            e = dict(row)
            e["formatted"] = format_lap(int(e["best_lap_ms"]))
            if sim_labels is not None:
                e["sim_label"] = sim_labels.get(e["simulator_id"], "")
            entries.append(e)
        return entries

    def recent_laps(
        self,
        sim_id: str,
        contest_id: str | None = None,
        limit: int = DEFAULT_RECENT_LAPS,
    ) -> list[dict[str, Any]]:
        limit = self._normalize_recent_limit(limit)
        if contest_id:
            rows = self._conn.execute(
                f"""SELECT * FROM laps
                   WHERE simulator_id = ? AND contest_id = ? AND {LAP_VALID_SQL}
                   ORDER BY started_at DESC LIMIT ?""",
                (sim_id, contest_id, limit),
            ).fetchall()
        else:
            rows = self._conn.execute(
                f"""SELECT * FROM laps
                   WHERE simulator_id = ? AND contest_id IS NULL AND {LAP_VALID_SQL}
                   ORDER BY started_at DESC LIMIT ?""",
                (sim_id, limit),
            ).fetchall()
        return self._prepare_recent_rows(rows)

    def tenant_recent_laps(
        self,
        tenant_id: str,
        limit: int = DEFAULT_RECENT_LAPS,
        scope_tenant_ids: list[str] | None = None,
    ) -> list[dict[str, Any]]:
        limit = self._normalize_recent_limit(limit)
        sim_ids, labels = self._sim_ids_and_labels(tenant_id, scope_tenant_ids)
        if not sim_ids:
            return []
        placeholders = ",".join("?" * len(sim_ids))
        rows = self._conn.execute(
            f"""SELECT * FROM laps
                WHERE simulator_id IN ({placeholders})
                  AND contest_id IS NULL AND {LAP_VALID_SQL}
                ORDER BY started_at DESC LIMIT ?""",
            (*sim_ids, limit),
        ).fetchall()
        return self._prepare_recent_rows(rows, labels)

    def _tenant_lap_counts_by_player(
        self,
        tenant_id: str,
        scope_tenant_ids: list[str] | None = None,
    ) -> dict[str, int]:
        """Nombre total de tours valides (global) par pilote, clé = name.casefold()."""
        sim_ids, _ = self._sim_ids_and_labels(tenant_id, scope_tenant_ids)
        if not sim_ids:
            return {}
        placeholders = ",".join("?" * len(sim_ids))
        rows = self._conn.execute(
            f"""SELECT name, COUNT(*) AS lap_count
                FROM laps
                WHERE simulator_id IN ({placeholders})
                  AND contest_id IS NULL AND {LAP_VALID_SQL}
                GROUP BY name COLLATE NOCASE""",
            sim_ids,
        ).fetchall()
        out: dict[str, int] = {}
        for row in rows:
            name = (row["name"] or "").strip()
            if not name:
                continue
            out[name.casefold()] = int(row["lap_count"])
        return out

    def tenant_championship(
        self,
        tenant_id: str,
        scope_tenant_ids: list[str] | None = None,
    ) -> dict[str, Any]:
        """Classement à points (web) : meilleur tour / pilote / circuit → points P1…Pn."""
        points = self.get_points_by_place()
        tracks = self.tenant_track_summaries(tenant_id, scope_tenant_ids)
        lap_counts = self._tenant_lap_counts_by_player(tenant_id, scope_tenant_ids)
        # name_key → aggregats
        totals: dict[str, dict[str, Any]] = {}

        for track in tracks:
            need = max(len(points), 1)
            board = self.tenant_leaderboard(
                tenant_id,
                int(track["track_id"]),
                best_per_player=True,
                page=1,
                page_size=min(need, MAX_PAGE_SIZE),
                scope_tenant_ids=scope_tenant_ids,
            )
            rows = board.get("rows") or []

            for place, entry in enumerate(rows):
                if place >= len(points):
                    break
                name = (entry.get("name") or "").strip()
                if not name or name == "—":
                    continue
                key = name.casefold()
                bucket = totals.get(key)
                if bucket is None:
                    bucket = {
                        "name": name,
                        "points": 0,
                        "wins": 0,
                        "podiums": 0,
                        "scoring_places": 0,
                        "tracks": 0,
                        "total_laps": 0,
                    }
                    totals[key] = bucket
                pts = points[place]
                bucket["points"] += pts
                bucket["tracks"] += 1
                if pts > 0:
                    bucket["scoring_places"] += 1
                if place == 0:
                    bucket["wins"] += 1
                if place < 3:
                    bucket["podiums"] += 1

        for key, bucket in totals.items():
            bucket["total_laps"] = lap_counts.get(key, 0)

        standings = sorted(
            totals.values(),
            key=lambda r: (-int(r["points"]), -int(r["wins"]), r["name"].casefold()),
        )
        for i, row in enumerate(standings, start=1):
            row["rank"] = i

        return {
            "points_by_place": points,
            "points_by_place_raw": format_points_by_place(points),
            "tracks_counted": len(tracks),
            "standings": standings,
        }

    def tenant_laps_for_player(
        self,
        tenant_id: str,
        player_name: str,
        *,
        limit: int | None = None,
        scope_tenant_ids: list[str] | None = None,
    ) -> list[dict[str, Any]]:
        name = (player_name or "").strip()
        if not name:
            return []
        sim_ids, labels = self._sim_ids_and_labels(tenant_id, scope_tenant_ids)
        if not sim_ids:
            return []
        placeholders = ",".join("?" * len(sim_ids))
        sql = f"""SELECT * FROM laps
                  WHERE simulator_id IN ({placeholders})
                    AND contest_id IS NULL AND {LAP_VALID_SQL}
                    AND name = ? COLLATE NOCASE
                  ORDER BY started_at DESC"""
        params: list[Any] = [*sim_ids, name]
        if limit is not None:
            sql += " LIMIT ?"
            params.append(self._normalize_recent_limit(limit))
        rows = self._conn.execute(sql, params).fetchall()
        return self._prepare_recent_rows(rows, labels)

    @staticmethod
    def _best_per_track(entries: list[dict[str, Any]]) -> list[dict[str, Any]]:
        best: dict[int, dict[str, Any]] = {}
        for e in entries:
            tid = int(e["track_id"])
            prev = best.get(tid)
            if prev is None or e["best_lap_ms"] < prev["best_lap_ms"]:
                best[tid] = e
        return sorted(
            best.values(),
            key=lambda x: ((x.get("track_name") or "").casefold(), x["track_id"]),
        )

    def tenant_pilot_profile(
        self,
        tenant_id: str,
        player_name: str,
        scope_tenant_ids: list[str] | None = None,
    ) -> dict[str, Any]:
        """Stats + meilleurs tours + récents pour un pseudo (global org)."""
        name = (player_name or "").strip()
        all_laps = self.tenant_laps_for_player(
            tenant_id, name, scope_tenant_ids=scope_tenant_ids
        )
        bests = self._best_per_track(all_laps)
        for i, e in enumerate(bests, start=1):
            e["rank"] = i
            e["formatted"] = e.get("formatted") or format_lap(int(e["best_lap_ms"]))
        recent = all_laps[:DEFAULT_RECENT_LAPS]
        champ = self.tenant_championship(tenant_id, scope_tenant_ids)
        standing = next(
            (
                s
                for s in champ["standings"]
                if (s.get("name") or "").casefold() == name.casefold()
            ),
            None,
        )
        return {
            "name": standing["name"] if standing else (all_laps[0]["name"] if all_laps else name),
            "total_laps": len(all_laps),
            "tracks_driven": len(bests),
            "experience": standing,
            "bests": bests,
            "recent": recent,
        }

    def tenant_pilot_names(
        self,
        tenant_id: str,
        scope_tenant_ids: list[str] | None = None,
    ) -> list[str]:
        """Pseudos ayant au moins un chrono valide (global) dans la portée."""
        sim_ids, _ = self._sim_ids_and_labels(tenant_id, scope_tenant_ids)
        if not sim_ids:
            return []
        placeholders = ",".join("?" * len(sim_ids))
        rows = self._conn.execute(
            f"""SELECT name, COUNT(*) AS n
                FROM laps
                WHERE simulator_id IN ({placeholders})
                  AND contest_id IS NULL AND {LAP_VALID_SQL}
                GROUP BY name COLLATE NOCASE
                ORDER BY n DESC, name COLLATE NOCASE""",
            sim_ids,
        ).fetchall()
        out: list[str] = []
        for row in rows:
            name = (row["name"] or "").strip()
            if name:
                out.append(name)
        return out

    def tenant_versus(
        self,
        tenant_id: str,
        name_a: str,
        name_b: str,
        scope_tenant_ids: list[str] | None = None,
    ) -> dict[str, Any]:
        """Comparaison tête-à-tête : écarts absolus / relatifs + duel par circuit."""
        a = (name_a or "").strip()
        b = (name_b or "").strip()
        if not a or not b:
            raise ValueError("Indiquez deux pseudos.")
        if a.casefold() == b.casefold():
            raise ValueError("Choisissez deux pilotes différents.")

        profile_a = self.tenant_pilot_profile(tenant_id, a, scope_tenant_ids)
        profile_b = self.tenant_pilot_profile(tenant_id, b, scope_tenant_ids)
        display_a = profile_a["name"]
        display_b = profile_b["name"]

        bests_a = {int(e["track_id"]): e for e in profile_a["bests"]}
        bests_b = {int(e["track_id"]): e for e in profile_b["bests"]}
        track_ids = sorted(set(bests_a) | set(bests_b))

        tracks: list[dict[str, Any]] = []
        relative_samples: list[float] = []
        wins_a = wins_b = ties = 0

        for tid in track_ids:
            ea = bests_a.get(tid)
            eb = bests_b.get(tid)
            track_name = (
                (ea or eb or {}).get("track_name") or f"Circuit {tid}"
            )
            row: dict[str, Any] = {
                "track_id": tid,
                "track_name": track_name,
                "a_ms": int(ea["best_lap_ms"]) if ea else None,
                "b_ms": int(eb["best_lap_ms"]) if eb else None,
                "a_formatted": (ea or {}).get("formatted"),
                "b_formatted": (eb or {}).get("formatted"),
                "gap_ms": None,
                "relative_pct": None,
                "winner": None,
            }
            if ea and eb:
                gap = int(ea["best_lap_ms"]) - int(eb["best_lap_ms"])
                row["gap_ms"] = gap
                # % de A par rapport à B : négatif = A plus rapide
                row["relative_pct"] = round(gap / float(eb["best_lap_ms"]) * 100.0, 3)
                relative_samples.append(row["relative_pct"])
                if gap < 0:
                    row["winner"] = "a"
                    wins_a += 1
                elif gap > 0:
                    row["winner"] = "b"
                    wins_b += 1
                else:
                    row["winner"] = "tie"
                    ties += 1
            tracks.append(row)

        tracks.sort(key=lambda r: (r.get("track_name") or "").casefold())

        avg_rel = (
            round(sum(relative_samples) / len(relative_samples), 3)
            if relative_samples
            else None
        )
        # Indice de niveau : 100 = égalité ; >100 = A plus rapide en moyenne
        level_index = (
            round(100.0 - avg_rel, 2) if avg_rel is not None else None
        )

        return {
            "a": {
                "name": display_a,
                "total_laps": profile_a["total_laps"],
                "tracks_driven": profile_a["tracks_driven"],
                "experience": profile_a.get("experience"),
            },
            "b": {
                "name": display_b,
                "total_laps": profile_b["total_laps"],
                "tracks_driven": profile_b["tracks_driven"],
                "experience": profile_b.get("experience"),
            },
            "summary": {
                "common_tracks": len(relative_samples),
                "wins_a": wins_a,
                "wins_b": wins_b,
                "ties": ties,
                "avg_relative_pct": avg_rel,
                "level_index": level_index,
                "faster": (
                    "a" if avg_rel is not None and avg_rel < 0
                    else "b" if avg_rel is not None and avg_rel > 0
                    else "tie" if avg_rel == 0
                    else None
                ),
            },
            "tracks": tracks,
        }

    def get_lap(self, sim_id: str, entry_id: str) -> dict[str, Any] | None:
        row = self._conn.execute(
            "SELECT * FROM laps WHERE simulator_id = ? AND id = ?",
            (sim_id, entry_id),
        ).fetchone()
        return dict(row) if row else None

    # --- jobs ---

    @staticmethod
    def _parse_payload(payload_json: str) -> dict[str, Any]:
        try:
            return json.loads(payload_json)
        except (json.JSONDecodeError, TypeError):
            return {}

    def pending_jobs(self, sim_id: str) -> list[dict[str, Any]]:
        rows = self._conn.execute(
            """SELECT * FROM jobs
               WHERE simulator_id = ? AND status = 'pending'
               ORDER BY created_at ASC""",
            (sim_id,),
        ).fetchall()
        jobs = []
        for row in rows:
            job = dict(row)
            job["payload"] = self._parse_payload(job["payload_json"])
            jobs.append(job)
        return jobs

    def jobs_as_commands(self, jobs: list[dict[str, Any]]) -> list[dict[str, Any]]:
        out = []
        for job in jobs:
            p = job["payload"]
            cmd = {
                "id": job["id"],
                "type": job["type"],
                "contestId": p.get("contestId"),
                "entryId": p.get("entryId"),
                "playerName": p.get("playerName"),
                "newName": p.get("newName"),
                "trackName": p.get("trackName"),
                "trackId": p.get("trackId"),
                "bestLapMs": p.get("bestLapMs"),
                "startedAt": p.get("startedAt"),
            }
            out.append({k: v for k, v in cmd.items() if v is not None or k in ("id", "type")})
        return out

    def _ack_jobs(self, sim_id: str, ids: list[str]) -> None:
        if not ids:
            return
        now = db.utcnow()
        for job_id in ids:
            self._conn.execute(
                """UPDATE jobs SET status = 'applied', applied_at = ?
                   WHERE id = ? AND simulator_id = ? AND status IN ('pending', 'delivered')""",
                (now, job_id, sim_id),
            )

    def list_jobs(self, sim_id: str) -> list[dict[str, Any]]:
        rows = self._conn.execute(
            """SELECT * FROM jobs WHERE simulator_id = ?
               ORDER BY created_at DESC LIMIT 200""",
            (sim_id,),
        ).fetchall()
        out = []
        for row in rows:
            job = dict(row)
            job["payload"] = self._parse_payload(job["payload_json"])
            job["can_revert"] = job["status"] in ("pending", "delivered", "applied")
            out.append(job)
        return out

    def _enqueue(self, sim_id: str, job_type: str, payload: dict[str, Any], revert_of: str | None = None) -> str:
        job_id = uuid.uuid4().hex
        self._conn.execute(
            """INSERT INTO jobs (id, simulator_id, type, status, payload_json, revert_of_job_id, created_at)
               VALUES (?, ?, ?, 'pending', ?, ?, ?)""",
            (job_id, sim_id, job_type, json.dumps(payload), revert_of, db.utcnow()),
        )
        self._conn.commit()
        self._touch()
        return job_id

    def admin_delete_entry(self, sim_id: str, entry_id: str) -> bool:
        lap = self.get_lap(sim_id, entry_id)
        if lap is None or lap.get("deleted_at"):
            return False
        self._soft_delete_lap(sim_id, entry_id)
        self._enqueue(
            sim_id,
            "deleteEntry",
            {
                "contestId": lap["contest_id"],
                "entryId": lap["id"],
                "playerName": lap["name"],
                "trackId": lap["track_id"],
                "trackName": lap["track_name"],
                "bestLapMs": lap["best_lap_ms"],
                "startedAt": lap["started_at"],
            },
        )
        return True

    def admin_rename_entry(self, sim_id: str, entry_id: str, new_name: str) -> bool:
        new_name = (new_name or "").strip()[:20]
        lap = self.get_lap(sim_id, entry_id)
        if not new_name or lap is None or lap.get("deleted_at"):
            return False
        old = lap["name"]
        self._conn.execute(
            "UPDATE laps SET name = ? WHERE simulator_id = ? AND id = ?",
            (new_name, sim_id, entry_id),
        )
        self._enqueue(
            sim_id,
            "renameEntry",
            {
                "contestId": lap["contest_id"],
                "entryId": entry_id,
                "playerName": old,
                "newName": new_name,
            },
        )
        return True

    def admin_rename_player(self, sim_id: str, contest_id: str | None, old_name: str, new_name: str) -> int:
        old_name = (old_name or "").strip()
        new_name = (new_name or "").strip()[:20]
        if not old_name or not new_name:
            return 0
        if contest_id:
            cur = self._conn.execute(
                """UPDATE laps SET name = ?
                   WHERE simulator_id = ? AND contest_id = ? AND deleted_at IS NULL
                     AND name = ? COLLATE NOCASE""",
                (new_name, sim_id, contest_id, old_name),
            )
        else:
            cur = self._conn.execute(
                """UPDATE laps SET name = ?
                   WHERE simulator_id = ? AND contest_id IS NULL AND deleted_at IS NULL
                     AND name = ? COLLATE NOCASE""",
                (new_name, sim_id, old_name),
            )
        count = cur.rowcount
        if count > 0:
            self._enqueue(
                sim_id,
                "renamePlayer",
                {
                    "contestId": contest_id,
                    "playerName": old_name,
                    "newName": new_name,
                },
            )
        self._conn.commit()
        return count

    def revert_job(self, sim_id: str, job_id: str) -> str | None:
        row = self._conn.execute(
            "SELECT * FROM jobs WHERE id = ? AND simulator_id = ?",
            (job_id, sim_id),
        ).fetchone()
        if row is None:
            return "introuvable"
        job = dict(row)
        if job["status"] not in ("pending", "delivered", "applied"):
            return "déjà traité"
        payload = self._parse_payload(job["payload_json"])
        if not payload and job["payload_json"]:
            return "payload invalide"
        now = db.utcnow()

        if job["status"] in ("pending", "delivered"):
            self._undo_local(sim_id, job["type"], payload)
            self._conn.execute(
                "UPDATE jobs SET status = 'cancelled', reverted_at = ? WHERE id = ?",
                (now, job_id),
            )
            self._conn.commit()
            self._touch()
            return None

        inverse = self._inverse_job(job["type"], payload)
        if inverse is None:
            return "non revertible"
        self._undo_local(sim_id, job["type"], payload)
        new_id = self._enqueue(sim_id, inverse[0], inverse[1], revert_of=job_id)
        self._conn.execute(
            "UPDATE jobs SET status = 'reverted', reverted_at = ? WHERE id = ?",
            (now, job_id),
        )
        self._conn.commit()
        return None if new_id else "échec"

    def _undo_local(self, sim_id: str, job_type: str, payload: dict[str, Any]) -> None:
        if job_type == "deleteEntry" and payload.get("entryId"):
            existing = self.get_lap(sim_id, payload["entryId"])
            if existing:
                self._restore_lap_row(sim_id, payload["entryId"])
            else:
                self._conn.execute(
                    """INSERT INTO laps (simulator_id, id, contest_id, track_id, track_name, name, best_lap_ms, started_at, deleted_at)
                       VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL)""",
                    (
                        sim_id,
                        payload["entryId"],
                        payload.get("contestId"),
                        payload.get("trackId") or -1,
                        payload.get("trackName") or "Inconnu",
                        payload.get("playerName") or "",
                        payload.get("bestLapMs") or 0,
                        payload.get("startedAt") or db.utcnow(),
                    ),
                )
        elif job_type == "restoreEntry" and payload.get("entryId"):
            self._soft_delete_lap(sim_id, payload["entryId"])
        elif job_type == "renameEntry" and payload.get("entryId"):
            self._conn.execute(
                "UPDATE laps SET name = ? WHERE simulator_id = ? AND id = ?",
                (payload.get("playerName") or "", sim_id, payload["entryId"]),
            )
        elif job_type == "renamePlayer":
            contest_id = payload.get("contestId")
            if contest_id:
                self._conn.execute(
                    """UPDATE laps SET name = ?
                       WHERE simulator_id = ? AND contest_id = ? AND deleted_at IS NULL
                         AND name = ? COLLATE NOCASE""",
                    (payload.get("playerName") or "", sim_id, contest_id, payload.get("newName") or ""),
                )
            else:
                self._conn.execute(
                    """UPDATE laps SET name = ?
                       WHERE simulator_id = ? AND contest_id IS NULL AND deleted_at IS NULL
                         AND name = ? COLLATE NOCASE""",
                    (payload.get("playerName") or "", sim_id, payload.get("newName") or ""),
                )

    def enqueue_set_player_name(self, sim_id: str, new_name: str) -> bool:
        """Session pseudo is owned by the sim: a job asks it to adopt a new one."""
        new_name = (new_name or "").strip()[:MAX_PLAYER_NAME_LENGTH]
        if not new_name or self.get_simulator(sim_id) is None:
            return False
        self._enqueue(sim_id, "setPlayerName", {"newName": new_name})
        return True

    # --- global settings ---

    def get_public_access(self) -> bool:
        row = self._conn.execute(
            "SELECT value FROM settings WHERE key = 'public_access'"
        ).fetchone()
        return True if row is None else row["value"] == "1"

    def set_public_access(self, enabled: bool) -> None:
        self._conn.execute(
            """INSERT INTO settings (key, value) VALUES ('public_access', ?)
               ON CONFLICT(key) DO UPDATE SET value = excluded.value""",
            ("1" if enabled else "0",),
        )
        self._conn.commit()
        self._touch()

    def get_points_by_place(self) -> list[int]:
        row = self._conn.execute(
            "SELECT value FROM settings WHERE key = 'points_by_place'"
        ).fetchone()
        raw = row["value"] if row is not None else DEFAULT_POINTS_BY_PLACE
        try:
            return parse_points_by_place(raw)
        except ValueError:
            return parse_points_by_place(DEFAULT_POINTS_BY_PLACE)

    def get_points_by_place_raw(self) -> str:
        return format_points_by_place(self.get_points_by_place())

    def set_points_by_place(self, raw: str) -> list[int]:
        points = parse_points_by_place(raw)
        self._conn.execute(
            """INSERT INTO settings (key, value) VALUES ('points_by_place', ?)
               ON CONFLICT(key) DO UPDATE SET value = excluded.value""",
            (format_points_by_place(points),),
        )
        self._conn.commit()
        self._touch()
        return points

    def _inverse_job(self, job_type: str, payload: dict[str, Any]) -> tuple[str, dict[str, Any]] | None:
        if job_type == "deleteEntry":
            return "restoreEntry", payload
        if job_type == "restoreEntry":
            return "deleteEntry", payload
        if job_type == "renameEntry":
            return "renameEntry", {
                **payload,
                "playerName": payload.get("newName"),
                "newName": payload.get("playerName"),
            }
        if job_type == "renamePlayer":
            return "renamePlayer", {
                **payload,
                "playerName": payload.get("newName"),
                "newName": payload.get("playerName"),
            }
        return None
