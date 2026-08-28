from pathlib import Path

import pytest
from fastapi.testclient import TestClient


@pytest.fixture
def client(tmp_path: Path, monkeypatch):
    monkeypatch.setenv("RESULTS_DATA", str(tmp_path))
    from app import deps
    from app.main import app

    deps.reset_state()
    return TestClient(app)


def _setup_admin(client: TestClient, email: str = "admin@localhost", password: str = "motdepasse-admin"):
    r = client.post("/api/v1/auth/setup", json={"email": email, "password": password})
    assert r.status_code == 200, r.text
    return r.json()["user"]


def _make_tenant_with_sim(client: TestClient, label="Club", visibility="public", sim_label="Box 1"):
    r = client.post("/api/v1/admin/tenants", json={"label": label, "visibility": visibility})
    assert r.status_code == 200, r.text
    tenant = r.json()["tenant"]
    r = client.post("/api/v1/admin/simulators", json={"label": sim_label, "tenant_id": tenant["id"]})
    assert r.status_code == 200, r.text
    return tenant, r.json()["sim"], r.json()["token"]


def _sync_laps(client: TestClient, token: str, count: int = 3):
    entries = [
        {
            "id": f"e{i}",
            "name": f"Pilote {i}",
            "bestLapMs": 80000 + i * 100,
            "startedAt": "2026-01-01T00:00:00",
        }
        for i in range(count)
    ]
    r = client.post(
        "/api/v1/sync",
        headers={"X-Results-Token": token},
        json={
            "simulatorId": "cli",
            "syncIntervalSeconds": 120,
            "global": {"tracks": [{"trackId": 1, "trackName": "Melbourne", "entries": entries}]},
        },
    )
    assert r.status_code == 200, r.text
    return r.json()


# ---------------------------------------------------------------------------
# SPA & santé
# ---------------------------------------------------------------------------


def test_health(client):
    r = client.get("/api/v1/health")
    assert r.status_code == 200
    assert r.json()["ok"] is True


def test_spa_fallback_serves_index(client):
    for path in ["/", "/t/quelque-chose", "/admin", "/login"]:
        r = client.get(path)
        assert r.status_code == 200
        assert "text/html" in r.headers["content-type"]
        assert "F1 Chronos" in r.text


def test_unknown_api_route_is_json_404(client):
    r = client.get("/api/v1/nope")
    assert r.status_code == 404
    assert r.json()["ok"] is False


def test_security_headers(client):
    r = client.get("/api/v1/health")
    assert "default-src 'self'" in r.headers["content-security-policy"]
    assert r.headers["x-content-type-options"] == "nosniff"
    assert r.headers["x-frame-options"] == "DENY"
    assert r.headers["cache-control"] == "no-store"


# ---------------------------------------------------------------------------
# Auth : setup, login, rate-limit
# ---------------------------------------------------------------------------


def test_setup_creates_first_admin_and_logs_in(client):
    user = _setup_admin(client)
    assert user["role"] == "admin"
    me = client.get("/api/v1/auth/me").json()
    assert me["authenticated"] is True
    assert me["user"]["email"] == "admin@localhost"
    assert me["setupRequired"] is False


def test_setup_rejected_once_users_exist(client):
    _setup_admin(client)
    r = client.post("/api/v1/auth/setup", json={"email": "x@y.fr", "password": "motdepasse"})
    assert r.status_code == 403


def test_login_logout_flow(client):
    _setup_admin(client)
    client.post("/api/v1/auth/logout")
    assert client.get("/api/v1/auth/me").json()["authenticated"] is False

    r = client.post("/api/v1/auth/login", json={"email": "admin@localhost", "password": "mauvais"})
    assert r.status_code == 401
    r = client.post("/api/v1/auth/login", json={"email": "ADMIN@localhost", "password": "motdepasse-admin"})
    assert r.status_code == 200
    assert client.get("/api/v1/auth/me").json()["authenticated"] is True


def test_login_rate_limited_after_failures(client):
    _setup_admin(client)
    client.post("/api/v1/auth/logout")
    for _ in range(5):
        r = client.post("/api/v1/auth/login", json={"email": "admin@localhost", "password": "nope"})
        assert r.status_code == 401
    r = client.post("/api/v1/auth/login", json={"email": "admin@localhost", "password": "motdepasse-admin"})
    assert r.status_code == 429


# ---------------------------------------------------------------------------
# Visibilité & rôles
# ---------------------------------------------------------------------------


def test_admin_routes_require_auth(client):
    assert client.get("/api/v1/admin/overview").status_code == 401


def test_visitor_cannot_access_admin(client):
    _setup_admin(client)
    r = client.post("/api/v1/admin/users", json={"email": "v@club.fr", "password": "motdepasse", "role": "visitor"})
    assert r.status_code == 200

    visitor = TestClient(client.app)
    r = visitor.post("/api/v1/auth/login", json={"email": "v@club.fr", "password": "motdepasse"})
    assert r.status_code == 200
    assert visitor.get("/api/v1/admin/overview").status_code == 403


def test_private_tenant_hidden_from_anonymous(client):
    _setup_admin(client)
    tenant, _, _ = _make_tenant_with_sim(client, visibility="private")

    anon = TestClient(client.app)
    listed = anon.get("/api/v1/tenants").json()["tenants"]
    assert all(t["id"] != tenant["id"] for t in listed)
    assert anon.get(f"/api/v1/tenants/{tenant['id']}").status_code == 404


