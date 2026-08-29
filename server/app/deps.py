from __future__ import annotations

import os
from pathlib import Path

from fastapi import HTTPException, Request

from . import db
from .auth import ROLE_ADMIN, ROLE_SIMRACER, UserAuth
from .security import LoginRateLimiter
from .store import ResultsStore

BASE = Path(__file__).resolve().parent


def data_dir() -> Path:
    """Lu paresseusement : les tests changent RESULTS_DATA puis reset_state()."""
    return Path(os.environ.get("RESULTS_DATA") or (BASE.parent / "data"))


_store: ResultsStore | None = None
_auth: UserAuth | None = None
_limiter: LoginRateLimiter | None = None


def _ensure() -> tuple[ResultsStore, UserAuth, LoginRateLimiter]:
    global _store, _auth, _limiter
    if _store is None:
        conn = db.connect(data_dir() / "results.sqlite")
        _store = ResultsStore(conn)
        _auth = UserAuth(conn)
        _auth.seed_from_env(os.environ.get("RESULTS_ADMIN_PASSWORD") or "")
        _limiter = LoginRateLimiter()
    assert _auth is not None and _limiter is not None
    return _store, _auth, _limiter


def reset_state() -> None:
    """Tests point RESULTS_DATA at a tmp dir, then call this for a fresh singleton."""
    global _store, _auth, _limiter
    _store = None
    _auth = None
    _limiter = None


def store() -> ResultsStore:
    return _ensure()[0]


def auth() -> UserAuth:
    return _ensure()[1]


def limiter() -> LoginRateLimiter:
    return _ensure()[2]


# --- session / access control ---


def current_user(request: Request) -> dict | None:
    user_id = request.session.get("user_id")
    if not user_id:
        return None
    user = auth().get_user(user_id)
    if user is None or user["disabled"]:
        request.session.clear()
        return None
    return user


def can_view_tenant(user: dict | None, tenant: dict) -> bool:
    if user is not None and user["role"] == ROLE_ADMIN:
        return True
    if tenant.get("visibility", "public") == "public":
        return store().get_public_access() or user is not None
    return user is not None and tenant["id"] in auth().tenant_ids_for_user(user["id"])


def visible_tenants(user: dict | None) -> list[dict]:
    return [t for t in store().list_tenants() if can_view_tenant(user, t)]


def require_user(request: Request) -> dict:
    user = current_user(request)
    if user is None:
        raise HTTPException(401, "Connexion requise.")
    return user


def require_admin(request: Request) -> dict:
    user = require_user(request)
    if user["role"] != ROLE_ADMIN:
        raise HTTPException(403, "Droits administrateur requis.")
    return user


def require_simracer(request: Request) -> dict:
    user = require_user(request)
    if user["role"] != ROLE_SIMRACER:
        raise HTTPException(403, "Compte SimRacer requis.")
    return user


def tenant_or_404(tenant_key: str, user: dict | None) -> dict:
    tenant = store().resolve_tenant(tenant_key)
    if tenant is None or not can_view_tenant(user, tenant):
        raise HTTPException(404, "Organisation introuvable.")
    return tenant


def sim_or_404(sim_id: str, user: dict | None) -> dict:
    sim = store().get_simulator(sim_id)
    if sim is None:
        raise HTTPException(404, "Simulateur introuvable.")
    tenant = store().get_tenant(sim["tenant_id"]) if sim.get("tenant_id") else None
    if tenant is not None and not can_view_tenant(user, tenant):
        raise HTTPException(404, "Simulateur introuvable.")
    return sim
