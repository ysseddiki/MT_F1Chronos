from pathlib import Path

from fastapi.testclient import TestClient

from app.main import app


def test_health():
    client = TestClient(app)
    r = client.get("/api/v1/health")
    assert r.status_code == 200
    assert r.json()["ok"] is True


def test_home_empty_db(tmp_path: Path, monkeypatch):
    monkeypatch.setenv("RESULTS_DATA", str(tmp_path))
    # Reset singleton so each test gets a fresh DB under tmp_path.
    import app.main as main

    main._store = None
    main._auth = None

    client = TestClient(app)
    r = client.get("/")
    assert r.status_code == 200
    assert "Classements" in r.text


def test_admin_without_password(tmp_path: Path, monkeypatch):
    monkeypatch.setenv("RESULTS_DATA", str(tmp_path))
    import app.main as main

    main._store = None
    main._auth = None

    client = TestClient(app)
    r = client.get("/admin")
    assert r.status_code == 200
    assert "Gérer les résultats" in r.text
