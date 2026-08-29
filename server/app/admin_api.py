from __future__ import annotations

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

from . import deps
from .serializers import (
    board_out,
    contest_out,
    job_out,
    sim_out,
    tenant_out,
    track_out,
    user_out,
)
from .store import DEFAULT_PAGE_SIZE, TENANT_VISIBILITIES

router = APIRouter(prefix="/api/v1/admin")


def _err(exc: ValueError, status: int = 400) -> JSONResponse:
    return JSONResponse({"ok": False, "message": str(exc)}, status_code=status)


# ---------------------------------------------------------------------------
# Vue d'ensemble
# ---------------------------------------------------------------------------


@router.get("/overview")
def overview(request: Request):
    deps.require_admin(request)
    store = deps.store()
    return {
        "ok": True,
        "tenants": [tenant_out(t) for t in store.list_tenants()],
        "sims": [sim_out(s, admin=True) for s in store.list_simulators()],
        "publicAccess": store.get_public_access(),
    }


@router.get("/sims/{sim_id}/detail")
def sim_detail(request: Request, sim_id: str, contest_id: str | None = None):
    deps.require_admin(request)
    store = deps.store()
    sim = store.get_simulator(sim_id)
    if sim is None:
        raise HTTPException(404, "Simulateur introuvable.")
    tenant = store.get_tenant(sim["tenant_id"]) if sim.get("tenant_id") else None
    return {
        "ok": True,
        "sim": sim_out(sim, admin=True),
        "tenant": tenant_out(tenant) if tenant else None,
        "contests": [contest_out(c) for c in store.list_contests(sim_id)],
        "tracks": [track_out(t) for t in store.track_summaries(sim_id, contest_id)],
        "jobs": [job_out(j) for j in store.list_jobs(sim_id)],
    }


# ---------------------------------------------------------------------------
# Organisations (tenants)
# ---------------------------------------------------------------------------


class TenantIn(BaseModel):
    label: str = Field(default="", max_length=60)
    visibility: str = "public"


class TenantPatch(BaseModel):
    label: str | None = Field(default=None, max_length=60)
    visibility: str | None = None
    slug: str | None = Field(default=None, max_length=48)


@router.post("/tenants")
def create_tenant(request: Request, body: TenantIn):
    deps.require_admin(request)
    visibility = body.visibility if body.visibility in TENANT_VISIBILITIES else "public"
    tenant = deps.store().create_tenant(body.label, visibility)
    return {"ok": True, "tenant": tenant_out(tenant)}


@router.patch("/tenants/{tenant_id}")
def update_tenant(request: Request, tenant_id: str, body: TenantPatch):
    deps.require_admin(request)
    try:
        tenant = deps.store().update_tenant(
            tenant_id, body.label, body.visibility, body.slug
        )
    except ValueError as exc:
        return _err(exc)
    return {"ok": True, "tenant": tenant_out(tenant)}


@router.delete("/tenants/{tenant_id}")
def delete_tenant(request: Request, tenant_id: str):
    deps.require_admin(request)
    try:
        deps.store().delete_tenant(tenant_id)
    except ValueError as exc:
        return _err(exc, status=409)
    return {"ok": True}


# ---------------------------------------------------------------------------
# Simulateurs
# ---------------------------------------------------------------------------


class SimIn(BaseModel):
    label: str = Field(default="", max_length=40)
    tenant_id: str = ""


class SimPatch(BaseModel):
    label: str | None = Field(default=None, max_length=40)
    tenant_id: str | None = None


@router.post("/simulators")
def create_simulator(request: Request, body: SimIn):
    deps.require_admin(request)
    tenant_id = body.tenant_id.strip() or None
    try:
        sim, token = deps.store().create_simulator(body.label, tenant_id=tenant_id)
    except ValueError as exc:
        return _err(exc)
    return {"ok": True, "sim": sim_out(sim, admin=True), "token": token}


@router.patch("/simulators/{sim_id}")
def update_simulator(request: Request, sim_id: str, body: SimPatch):
    deps.require_admin(request)
    try:
        sim = deps.store().update_simulator(sim_id, body.label, body.tenant_id)
    except ValueError as exc:
        return _err(exc)
    return {"ok": True, "sim": sim_out(sim, admin=True)}


@router.delete("/simulators/{sim_id}")
def delete_simulator(request: Request, sim_id: str):
    deps.require_admin(request)
    if not deps.store().delete_simulator(sim_id):
        raise HTTPException(404, "Simulateur introuvable.")
    return {"ok": True}


@router.post("/simulators/{sim_id}/token")
def regenerate_token(request: Request, sim_id: str):
    deps.require_admin(request)
    token = deps.store().regenerate_token(sim_id)
    if token is None:
        raise HTTPException(404, "Simulateur introuvable.")
    return {"ok": True, "token": token}


class PlayerNameIn(BaseModel):
    new_name: str = Field(default="", max_length=40)


@router.post("/simulators/{sim_id}/player-name")
def set_player_name(request: Request, sim_id: str, body: PlayerNameIn):
    """Change le pseudo de la session en cours : le simu l'applique à sa prochaine sync."""
    deps.require_admin(request)
    if not deps.store().enqueue_set_player_name(sim_id, body.new_name):
        raise HTTPException(400, "Pseudo invalide ou simulateur introuvable.")
    return {"ok": True, "message": "Pseudo de session en file — appliqué à la prochaine sync du simu."}


