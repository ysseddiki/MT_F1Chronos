from __future__ import annotations

"""snake_case SQLite → camelCase API."""


def tenant_out(t: dict) -> dict:
    return {
        "id": t["id"],
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
        "name": e["name"],
        "bestLapMs": e["best_lap_ms"],
        "formatted": e["formatted"],
        "rank": e["rank"],
        "startedAt": e.get("started_at"),
        "trackId": e.get("track_id"),
        "trackName": e.get("track_name"),
        "simLabel": e.get("sim_label"),
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
    return {
        "trackId": t["track_id"],
        "trackName": t["track_name"],
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
    return {
        "id": u["id"],
        "email": u["email"],
        "role": u["role"],
        "disabled": u["disabled"],
        "createdAt": u.get("created_at"),
        "tenantIds": u.get("tenant_ids") or [],
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
