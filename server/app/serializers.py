from __future__ import annotations

"""snake_case SQLite → camelCase API."""


def tenant_out(t: dict) -> dict:
    return {
        "id": t["id"],
        "slug": t.get("slug") or t["id"],
        "label": t["label"],
        "visibility": t.get("visibility", "public"),
        "simCount": t.get("sim_count"),
        "createdAt": t.get("created_at"),
    }


def sim_out(s: dict, admin: bool = False) -> dict:
    out = {
        "id": s["id"],
        "label": s["label"],
        "tenantId": s.get("tenant_id"),
        "playerName": s.get("player_name") or "",
        "currentTrackId": s.get("current_track_id", -1),
        "currentTrackName": s.get("current_track_name") or "",
        "connected": bool(s.get("connected")),
        "lastSeenUtc": s.get("last_seen_utc"),
        "syncIntervalSeconds": s.get("sync_interval_seconds") or 120,
    }
    if admin:
        out["clientId"] = s.get("client_id")
    return out


def lap_out(e: dict) -> dict:
    return {
        "id": e["id"],
        "name": (e.get("name") or "").strip() or "—",
        "bestLapMs": e["best_lap_ms"],
        "formatted": e["formatted"],
        "rank": e["rank"],
        "startedAt": e.get("started_at"),
        "trackId": e.get("track_id"),
        "trackName": e.get("track_name") or "",
        "simLabel": e.get("sim_label"),
        "simId": e.get("simulator_id"),
    }


def board_out(board: dict) -> dict:
    return {
        "rows": [lap_out(r) for r in board["rows"]],
        "total": board["total"],
        "page": board["page"],
        "pageSize": board["page_size"],
        "pages": board["pages"],
    }


def track_out(t: dict) -> dict:
    track_id = t["track_id"]
    name = (t.get("track_name") or "").strip() or f"Circuit {track_id}"
    return {
        "trackId": track_id,
        "trackName": name,
        "scoreCount": t["score_count"],
    }


def contest_out(c: dict) -> dict:
    return {
        "id": c["id"],
        "name": c["name"],
        "status": c["status"],
        "trackFilter": c.get("track_filter"),
        "createdAt": c.get("created_at"),
        "startedAt": c.get("started_at"),
        "stoppedAt": c.get("stopped_at"),
        "scoreCount": c.get("score_count"),
    }


def user_out(u: dict) -> dict:
    sim_pseudo = (u.get("sim_pseudo") or "").strip()
    return {
        "id": u["id"],
        "email": u["email"],
        "role": u["role"],
        "disabled": u["disabled"],
        "createdAt": u.get("created_at"),
        "tenantIds": u.get("tenant_ids") or [],
        "simPseudo": sim_pseudo,
        "profileRequired": u.get("role") == "simracer" and not sim_pseudo,
    }


def job_out(j: dict) -> dict:
    return {
        "id": j["id"],
        "type": j["type"],
        "status": j["status"],
        "createdAt": j.get("created_at"),
        "appliedAt": j.get("applied_at"),
        "revertedAt": j.get("reverted_at"),
        "canRevert": bool(j.get("can_revert")),
        "payload": j.get("payload") or {},
    }
