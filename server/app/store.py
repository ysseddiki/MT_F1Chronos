from __future__ import annotations

import hashlib
import json
import secrets
import sqlite3
import uuid
from typing import Any

from . import db
from .online import is_simulator_connected


def hash_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def format_lap(ms: int) -> str:
    if ms <= 0:
        return "--:--.---"
    minutes, rem = divmod(ms, 60_000)
    seconds, millis = divmod(rem, 1000)
    return f"{minutes:02d}:{seconds:02d}.{millis:03d}"


class ResultsStore:
    def __init__(self, conn: sqlite3.Connection):
        self._conn = conn

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

    def create_tenant(self, label: str) -> dict[str, Any]:
        tenant_id = uuid.uuid4().hex
        label = (label or "Organisation").strip() or "Organisation"
        self._conn.execute(
            "INSERT INTO tenants (id, label, created_at) VALUES (?, ?, ?)",
            (tenant_id, label, db.utcnow()),
        )
        self._conn.commit()
        tenant = self.get_tenant(tenant_id)
        assert tenant is not None
        return tenant

    def list_simulators_for_tenant(self, tenant_id: str) -> list[dict[str, Any]]:
        rows = self._conn.execute(
            "SELECT * FROM simulators WHERE tenant_id = ? ORDER BY label COLLATE NOCASE",
            (tenant_id,),
        ).fetchall()
        return [self._with_presence(dict(r)) for r in rows]

    def assign_simulator_to_tenant(self, sim_id: str, tenant_id: str) -> bool:
        if self.get_simulator(sim_id) is None or self.get_tenant(tenant_id) is None:
            return False
        self._conn.execute(
            "UPDATE simulators SET tenant_id = ? WHERE id = ?",
            (tenant_id, sim_id),
        )
        self._conn.commit()
        return True

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
        sim = self.get_simulator(sim_id)
        assert sim is not None
        return sim

    def create_simulator(self, label: str, tenant_id: str | None = None) -> tuple[dict[str, Any], str]:
        token = secrets.token_urlsafe(32)
        if tenant_id:
            if self.get_tenant(tenant_id) is None:
                raise ValueError("tenant introuvable")
            tid = tenant_id
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
            track_name = track.get("trackName") or f"Circuit {track_id}"
            for entry in track.get("entries") or []:
                entry_id = entry.get("id")
                if not entry_id or not entry.get("bestLapMs"):
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
                        (entry.get("name") or "")[:20],
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
                """SELECT COUNT(*) FROM laps
                   WHERE simulator_id = ? AND contest_id = ? AND deleted_at IS NULL""",
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
                """SELECT track_id, track_name, COUNT(*) AS score_count
                   FROM laps
                   WHERE simulator_id = ? AND contest_id = ? AND deleted_at IS NULL
                   GROUP BY track_id
                   ORDER BY track_name COLLATE NOCASE""",
                (sim_id, contest_id),
            ).fetchall()
        else:
            rows = self._conn.execute(
                """SELECT track_id, track_name, COUNT(*) AS score_count
                   FROM laps
                   WHERE simulator_id = ? AND contest_id IS NULL AND deleted_at IS NULL
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

    def tenant_track_summaries(self, tenant_id: str) -> list[dict[str, Any]]:
        sim_ids = self._sim_ids_for_tenant(tenant_id)
        if not sim_ids:
            return []
        placeholders = ",".join("?" * len(sim_ids))
        rows = self._conn.execute(
            f"""SELECT track_id, track_name, COUNT(*) AS score_count
                FROM laps
                WHERE simulator_id IN ({placeholders})
                  AND contest_id IS NULL AND deleted_at IS NULL
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
    ) -> list[dict[str, Any]]:
        sim_ids = self._sim_ids_for_tenant(tenant_id)
        if not sim_ids:
            return []
        labels = {
            s["id"]: s["label"]
            for s in self.list_simulators_for_tenant(tenant_id)
        }
        placeholders = ",".join("?" * len(sim_ids))
        rows = self._conn.execute(
            f"""SELECT * FROM laps
                WHERE simulator_id IN ({placeholders})
                  AND contest_id IS NULL AND track_id = ? AND deleted_at IS NULL
                ORDER BY best_lap_ms ASC, started_at ASC""",
            (*sim_ids, track_id),
        ).fetchall()
        entries = [dict(r) for r in rows]
        for e in entries:
            e["sim_label"] = labels.get(e["simulator_id"], "")
        if best_per_player:
            best: dict[str, dict[str, Any]] = {}
            for e in entries:
                key = e["name"].casefold()
                prev = best.get(key)
                if prev is None or e["best_lap_ms"] < prev["best_lap_ms"]:
                    best[key] = e
            entries = sorted(best.values(), key=lambda x: (x["best_lap_ms"], x["started_at"]))
        return self._rank_entries(entries)

    def _rank_entries(self, entries: list[dict[str, Any]]) -> list[dict[str, Any]]:
        ranked = []
        for i, e in enumerate(entries, start=1):
            e["rank"] = i
            e["formatted"] = format_lap(int(e["best_lap_ms"]))
            ranked.append(e)
        return ranked

    def leaderboard(
        self,
        sim_id: str,
        track_id: int,
        contest_id: str | None = None,
        best_per_player: bool = False,
    ) -> list[dict[str, Any]]:
        if contest_id:
            rows = self._conn.execute(
                """SELECT * FROM laps
                   WHERE simulator_id = ? AND contest_id = ? AND track_id = ? AND deleted_at IS NULL
                   ORDER BY best_lap_ms ASC, started_at ASC""",
                (sim_id, contest_id, track_id),
            ).fetchall()
        else:
            rows = self._conn.execute(
                """SELECT * FROM laps
                   WHERE simulator_id = ? AND contest_id IS NULL AND track_id = ? AND deleted_at IS NULL
                   ORDER BY best_lap_ms ASC, started_at ASC""",
                (sim_id, track_id),
            ).fetchall()

        entries = [dict(r) for r in rows]
        if best_per_player:
            best: dict[str, dict[str, Any]] = {}
            for e in entries:
                key = e["name"].casefold()
                prev = best.get(key)
                if prev is None or e["best_lap_ms"] < prev["best_lap_ms"]:
                    best[key] = e
            entries = sorted(best.values(), key=lambda x: (x["best_lap_ms"], x["started_at"]))

        return self._rank_entries(entries)

    def get_lap(self, sim_id: str, entry_id: str) -> dict[str, Any] | None:
        row = self._conn.execute(
            "SELECT * FROM laps WHERE simulator_id = ? AND id = ?",
            (sim_id, entry_id),
        ).fetchone()
        return dict(row) if row else None

    # --- jobs ---

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
            job["payload"] = json.loads(job["payload_json"])
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
            try:
                job["payload"] = json.loads(job["payload_json"])
            except (json.JSONDecodeError, TypeError):
                job["payload"] = {}
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
        payload = json.loads(job["payload_json"])
        now = db.utcnow()

        if job["status"] in ("pending", "delivered"):
            self._undo_local(sim_id, job["type"], payload)
            self._conn.execute(
                "UPDATE jobs SET status = 'cancelled', reverted_at = ? WHERE id = ?",
                (now, job_id),
            )
            self._conn.commit()
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
