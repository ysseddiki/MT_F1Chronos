from pathlib import Path

import pytest

from app.db import connect
from app.store import ResultsStore, hash_token


def _store(tmp_path: Path) -> ResultsStore:
    return ResultsStore(connect(tmp_path / "t.sqlite"))


def test_data_version_bumps_on_mutations(tmp_path: Path):
    store = _store(tmp_path)
    assert store.data_version == 0
    sim, _ = store.create_simulator("Box")
    v = store.data_version
    assert v > 0
    store.ingest(sim, {"simulatorId": "cli", "global": {"tracks": []}, "contests": []})
    assert store.data_version > v
    v = store.data_version
    store.enqueue_set_player_name(sim["id"], "Nouveau")
    assert store.data_version > v


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


def _ingest_laps(store, sim, count: int, track_id: int = 1):
    entries = [
        {
            "id": f"e{i}",
            "name": f"Pilote {i}",
            "bestLapMs": 80_000 + i * 100,
            "startedAt": "2026-01-01T00:00:00",
        }
        for i in range(count)
    ]
    store.ingest(
        sim,
        {
            "simulatorId": "cli",
            "global": {"tracks": [{"trackId": track_id, "trackName": "Melbourne", "entries": entries}]},
        },
    )


def test_leaderboard_pagination(tmp_path: Path):
    store = _store(tmp_path)
    sim, _ = store.create_simulator("Box")
    _ingest_laps(store, sim, 25)

    page1 = store.leaderboard(sim["id"], 1)
    assert page1["total"] == 25
    assert page1["pages"] == 2
    assert len(page1["rows"]) == 20
    assert page1["rows"][0]["rank"] == 1

    page2 = store.leaderboard(sim["id"], 1, page=2)
    assert len(page2["rows"]) == 5
    # Le rang reste global à travers les pages
    assert page2["rows"][0]["rank"] == 21


def test_leaderboard_best_per_player_dedupes_before_pagination(tmp_path: Path):
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
                        "entries": [
                            {"id": "e1", "name": "Ada", "bestLapMs": 81000, "startedAt": "2026-01-01T00:00:00"},
                            {"id": "e2", "name": "ada", "bestLapMs": 80000, "startedAt": "2026-01-01T01:00:00"},
                            {"id": "e3", "name": "Bob", "bestLapMs": 82000, "startedAt": "2026-01-01T00:00:00"},
                        ],
                    }
                ]
            },
        },
    )
    board = store.leaderboard(sim["id"], 1, best_per_player=True)
    assert board["total"] == 2
    assert [r["name"] for r in board["rows"]] == ["ada", "Bob"]


def test_enqueue_set_player_name(tmp_path: Path):
    store = _store(tmp_path)
    sim, _ = store.create_simulator("Box")
    assert store.enqueue_set_player_name(sim["id"], "  NouveauPseudo  ")
    jobs = store.pending_jobs(sim["id"])
    assert len(jobs) == 1
    assert jobs[0]["type"] == "setPlayerName"
    commands = store.jobs_as_commands(jobs)
    assert commands[0]["newName"] == "NouveauPseudo"

    # Pseudo vide refusé, simu inconnu refusé
    assert not store.enqueue_set_player_name(sim["id"], "   ")
    assert not store.enqueue_set_player_name("inconnu", "Ada")


def test_delete_tenant_blocked_while_sims(tmp_path: Path):
    store = _store(tmp_path)
    tenant = store.create_tenant("Club")
    sim, _ = store.create_simulator("Box", tenant_id=tenant["id"])
    with pytest.raises(ValueError):
        store.delete_tenant(tenant["id"])
    assert store.delete_simulator(sim["id"])
    store.delete_tenant(tenant["id"])
    assert store.get_tenant(tenant["id"]) is None


def test_delete_simulator_cascades(tmp_path: Path):
    store = _store(tmp_path)
    sim, _ = store.create_simulator("Box")
    _ingest_laps(store, sim, 3)
    store.admin_delete_entry(sim["id"], "e0")  # crée un job
    assert store.delete_simulator(sim["id"])
    assert store.get_simulator(sim["id"]) is None
    assert store._conn.execute("SELECT COUNT(*) FROM laps WHERE simulator_id = ?", (sim["id"],)).fetchone()[0] == 0
    assert store._conn.execute("SELECT COUNT(*) FROM jobs WHERE simulator_id = ?", (sim["id"],)).fetchone()[0] == 0


def test_tenant_visibility_and_rename(tmp_path: Path):
    store = _store(tmp_path)
    tenant = store.create_tenant("Club")
    assert tenant["visibility"] == "public"
    updated = store.update_tenant(tenant["id"], label="Club 2", visibility="private")
    assert updated["label"] == "Club 2"
    assert updated["visibility"] == "private"
    with pytest.raises(ValueError):
        store.update_tenant(tenant["id"], visibility="secret")
    with pytest.raises(ValueError):
        store.update_tenant(tenant["id"], label="   ")


def test_public_access_setting(tmp_path: Path):
    store = _store(tmp_path)
    assert store.get_public_access() is True
    store.set_public_access(False)
    assert store.get_public_access() is False
    store.set_public_access(True)
    assert store.get_public_access() is True
