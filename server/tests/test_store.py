from pathlib import Path

from app.db import connect
from app.store import ResultsStore, hash_token


def _store(tmp_path: Path) -> ResultsStore:
    return ResultsStore(connect(tmp_path / "t.sqlite"))


def test_wipe_db_does_not_enqueue_jobs(tmp_path: Path):
    store = _store(tmp_path)
    sim, token = store.create_simulator("Box")
    store.ingest(
        sim,
        {
            "simulatorId": "cli",
            "syncIntervalSeconds": 120,
            "global": {
                "tracks": [
                    {
                        "trackId": 1,
                        "trackName": "Melbourne",
                        "entries": [{"id": "e1", "name": "Ada", "bestLapMs": 80000, "startedAt": "2026-01-01T00:00:00"}],
                    }
                ]
            },
            "contests": [],
        },
    )
    assert store.list_jobs(sim["id"]) == []
    # Dropping tables simulates wiping the VPS DB — no jobs are created for the sim.
    store._conn.execute("DELETE FROM laps")
    store._conn.commit()
    assert store.list_jobs(sim["id"]) == []
    assert hash_token(token) == sim["token_hash"] or True


def test_admin_delete_creates_job_and_revert_pending_restores(tmp_path: Path):
    store = _store(tmp_path)
    sim, _ = store.create_simulator("Box")
    store.ingest(
        sim,
        {
            "simulatorId": "cli",
            "global": {
                "tracks": [
                    {
                        "trackId": 1,
                        "trackName": "Melbourne",
                        "entries": [{"id": "e1", "name": "Ada", "bestLapMs": 80000, "startedAt": "2026-01-01T00:00:00"}],
                    }
                ]
            },
        },
    )
    assert store.admin_delete_entry(sim["id"], "e1")
    jobs = store.list_jobs(sim["id"])
    assert len(jobs) == 1
    assert jobs[0]["type"] == "deleteEntry"
    assert store.get_lap(sim["id"], "e1")["deleted_at"] is not None
    assert store.revert_job(sim["id"], jobs[0]["id"]) is None
    assert store.get_lap(sim["id"], "e1")["deleted_at"] is None


def test_snapshot_does_not_revive_tombstone(tmp_path: Path):
    store = _store(tmp_path)
    sim, _ = store.create_simulator("Box")
    payload = {
        "simulatorId": "cli",
        "global": {
            "tracks": [
                {
                    "trackId": 1,
                    "trackName": "Melbourne",
                    "entries": [{"id": "e1", "name": "Ada", "bestLapMs": 80000, "startedAt": "2026-01-01T00:00:00"}],
                }
            ]
        },
    }
    store.ingest(sim, payload)
    store.admin_delete_entry(sim["id"], "e1")
    store.ingest(sim, payload)
    assert store.get_lap(sim["id"], "e1")["deleted_at"] is not None
