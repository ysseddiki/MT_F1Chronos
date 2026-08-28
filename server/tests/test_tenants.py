from pathlib import Path

from fastapi.testclient import TestClient

from app.store import ResultsStore, hash_token


def _store(tmp_path: Path) -> ResultsStore:
    from app.db import connect

    return ResultsStore(connect(tmp_path / "t.sqlite"))


def test_register_creates_tenant_and_sim(tmp_path: Path):
    store = _store(tmp_path)
    tenant, sim, token = store.register_simulator("Box A", "client-1")
    assert tenant["label"] == "Box A"
    assert sim["tenant_id"] == tenant["id"]
    assert sim["client_id"] == "client-1"
    assert store.get_by_token(token) is not None


def test_register_same_client_id_reissues_token(tmp_path: Path):
    store = _store(tmp_path)
    _, sim1, token1 = store.register_simulator("Box A", "client-1")
    _, sim2, token2 = store.register_simulator("Box A renamed", "client-1")
    assert sim1["id"] == sim2["id"]
    assert token1 != token2
    assert store.get_by_token(token1) is None
    assert store.get_by_token(token2) is not None


def test_assign_simulator_merges_tenants(tmp_path: Path):
    store = _store(tmp_path)
    t1 = store.create_tenant("Club")
    sim_a, _ = store.create_simulator("Box A", tenant_id=t1["id"])
    sim_b, _ = store.create_simulator("Box B")
    assert sim_a["tenant_id"] != sim_b["tenant_id"]
    assert store.assign_simulator_to_tenant(sim_b["id"], t1["id"])
    assert store.get_simulator(sim_b["id"])["tenant_id"] == t1["id"]
    assert len(store.list_simulators_for_tenant(t1["id"])) == 2


def test_tenant_leaderboard_aggregates(tmp_path: Path):
    store = _store(tmp_path)
    tenant = store.create_tenant("Club")
    sim_a, _ = store.create_simulator("A", tenant_id=tenant["id"])
    sim_b, _ = store.create_simulator("B", tenant_id=tenant["id"])
    for sim, name, ms in [(sim_a, "Ada", 80000), (sim_b, "Bob", 79000)]:
        store.ingest(
            sim,
            {
                "simulatorId": sim["id"],
                "global": {
                    "tracks": [
                        {
                            "trackId": 1,
                            "trackName": "Melbourne",
                            "entries": [
                                {
                                    "id": f"e-{sim['id']}",
                                    "name": name,
                                    "bestLapMs": ms,
                                    "startedAt": "2026-01-01T00:00:00",
                                }
                            ],
                        }
                    ]
                },
            },
        )
    rows = store.tenant_leaderboard(tenant["id"], 1)
    assert len(rows) == 2
    assert rows[0]["name"] == "Bob"


def test_register_api(tmp_path: Path, monkeypatch):
    monkeypatch.setenv("RESULTS_DATA", str(tmp_path))
    import app.main as main

    main._store = None
    main._auth = None
    client = TestClient(main.app)
    r = client.post(
        "/api/v1/register",
        json={"simulatorId": "cli-99", "simulatorLabel": "Auto Box"},
    )
    assert r.status_code == 200
    body = r.json()
    assert body["ok"] is True
    assert body["token"]
    assert body["tenantLabel"] == "Auto Box"
