from __future__ import annotations

"""snake_case SQLite → camelCase API."""


def tenant_out(t: dict) -> dict:
    out = {
        "id": t["id"],
        "slug": t.get("slug") or t["id"],
        "label": t["label"],
        "visibility": t.get("visibility", "public"),
        "simCount": t.get("sim_count"),
        "createdAt": t.get("created_at"),
    }
    if t.get("is_aggregate"):
        out["isAggregate"] = True
        if t.get("org_count") is not None:
            out["orgCount"] = t["org_count"]
    return out


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
    if s.get("tenant_label"):
        out["tenantLabel"] = s["tenant_label"]
    if admin:
        out["clientId"] = s.get("client_id")
    return out


def lap_out(e: dict) -> dict:
    return {
        "id": e["id"],
        "name": (e.get("name") or "").strip() or "—",
        "bestLapMs": e["best_lap_ms"],
        "formatted": e["formatted"],
        "rank": e.get("rank", 0),
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


def championship_out(data: dict) -> dict:
    return {
        "pointsByPlace": list(data["points_by_place"]),
        "pointsByPlaceRaw": data["points_by_place_raw"],
        "tracksCounted": data["tracks_counted"],
        "standings": [
            {
                "rank": s["rank"],
                "name": s["name"],
                "points": s["points"],
                "totalLaps": s.get("total_laps", 0),
                "wins": s["wins"],
                "podiums": s["podiums"],
                "scoringPlaces": s["scoring_places"],
                "tracks": s["tracks"],
            }
            for s in data["standings"]
        ],
    }


def experience_standing_out(s: dict | None) -> dict | None:
    if not s:
        return None
    return {
        "rank": s["rank"],
        "name": s["name"],
        "points": s["points"],
        "totalLaps": s.get("total_laps", 0),
        "wins": s["wins"],
        "podiums": s["podiums"],
        "scoringPlaces": s.get("scoring_places", 0),
        "tracks": s["tracks"],
    }


def pilot_profile_out(data: dict) -> dict:
    return {
        "name": data["name"],
        "totalLaps": data["total_laps"],
        "tracksDriven": data["tracks_driven"],
        "experience": experience_standing_out(data.get("experience")),
        "bests": [lap_out(r) for r in data["bests"]],
        "recent": [lap_out(r) for r in data["recent"]],
    }


def versus_pilot_out(p: dict) -> dict:
    return {
        "name": p["name"],
        "totalLaps": p["total_laps"],
        "tracksDriven": p["tracks_driven"],
        "experience": experience_standing_out(p.get("experience")),
    }


def versus_out(data: dict) -> dict:
    return {
        "a": versus_pilot_out(data["a"]),
        "b": versus_pilot_out(data["b"]),
        "summary": {
            "commonTracks": data["summary"]["common_tracks"],
            "winsA": data["summary"]["wins_a"],
            "winsB": data["summary"]["wins_b"],
            "ties": data["summary"]["ties"],
            "avgRelativePct": data["summary"]["avg_relative_pct"],
            "avgGapMs": data["summary"]["avg_gap_ms"],
            "levelIndex": data["summary"]["level_index"],
            "faster": data["summary"]["faster"],
        },
        "tracks": [
            {
                "trackId": t["track_id"],
                "trackName": t["track_name"],
                "aMs": t["a_ms"],
                "bMs": t["b_ms"],
                "aFormatted": t["a_formatted"],
                "bFormatted": t["b_formatted"],
                "gapMs": t["gap_ms"],
                "relativePct": t["relative_pct"],
                "winner": t["winner"],
            }
            for t in data["tracks"]
        ],
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
        "credentialsPending": bool(u.get("credentials_pending")),
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
