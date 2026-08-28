from __future__ import annotations

import time
from collections import deque

from starlette.datastructures import MutableHeaders

CSP = (
    "default-src 'self'; "
    "script-src 'self'; "
    "style-src 'self'; "
    "img-src 'self' data:; "
    "connect-src 'self'; "
    "font-src 'self'; "
    "object-src 'none'; "
    "base-uri 'self'; "
    "form-action 'self'; "
    "frame-ancestors 'none'"
)


class SecurityHeadersMiddleware:
    """Pure-ASGI : n'enveloppe que http.response.start, donc les flux SSE
    (StreamingResponse infini) passent sans être bufferisés — contrairement
    à BaseHTTPMiddleware qui consomme le corps et bloque les flux infinis."""

    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        is_api = scope.get("path", "").startswith("/api/")

        async def send_with_headers(message):
            if message["type"] == "http.response.start":
                headers = MutableHeaders(scope=message)
                headers["Content-Security-Policy"] = CSP
                headers["X-Content-Type-Options"] = "nosniff"
                headers["X-Frame-Options"] = "DENY"
                headers["Referrer-Policy"] = "same-origin"
                if is_api:
                    headers["Cache-Control"] = "no-store"
            await send(message)

        await self.app(scope, receive, send_with_headers)


class LoginRateLimiter:
    """In-memory sliding window: blocks after `max_attempts` failures per key per `window_seconds`."""

    def __init__(self, max_attempts: int = 5, window_seconds: int = 300):
        self._max = max_attempts
        self._window = window_seconds
        self._failures: dict[str, deque[float]] = {}

    def _prune(self, key: str, now: float) -> deque[float]:
        entries = self._failures.setdefault(key, deque())
        while entries and now - entries[0] > self._window:
            entries.popleft()
        return entries

    def blocked(self, key: str) -> bool:
        return len(self._prune(key, time.monotonic())) >= self._max

    def hit(self, key: str) -> None:
        self._prune(key, time.monotonic()).append(time.monotonic())

    def reset(self, key: str) -> None:
        self._failures.pop(key, None)
