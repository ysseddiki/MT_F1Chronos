from pathlib import Path

import pytest
from fastapi.testclient import TestClient


@pytest.fixture
def client(tmp_path: Path, monkeypatch):
    monkeypatch.setenv("RESULTS_DATA", str(tmp_path))
    # Flux SSE borné très court en test : le générateur se termine tout seul.
    monkeypatch.setenv("RESULTS_STREAM_POLL", "0.02")
    monkeypatch.setenv("RESULTS_STREAM_MAX_AGE", "0.1")
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


def test_stream_emits_data_version(client):
    with client.stream("GET", "/api/v1/stream") as r:
        assert r.status_code == 200
        assert r.headers["content-type"].startswith("text/event-stream")
        lines = [line for line in r.iter_lines() if line]
    assert lines, "le flux SSE doit émettre au moins la version courante"
    assert lines[0].startswith("data: ")
    assert lines[0].removeprefix("data: ").strip().isdigit()


def test_spa_fallback_serves_index(client):
    for path in ["/", "/t/quelque-chose", "/t/quelque-chose/recent", "/t/quelque-chose/championship",
                 "/t/quelque-chose/pilot/Ada", "/t/quelque-chose/versus", "/versus", "/championship", "/recent", "/account", "/admin", "/login"]:
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
    r = anon.get(f"/api/v1/sims/{sim['id']}/leaderboard?track_id=1&page=2&best=false")
    assert r.status_code == 200
    body = r.json()
    assert body["total"] == 25
    assert body["pages"] == 2
    assert len(body["rows"]) == 5
    assert body["rows"][0]["rank"] == 21


def test_leaderboard_best_per_player_default(client):
    _setup_admin(client)
    _, sim, token = _make_tenant_with_sim(client)
    client.post(
        "/api/v1/sync",
        headers={"X-Results-Token": token},
        json={
            "simulatorId": "cli",
            "global": {
                "tracks": [{
                    "trackId": 1,
                    "trackName": "Melbourne",
                    "entries": [
                        {"id": "a", "name": "Ada", "bestLapMs": 90000, "startedAt": "2026-01-01T00:00:00"},
                        {"id": "b", "name": "Ada", "bestLapMs": 80000, "startedAt": "2026-01-02T00:00:00"},
                        {"id": "c", "name": "Bob", "bestLapMs": 85000, "startedAt": "2026-01-01T00:00:00"},
                    ],
                }],
            },
        },
    )
    anon = TestClient(client.app)
    r = anon.get(f"/api/v1/sims/{sim['id']}/leaderboard?track_id=1")
    assert r.status_code == 200
    body = r.json()
    assert body["total"] == 2
    assert {row["name"] for row in body["rows"]} == {"Ada", "Bob"}
    assert body["rows"][0]["bestLapMs"] == 80000


def test_recent_laps_over_api(client):
    _setup_admin(client)
    _, sim, token = _make_tenant_with_sim(client)
    client.post(
        "/api/v1/sync",
        headers={"X-Results-Token": token},
        json={
            "simulatorId": "cli",
            "global": {
                "tracks": [{
                    "trackId": 1,
                    "trackName": "Melbourne",
                    "entries": [
                        {"id": "old", "name": "Ada", "bestLapMs": 81000, "startedAt": "2026-01-01T00:00:00"},
                        {"id": "new", "name": "Bob", "bestLapMs": 82000, "startedAt": "2026-01-05T00:00:00"},
                    ],
                }],
            },
        },
    )
    anon = TestClient(client.app)
    r = anon.get(f"/api/v1/sims/{sim['id']}/recent-laps?limit=1")
    assert r.status_code == 401

    r = client.get(f"/api/v1/sims/{sim['id']}/recent-laps?limit=1")
    assert r.status_code == 200
    body = r.json()
    assert len(body["rows"]) == 1
    assert body["rows"][0]["id"] == "new"
    assert body["rows"][0]["startedAt"] == "2026-01-05T00:00:00"