def test_public_tenant_visible_to_anonymous(client):
    _setup_admin(client)
    tenant, sim, _ = _make_tenant_with_sim(client, visibility="public")

    anon = TestClient(client.app)
    listed = anon.get("/api/v1/tenants").json()["tenants"]
    assert any(t["id"] == tenant["id"] for t in listed)
    assert anon.get(f"/api/v1/sims/{sim['id']}").status_code == 200


def test_public_access_off_hides_public_tenants_from_anonymous(client):
    _setup_admin(client)
    tenant, _, _ = _make_tenant_with_sim(client, visibility="public")
    r = client.post("/api/v1/admin/settings", json={"public_access": False})
    assert r.status_code == 200

    anon = TestClient(client.app)
    assert anon.get("/api/v1/tenants").json()["tenants"] == []
    assert anon.get(f"/api/v1/tenants/{tenant['id']}").status_code == 404

    # … mais un visiteur connecté voit toujours les tenants publics
    client.post("/api/v1/admin/users", json={"email": "v@club.fr", "password": "motdepasse", "role": "visitor"})
    visitor = TestClient(client.app)
    visitor.post("/api/v1/auth/login", json={"email": "v@club.fr", "password": "motdepasse"})
    assert any(t["id"] == tenant["id"] for t in visitor.get("/api/v1/tenants").json()["tenants"])


def test_visitor_sees_assigned_private_tenant(client):
    _setup_admin(client)
    tenant, sim, _ = _make_tenant_with_sim(client, visibility="private")
    r = client.post(
        "/api/v1/admin/users",
        json={"email": "v@club.fr", "password": "motdepasse", "role": "visitor", "tenant_ids": [tenant["id"]]},
    )
    assert r.status_code == 200

    visitor = TestClient(client.app)
    visitor.post("/api/v1/auth/login", json={"email": "v@club.fr", "password": "motdepasse"})
    listed = visitor.get("/api/v1/tenants").json()["tenants"]
    assert [t["id"] for t in listed] == [tenant["id"]]
    assert visitor.get(f"/api/v1/sims/{sim['id']}").status_code == 200


# ---------------------------------------------------------------------------
# Lecture : classements paginés
# ---------------------------------------------------------------------------


def test_leaderboard_paginated_over_api(client):
    _setup_admin(client)
    _, sim, token = _make_tenant_with_sim(client)
    _sync_laps(client, token, count=25)

    anon = TestClient(client.app)
    r = anon.get(f"/api/v1/sims/{sim['id']}/leaderboard?track_id=1&page=2")
    assert r.status_code == 200
    body = r.json()
    assert body["total"] == 25
    assert body["pages"] == 2
    assert len(body["rows"]) == 5
    assert body["rows"][0]["rank"] == 21


def test_sync_contract_unchanged(client):
    _setup_admin(client)
    _, sim, token = _make_tenant_with_sim(client)
    body = _sync_laps(client, token)
    assert body["ok"] is True
    assert body["commands"] == []

    # Jeton invalide → 401
    r = client.post("/api/v1/sync", headers={"X-Results-Token": "faux"}, json={})
    assert r.status_code == 401


# ---------------------------------------------------------------------------
# Admin : tenants, sims, pseudo de session
# ---------------------------------------------------------------------------


def test_tenant_delete_blocked_with_sims(client):
    _setup_admin(client)
    tenant, sim, _ = _make_tenant_with_sim(client)
    r = client.request("DELETE", f"/api/v1/admin/tenants/{tenant['id']}")
    assert r.status_code == 409
    assert client.request("DELETE", f"/api/v1/admin/simulators/{sim['id']}").status_code == 200
    assert client.request("DELETE", f"/api/v1/admin/tenants/{tenant['id']}").status_code == 200


def test_set_player_name_enqueues_job(client):
    _setup_admin(client)
    _, sim, token = _make_tenant_with_sim(client)
    r = client.post(f"/api/v1/admin/simulators/{sim['id']}/player-name", json={"new_name": "Capitaine"})
    assert r.status_code == 200

    # Le simu récupère la commande à sa prochaine sync
    body = _sync_laps(client, token, count=0)
    commands = body["commands"]
    assert len(commands) == 1
    assert commands[0]["type"] == "setPlayerName"
    assert commands[0]["newName"] == "Capitaine"


def test_user_lifecycle(client):
    _setup_admin(client)
    r = client.post("/api/v1/admin/users", json={"email": "v@club.fr", "password": "motdepasse", "role": "visitor"})
    user = r.json()["user"]
    assert user["role"] == "visitor"

    r = client.request("PATCH", f"/api/v1/admin/users/{user['id']}", json={"disabled": True})
    assert r.status_code == 200
    assert r.json()["user"]["disabled"] is True

    visitor = TestClient(client.app)
    assert visitor.post("/api/v1/auth/login", json={"email": "v@club.fr", "password": "motdepasse"}).status_code == 401

    # Le dernier admin ne peut pas être supprimé
    me = client.get("/api/v1/auth/me").json()["user"]
    assert client.request("DELETE", f"/api/v1/admin/users/{me['id']}").status_code in (400, 409)
