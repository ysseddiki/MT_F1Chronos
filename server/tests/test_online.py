from datetime import datetime, timedelta, timezone

from app.online import is_simulator_connected


def test_connected_within_interval():
    now = datetime(2026, 1, 1, 12, 0, tzinfo=timezone.utc)
    last = (now - timedelta(seconds=100)).isoformat()
    assert is_simulator_connected(last, 120, now) is True


def test_disconnected_after_two_intervals():
    now = datetime(2026, 1, 1, 12, 0, tzinfo=timezone.utc)
    last = (now - timedelta(seconds=241)).isoformat()
    assert is_simulator_connected(last, 120, now) is False


def test_exactly_two_intervals_still_connected():
    now = datetime(2026, 1, 1, 12, 0, tzinfo=timezone.utc)
    last = (now - timedelta(seconds=240)).isoformat()
    assert is_simulator_connected(last, 120, now) is True


def test_never_seen():
    assert is_simulator_connected(None, 120) is False
