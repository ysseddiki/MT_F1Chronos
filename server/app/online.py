from __future__ import annotations

from datetime import datetime, timezone


def is_simulator_connected(last_seen_iso: str | None, interval_seconds: int | None, now: datetime | None = None) -> bool:
    """Offline if nothing received for more than 2× the sim's configured sync interval."""
    if not last_seen_iso:
        return False
    now = now or datetime.now(timezone.utc)
    try:
        last = datetime.fromisoformat(last_seen_iso)
    except ValueError:
        return False
    if last.tzinfo is None:
        last = last.replace(tzinfo=timezone.utc)
    interval = interval_seconds if interval_seconds and interval_seconds > 0 else 120
    return (now - last).total_seconds() <= interval * 2