def test_tenant_recent_laps_over_api(client):
    _setup_admin(client)
    tenant, sim, token = _make_tenant_with_sim(client)
    _sync_laps(client, token, count=2)
    anon = TestClient(client.app)
    r = anon.get(f"/api/v1/tenants/{tenant['id']}/recent-laps")
    assert r.status_code == 401

    r = client.get(f"/api/v1/tenants/{tenant['id']}/recent-laps")
    assert r.status_code == 200
    body = r.json()
    assert len(body["rows"]) == 2
    assert body["total"] == 2
    assert body["page"] == 1
    assert body["pageSize"] >= 1

    filtered = client.get(
        f"/api/v1/tenants/{tenant['id']}/recent-laps",
        params={"pilot": "Pilote 0", "sort": "best_lap_ms", "order": "asc", "page_size": 50},
    )
    assert filtered.status_code == 200
    names = [row["name"] for row in filtered.json()["rows"]]
    assert names == ["Pilote 0"]

    page1 = client.get(
        f"/api/v1/tenants/{tenant['id']}/recent-laps",
        params={"page": 1, "page_size": 1},
    )
    assert page1.status_code == 200
    p1 = page1.json()
    assert p1["total"] == 2
    assert p1["pages"] == 2
    assert len(p1["rows"]) == 1


def test_tenant_resolved_by_slug(client):
    _setup_admin(client)
    tenant, sim, token = _make_tenant_with_sim(client, label="Sim Racing DC")
    slug = tenant["slug"]
    assert slug
    _sync_laps(client, token)
    anon = TestClient(client.app)
    by_slug = anon.get(f"/api/v1/tenants/{slug}")
    assert by_slug.status_code == 200
    body = by_slug.json()
    assert body["tenant"]["id"] == tenant["id"]
    assert len(body["sims"]) == 1
    assert body["sims"][0]["id"] == sim["id"]

    tracks = anon.get(f"/api/v1/tenants/{slug}/tracks")
    assert tracks.status_code == 200
    assert len(tracks.json()["tracks"]) == 1

    board = anon.get(f"/api/v1/tenants/{slug}/leaderboard?track_id=1")
    assert board.status_code == 200
    assert board.json()["total"] >= 1

    by_id = anon.get(f"/api/v1/tenants/{tenant['id']}")
    assert by_id.status_code == 200
    assert by_slug.json()["tenant"]["simCount"] == 1


def test_sync_contract_unchanged(client):
    _setup_admin(client)
    _, sim, token = _make_tenant_with_sim(client)
    body = _sync_laps(client, token)
    assert body["ok"] is True
    assert body["commands"] == []

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


def _login(client: TestClient, email: str, password: str) -> TestClient:
    session = TestClient(client.app)
    r = session.post("/api/v1/auth/login", json={"email": email, "password": password})
    assert r.status_code == 200, r.text
    return session


def test_simracer_profile_and_apply_pseudo(client):
    _setup_admin(client)
    tenant, sim, token = _make_tenant_with_sim(client)

    r = client.post(
        "/api/v1/admin/users",
        json={"email": "sim@club.fr", "password": "motdepasse", "role": "simracer", "tenant_ids": [tenant["id"]]},
    )
    assert r.status_code == 200, r.text
    sim_user = r.json()["user"]
    assert sim_user["role"] == "simracer"
    assert sim_user["profileRequired"] is True

    sim_client = _login(client, "sim@club.fr", "motdepasse")
    me = sim_client.get("/api/v1/auth/me").json()
    assert me["profileRequired"] is True

    r = sim_client.patch("/api/v1/profile/sim-pseudo", json={"sim_pseudo": "Capitaine"})
    assert r.status_code == 200, r.text
    assert r.json()["user"]["simPseudo"] == "Capitaine"
    assert r.json()["user"]["profileRequired"] is False

    r = sim_client.post(f"/api/v1/sims/{sim['id']}/apply-my-pseudo")
    assert r.status_code == 200, r.text

    body = _sync_laps(client, token, count=0)
    commands = body["commands"]
    assert len(commands) == 1
    assert commands[0]["type"] == "setPlayerName"
    assert commands[0]["newName"] == "Capitaine"