# ---------------------------------------------------------------------------
# Chronos & joueurs
# ---------------------------------------------------------------------------


@router.get("/laps")
def list_laps(
    request: Request,
    sim_id: str,
    track_id: int,
    contest_id: str | None = None,
    page: int = 1,
    page_size: int = DEFAULT_PAGE_SIZE,
):
    deps.require_admin(request)
    board = deps.store().leaderboard(
        sim_id, track_id, contest_id, page=page, page_size=page_size
    )
    return {"ok": True, **board_out(board)}


class LapActionIn(BaseModel):
    sim_id: str = Field(min_length=1, max_length=64)


class RenameEntryIn(LapActionIn):
    new_name: str = Field(default="", max_length=40)


@router.post("/laps/{entry_id}/delete")
def delete_lap(request: Request, entry_id: str, body: LapActionIn):
    deps.require_admin(request)
    if not deps.store().admin_delete_entry(body.sim_id, entry_id):
        raise HTTPException(404, "Chrono introuvable.")
    return {"ok": True, "message": "Chrono retiré — job en file pour le simu."}


@router.post("/laps/{entry_id}/rename")
def rename_lap(request: Request, entry_id: str, body: RenameEntryIn):
    deps.require_admin(request)
    if not deps.store().admin_rename_entry(body.sim_id, entry_id, body.new_name):
        return _err(ValueError("Renommage impossible (chrono ou pseudo invalide)."))
    return {"ok": True, "message": "Pseudo modifié (ce chrono) — job en file."}


class RenamePlayerIn(BaseModel):
    sim_id: str = Field(min_length=1, max_length=64)
    contest_id: str | None = None
    old_name: str = Field(default="", max_length=40)
    new_name: str = Field(default="", max_length=40)


@router.post("/players/rename")
def rename_player(request: Request, body: RenamePlayerIn):
    deps.require_admin(request)
    count = deps.store().admin_rename_player(
        body.sim_id, body.contest_id or None, body.old_name, body.new_name
    )
    if not count:
        return _err(ValueError("Aucun chrono ne correspond à ce pseudo."))
    return {"ok": True, "message": f"Pseudo modifié sur {count} chrono(s) — job en file."}


# ---------------------------------------------------------------------------
# Jobs
# ---------------------------------------------------------------------------


@router.get("/jobs")
def list_jobs(request: Request, sim_id: str):
    deps.require_admin(request)
    return {
        "ok": True,
        "jobs": [job_out(j) for j in deps.store().list_jobs(sim_id)],
    }


class JobRevertIn(BaseModel):
    sim_id: str = Field(min_length=1, max_length=64)


@router.post("/jobs/{job_id}/revert")
def revert_job(request: Request, job_id: str, body: JobRevertIn):
    deps.require_admin(request)
    err = deps.store().revert_job(body.sim_id, job_id)
    if err is not None:
        return _err(ValueError(f"Revert impossible : {err}"), status=409)
    return {"ok": True, "message": "Job revert."}


# ---------------------------------------------------------------------------
# Utilisateurs
# ---------------------------------------------------------------------------


class UserIn(BaseModel):
    email: str = Field(default="", max_length=254)
    password: str = Field(default="", max_length=256)
    role: str = "visitor"
    tenant_ids: list[str] = []


class UserPatch(BaseModel):
    role: str | None = None
    disabled: bool | None = None
    password: str | None = Field(default=None, max_length=256)
    tenant_ids: list[str] | None = None


@router.get("/users")
def list_users(request: Request):
    deps.require_admin(request)
    return {"ok": True, "users": [user_out(u) for u in deps.auth().list_users()]}


@router.post("/users")
def create_user(request: Request, body: UserIn):
    deps.require_admin(request)
    try:
        user = deps.auth().create_user(body.email, body.password, body.role, body.tenant_ids)
    except ValueError as exc:
        return _err(exc)
    return {"ok": True, "user": user_out(user)}


@router.patch("/users/{user_id}")
def update_user(request: Request, user_id: str, body: UserPatch):
    deps.require_admin(request)
    try:
        user = deps.auth().update_user(
            user_id,
            role=body.role,
            disabled=body.disabled,
            password=body.password,
            tenant_ids=body.tenant_ids,
        )
    except ValueError as exc:
        return _err(exc)
    return {"ok": True, "user": user_out(user)}


@router.delete("/users/{user_id}")
def delete_user(request: Request, user_id: str):
    current = deps.require_admin(request)
    if current["id"] == user_id:
        return _err(ValueError("Tu ne peux pas supprimer ton propre compte."))
    try:
        deps.auth().delete_user(user_id)
    except ValueError as exc:
        return _err(exc, status=409)
    return {"ok": True}


# ---------------------------------------------------------------------------
# Réglages globaux
# ---------------------------------------------------------------------------


class SettingsIn(BaseModel):
    public_access: bool


@router.post("/settings")
def update_settings(request: Request, body: SettingsIn):
    deps.require_admin(request)
    deps.store().set_public_access(body.public_access)
    return {"ok": True, "publicAccess": deps.store().get_public_access()}