def test_simracer_cannot_apply_without_pseudo(client):
    _setup_admin(client)
    _, sim, _ = _make_tenant_with_sim(client)
    client.post(
        "/api/v1/admin/users",
        json={"email": "sim@club.fr", "password": "motdepasse", "role": "simracer"},
    )
    sim_client = _login(client, "sim@club.fr", "motdepasse")
    r = sim_client.post(f"/api/v1/sims/{sim['id']}/apply-my-pseudo")
    assert r.status_code == 400


def test_championship_and_points_settings(client):
    _setup_admin(client)
    tenant, sim, token = _make_tenant_with_sim(client)
    client.post(
        "/api/v1/sync",
        headers={"X-Results-Token": token},
        json={
            "simulatorId": "cli",
            "global": {
                "tracks": [{
                    "trackId": 1,
                    "trackName": "Melbourne",
                    "entries": [
                        {"id": "a", "name": "Ada", "bestLapMs": 79000, "startedAt": "2026-01-01T00:00:00"},
                        {"id": "b", "name": "Bob", "bestLapMs": 80000, "startedAt": "2026-01-01T00:00:00"},
                    ],
                }],
            },
        },
    )

    r = client.post("/api/v1/admin/settings", json={"points_by_place": "10,5"})
    assert r.status_code == 200, r.text
    assert r.json()["pointsByPlace"] == "10,5"

    overview = client.get("/api/v1/admin/overview").json()
    assert overview["pointsByPlace"] == "10,5"

    r = client.post("/api/v1/admin/settings", json={"points_by_place": "bad"})
    assert r.status_code == 400

    anon = TestClient(client.app)
    r = anon.get(f"/api/v1/tenants/{tenant['id']}/championship")
    assert r.status_code == 200
    body = r.json()
    assert body["pointsByPlace"] == [10, 5]
    assert body["standings"][0]["name"] == "Ada"
    assert body["standings"][0]["points"] == 10
    assert body["standings"][1]["points"] == 5


def test_pilot_profile_linked_after_sync_auto_provision(client):
    _setup_admin(client)
    tenant, sim, token = _make_tenant_with_sim(client)
    _sync_laps(client, token, count=2)

    anon = TestClient(client.app)
    r = anon.get(f"/api/v1/tenants/{tenant['id']}/pilots/Pilote%200")
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["pilot"]["simPseudo"] == "Pilote 0"
    assert body["pilot"]["linked"] is True
    assert body["pilot"]["role"] == "simracer"
    assert "email" not in body["pilot"]
    assert body["profile"]["totalLaps"] >= 1
    assert body["profile"]["bests"]

    users = client.get("/api/v1/admin/users").json()["users"]
    auto = next(u for u in users if u.get("simPseudo") == "Pilote 0")
    assert auto["credentialsPending"] is True

    linked = anon.get(f"/api/v1/tenants/{tenant['id']}/linked-pilots")
    assert linked.status_code == 200
    assert "Pilote 0" in linked.json()["pseudos"]

    r = client.patch(
        f"/api/v1/admin/users/{auto['id']}",
        json={"password": "motdepasse-pilote"},
    )
    assert r.status_code == 200, r.text
    assert r.json()["user"]["credentialsPending"] is False
    sim_client = _login(client, auto["email"], "motdepasse-pilote")
    assert sim_client.get("/api/v1/auth/me").status_code == 200


def test_sim_pseudo_unique_across_users(client):
    _setup_admin(client)
    tenant, _, _ = _make_tenant_with_sim(client)
    client.post(
        "/api/v1/admin/users",
        json={"email": "a@club.fr", "password": "motdepasse", "role": "simracer", "tenant_ids": [tenant["id"]]},
    )
    client.post(
        "/api/v1/admin/users",
        json={"email": "b@club.fr", "password": "motdepasse", "role": "simracer", "tenant_ids": [tenant["id"]]},
    )
    a = _login(client, "a@club.fr", "motdepasse")
    b = _login(client, "b@club.fr", "motdepasse")
    assert a.patch("/api/v1/profile/sim-pseudo", json={"sim_pseudo": "Ace"}).status_code == 200
    r = b.patch("/api/v1/profile/sim-pseudo", json={"sim_pseudo": "ace"})
    assert r.status_code == 400


def test_all_tenant_aggregates_visible_orgs(client):
    _setup_admin(client)
    t1, sim1, token1 = _make_tenant_with_sim(client, label="Club A", sim_label="Box A")
    t2, sim2, token2 = _make_tenant_with_sim(client, label="Club B", sim_label="Box B")
    client.post(
        "/api/v1/sync",
        headers={"X-Results-Token": token1},
        json={
            "simulatorId": "cli-a",
            "global": {"tracks": [{
                "trackId": 1,
                "trackName": "Melbourne",
                "entries": [
                    {"id": "a1", "name": "Ada", "bestLapMs": 79000, "startedAt": "2026-01-01T00:00:00"},
                ],
            }]},
        },
    )
    client.post(
        "/api/v1/sync",
        headers={"X-Results-Token": token2},
        json={
            "simulatorId": "cli-b",
            "global": {"tracks": [{
                "trackId": 1,
                "trackName": "Melbourne",
                "entries": [
                    {"id": "b1", "name": "Bob", "bestLapMs": 78000, "startedAt": "2026-01-01T00:00:00"},
                ],
            }]},
        },
    )

    listed = client.get("/api/v1/tenants").json()["tenants"]
    assert listed[0]["id"] == "all"
    assert listed[0]["isAggregate"] is True
    assert listed[0]["orgCount"] == 2

    r = client.get("/api/v1/tenants/all")
    assert r.status_code == 200
    body = r.json()
    assert body["tenant"]["slug"] == "all"
    assert len(body["sims"]) == 2

    board = client.get("/api/v1/tenants/all/leaderboard?track_id=1&best=true").json()
    assert board["total"] == 2
    assert board["rows"][0]["name"] == "Bob"
    assert board["rows"][1]["name"] == "Ada"

    champ = client.get("/api/v1/tenants/all/championship").json()
    assert champ["tracksCounted"] == 1
    assert champ["standings"][0]["name"] == "Bob"
    assert champ["standings"][0]["points"] == 25

    pilot = client.get("/api/v1/tenants/all/pilots/Ada").json()
    assert pilot["profile"]["totalLaps"] == 1


def test_versus_compares_two_pilots(client):
    _setup_admin(client)
    tenant, sim, token = _make_tenant_with_sim(client)
    client.post(
        "/api/v1/sync",
        headers={"X-Results-Token": token},
        json={
            "simulatorId": "cli-vs",
            "global": {"tracks": [
                {
                    "trackId": 1,
                    "trackName": "Melbourne",
                    "entries": [
                        {"id": "a1", "name": "Ada", "bestLapMs": 80000, "startedAt": "2026-01-01T00:00:00"},
                        {"id": "b1", "name": "Bob", "bestLapMs": 82000, "startedAt": "2026-01-01T00:00:00"},
                    ],
                },
                {
                    "trackId": 2,
                    "trackName": "Spa",
                    "entries": [
                        {"id": "a2", "name": "Ada", "bestLapMs": 105000, "startedAt": "2026-01-02T00:00:00"},
                        {"id": "b2", "name": "Bob", "bestLapMs": 100000, "startedAt": "2026-01-02T00:00:00"},
                    ],
                },
            ]},
        },
    )

    names = client.get(f"/api/v1/tenants/{tenant['id']}/pilot-names").json()["names"]
    assert "Ada" in names and "Bob" in names

    r = client.get(f"/api/v1/tenants/{tenant['id']}/versus?a=Ada&b=Bob")
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["summary"]["commonTracks"] == 2
    assert body["summary"]["winsA"] == 1
    assert body["summary"]["winsB"] == 1
    assert body["summary"]["avgRelativePct"] is not None
    assert body["summary"]["levelIndex"] is not None
    # Melbourne −2000 + Spa +5000 → moyenne +1500 ms
    assert body["summary"]["avgGapMs"] == 1500
    tracks = {t["trackName"]: t for t in body["tracks"]}
    assert tracks["Melbourne"]["winner"] == "a"
    assert tracks["Melbourne"]["gapMs"] == -2000
    assert tracks["Spa"]["winner"] == "b"

    bad = client.get(f"/api/v1/tenants/{tenant['id']}/versus?a=Ada&b=Ada")
    assert bad.status_code == 400
