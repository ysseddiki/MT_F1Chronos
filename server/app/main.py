from __future__ import annotations

import asyncio
import logging
import os
import secrets as _secrets

from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import FileResponse, JSONResponse, StreamingResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field
from starlette.middleware.sessions import SessionMiddleware

from . import db, deps
from .admin_api import router as admin_router
from .security import SecurityHeadersMiddleware
from .serializers import board_out, contest_out, lap_out, sim_out, tenant_out, track_out, user_out
from .store import DEFAULT_PAGE_SIZE, DEFAULT_RECENT_LAPS

BASE = deps.BASE
STATIC_DIR = BASE / "static"

logger = logging.getLogger("uvicorn.error")

# RESULTS_SECRET signs session cookies. When unset, fall back to a random per-boot
# secret (sessions die on restart) — never a publicly known constant.
SECRET = os.environ.get("RESULTS_SECRET") or ""
if not SECRET:
    SECRET = _secrets.token_hex(32)
    logger.warning("RESULTS_SECRET absent — secret de session aléatoire généré (sessions perdues au redémarrage).")

# Cookie Secure : HTTPS derrière Caddy (domaine configuré). HTTP explicite ⇒ false.
def _secure_session_cookies() -> bool:
    override = os.environ.get("RESULTS_SECURE_COOKIES", "").strip().lower()
    if override in ("1", "true", "yes"):
        return True
    if override in ("0", "false", "no"):
        return False
    if not os.environ.get("RESULTS_DOMAIN", "").strip():
        return False
    mode = os.environ.get("RESULTS_TLS_MODE", "letsencrypt").strip().lower()
    return mode != "http"


app = FastAPI(title="F1 Chronos — Résultats", docs_url=None, redoc_url=None, openapi_url=None)
# X-Forwarded-* : géré par uvicorn (--proxy-headers dans le Dockerfile), pas ici.
app.add_middleware(SecurityHeadersMiddleware)
app.add_middleware(
    SessionMiddleware,
    secret_key=SECRET,
    same_site="lax",
    https_only=_secure_session_cookies(),
)
app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")


# ---------------------------------------------------------------------------
# Erreurs : JSON homogène {ok, message}
# ---------------------------------------------------------------------------


@app.exception_handler(HTTPException)
async def http_exception(request: Request, exc: HTTPException):
    return JSONResponse({"ok": False, "message": exc.detail}, status_code=exc.status_code)


@app.exception_handler(RequestValidationError)
async def validation_exception(request: Request, exc: RequestValidationError):
    return JSONResponse({"ok": False, "message": "Requête invalide."}, status_code=400)


@app.exception_handler(Exception)
async def unhandled_exception(request: Request, exc: Exception):
    import traceback

    logger.error(
        "Unhandled error on %s %s\n%s",
        request.method,
        request.url.path,
        traceback.format_exc(),
    )
    return JSONResponse({"ok": False, "message": "Erreur serveur."}, status_code=500)


def _err(exc: ValueError, status: int = 400) -> JSONResponse:
    return JSONResponse({"ok": False, "message": str(exc)}, status_code=status)


# ---------------------------------------------------------------------------
# API simulateur (contrat figé — ne pas casser les clients déployés)
# ---------------------------------------------------------------------------


@app.get("/api/v1/health")
def health():
    return {"ok": True, "protocolVersion": 1, "authRequired": True}


@app.post("/api/v1/register")
async def register(request: Request):
    key = f"register:{_client_key(request)}"
    if deps.limiter().blocked(key):
        raise HTTPException(429, "Trop d'enregistrements. Réessaie dans quelques minutes.")
    deps.limiter().hit(key)

    try:
        payload = await request.json()
    except Exception:
        return JSONResponse({"ok": False, "message": "JSON invalide."}, status_code=400)

    client_id = (payload.get("simulatorId") or "").strip()
    label = (payload.get("simulatorLabel") or "Simulateur").strip() or "Simulateur"
    if not client_id:
        return JSONResponse({"ok": False, "message": "simulatorId requis."}, status_code=400)

    try:
        tenant, sim, token = deps.store().register_simulator(label, client_id)
    except ValueError as exc:
        return _err(exc)

    return {
        "ok": True,
        "token": token,
        "simulatorId": sim["id"],
        "tenantId": tenant["id"],
        "tenantLabel": tenant["label"],
        "serverTime": db.utcnow(),
    }


@app.post("/api/v1/sync")
async def sync(request: Request):
    token = request.headers.get("X-Results-Token") or ""
    auth_header = request.headers.get("Authorization") or ""
    if auth_header.lower().startswith("bearer "):
        token = auth_header[7:].strip()
    sim = deps.store().get_by_token(token)
    if sim is None:
        return JSONResponse(
            {
                "ok": False,
                "message": "Jeton invalide. Laisse le champ jeton vide dans F1 Chronos pour l’auto-enregistrement.",
            },
            status_code=401,
        )
    try:
        payload = await request.json()
    except Exception:
        return JSONResponse({"ok": False, "message": "JSON invalide."}, status_code=400)

    jobs = deps.store().ingest(sim, payload)
    return {
        "ok": True,
        "serverTime": db.utcnow(),
        "commands": deps.store().jobs_as_commands(jobs),
    }


# ---------------------------------------------------------------------------
# Auth (session cookie signé)
# ---------------------------------------------------------------------------


class LoginIn(BaseModel):
    email: str = Field(default="", max_length=254)
    password: str = Field(default="", max_length=256)


class SetupIn(BaseModel):
    email: str = Field(default="", max_length=254)
    password: str = Field(default="", max_length=256)


class ChangePasswordIn(BaseModel):
    current_password: str = Field(default="", max_length=256)
    new_password: str = Field(default="", max_length=256)


def _client_key(request: Request) -> str:
    return request.client.host if request.client else "unknown"


@app.get("/api/v1/auth/me")
def auth_me(request: Request):
    user = deps.current_user(request)
    profile_required = bool(user and user_out(user).get("profileRequired"))
    return {
        "ok": True,
        "authenticated": user is not None,
        "setupRequired": not deps.auth().has_users(),
        "publicAccess": deps.store().get_public_access(),
        "profileRequired": profile_required,
        "user": user_out(user) if user else None,
    }


@app.post("/api/v1/auth/setup")
def auth_setup(request: Request, body: SetupIn):
    """Bootstrap du premier admin — refusé dès qu'un compte existe."""
    if deps.auth().has_users():
        raise HTTPException(403, "Le premier compte existe déjà.")
    key = f"setup:{_client_key(request)}"
    if deps.limiter().blocked(key):
        raise HTTPException(429, "Trop de tentatives. Réessaie dans quelques minutes.")
    try:
        user = deps.auth().create_user(body.email, body.password, "admin")
    except ValueError as exc:
        deps.limiter().hit(key)
        return _err(exc)
    deps.limiter().reset(key)
    request.session["user_id"] = user["id"]
    return {"ok": True, "user": user_out(user)}


@app.post("/api/v1/auth/login")
def auth_login(request: Request, body: LoginIn):
    key = f"login:{_client_key(request)}"
    if deps.limiter().blocked(key):
        raise HTTPException(429, "Trop de tentatives. Réessaie dans quelques minutes.")
    user = deps.auth().verify_credentials(body.email, body.password)
    if user is None:
        deps.limiter().hit(key)
        return JSONResponse(
            {"ok": False, "message": "Identifiants incorrects."}, status_code=401
        )
    deps.limiter().reset(key)
    request.session.clear()
    request.session["user_id"] = user["id"]
    return {"ok": True, "user": user_out(user)}


@app.post("/api/v1/auth/logout")
def auth_logout(request: Request):
    request.session.clear()
    return {"ok": True}


@app.post("/api/v1/auth/change-password")
def auth_change_password(request: Request, body: ChangePasswordIn):
    user = deps.require_user(request)
    try:
        deps.auth().change_password(user["id"], body.current_password, body.new_password)
    except ValueError as exc:
        return _err(exc)
    return {"ok": True, "message": "Mot de passe mis à jour."}


class ProfileSimPseudoIn(BaseModel):
    sim_pseudo: str = Field(default="", max_length=20)


@app.patch("/api/v1/profile/sim-pseudo")
def profile_sim_pseudo(request: Request, body: ProfileSimPseudoIn):
    user = deps.require_simracer(request)
    try:
        updated = deps.auth().update_sim_pseudo(user["id"], body.sim_pseudo)
    except ValueError as exc:
        return _err(exc)
    return {"ok": True, "user": user_out(updated), "message": "Pseudo simulateur enregistré."}


@app.post("/api/v1/sims/{sim_id}/apply-my-pseudo")
def apply_my_sim_pseudo(request: Request, sim_id: str):
    """SimRacer : applique son pseudo de profil sur la session en cours du simulateur."""
    user = deps.require_simracer(request)
    pseudo = (user.get("sim_pseudo") or "").strip()
    if not pseudo:
        raise HTTPException(400, "Configurez votre pseudo simulateur dans votre profil.")
    deps.sim_or_404(sim_id, user)
    if not deps.store().enqueue_set_player_name(sim_id, pseudo):
        raise HTTPException(400, "Simulateur introuvable.")
    return {
        "ok": True,
        "message": f"Pseudo « {pseudo} » en file — appliqué à la prochaine sync du simu.",
    }


# ---------------------------------------------------------------------------
# API de lecture (filtrée par visibilité)
# ---------------------------------------------------------------------------


@app.get("/api/v1/tenants")
def list_tenants(request: Request):
    user = deps.current_user(request)
    return {
        "ok": True,
        "tenants": [tenant_out(t) for t in deps.visible_tenants(user)],
        "publicAccess": deps.store().get_public_access(),
    }


@app.get("/api/v1/tenants/{tenant_id}")
def get_tenant(request: Request, tenant_id: str):
    user = deps.current_user(request)
    tenant = deps.tenant_or_404(tenant_id, user)
    sims = deps.store().list_simulators_for_tenant(tenant["id"])
    tenant_payload = tenant_out(tenant)
    tenant_payload["simCount"] = len(sims)
    return {
        "ok": True,
        "tenant": tenant_payload,
        "sims": [sim_out(s) for s in sims],
    }


@app.get("/api/v1/tenants/{tenant_id}/tracks")
def get_tenant_tracks(request: Request, tenant_id: str):
    user = deps.current_user(request)
    tenant = deps.tenant_or_404(tenant_id, user)
    return {
        "ok": True,
        "tracks": [track_out(t) for t in deps.store().tenant_track_summaries(tenant["id"])],
    }


@app.get("/api/v1/tenants/{tenant_id}/leaderboard")
def get_tenant_leaderboard(
    request: Request,
    tenant_id: str,
    track_id: int,
    best: bool = True,
    page: int = 1,
    page_size: int = DEFAULT_PAGE_SIZE,
):
    user = deps.current_user(request)
    tenant = deps.tenant_or_404(tenant_id, user)
    board = deps.store().tenant_leaderboard(
        tenant["id"], track_id, best_per_player=best, page=page, page_size=page_size
    )
    return {"ok": True, **board_out(board)}


@app.get("/api/v1/sims")
def list_sims(request: Request):
    user = deps.current_user(request)
    store = deps.store()
    sims = []
    for tenant in deps.visible_tenants(user):
        for sim in store.list_simulators_for_tenant(tenant["id"]):
            entry = sim_out(sim)
            entry["tenantLabel"] = tenant["label"]
            sims.append(entry)
    return {"ok": True, "sims": sims}


@app.get("/api/v1/sims/{sim_id}")
def get_sim(request: Request, sim_id: str):
    user = deps.current_user(request)
    sim = deps.sim_or_404(sim_id, user)
    tenant = None
    if sim.get("tenant_id"):
        t = deps.store().get_tenant(sim["tenant_id"])
        tenant = tenant_out(t) if t else None
    return {"ok": True, "sim": sim_out(sim), "tenant": tenant}


@app.get("/api/v1/sims/{sim_id}/tracks")
def get_sim_tracks(request: Request, sim_id: str, contest_id: str | None = None):
    user = deps.current_user(request)
    deps.sim_or_404(sim_id, user)
    return {
        "ok": True,
        "tracks": [track_out(t) for t in deps.store().track_summaries(sim_id, contest_id)],
    }


@app.get("/api/v1/sims/{sim_id}/leaderboard")
def get_sim_leaderboard(
    request: Request,
    sim_id: str,
    track_id: int,
    contest_id: str | None = None,
    best: bool = True,
    page: int = 1,
    page_size: int = DEFAULT_PAGE_SIZE,
):
    user = deps.current_user(request)
    deps.sim_or_404(sim_id, user)
    board = deps.store().leaderboard(
        sim_id, track_id, contest_id, best_per_player=best, page=page, page_size=page_size
    )
    return {"ok": True, **board_out(board)}


@app.get("/api/v1/sims/{sim_id}/recent-laps")
def get_sim_recent_laps(
    request: Request,
    sim_id: str,
    limit: int = DEFAULT_RECENT_LAPS,
    contest_id: str | None = None,
):
    user = deps.require_admin(request)
    deps.sim_or_404(sim_id, user)
    rows = deps.store().recent_laps(sim_id, contest_id, limit)
    return {"ok": True, "rows": [lap_out(r) for r in rows]}


@app.get("/api/v1/tenants/{tenant_id}/recent-laps")
def get_tenant_recent_laps(
    request: Request,
    tenant_id: str,
    limit: int = DEFAULT_RECENT_LAPS,
):
    user = deps.require_admin(request)
    tenant = deps.tenant_or_404(tenant_id, user)
    rows = deps.store().tenant_recent_laps(tenant["id"], limit)
    return {"ok": True, "rows": [lap_out(r) for r in rows]}


@app.get("/api/v1/sims/{sim_id}/contests")
def get_sim_contests(request: Request, sim_id: str):
    user = deps.current_user(request)
    deps.sim_or_404(sim_id, user)
    return {
        "ok": True,
        "contests": [contest_out(c) for c in deps.store().list_contests(sim_id)],
    }


@app.get("/api/v1/sims/{sim_id}/contests/{contest_id}")
def get_sim_contest(request: Request, sim_id: str, contest_id: str):
    user = deps.current_user(request)
    deps.sim_or_404(sim_id, user)
    contest = deps.store().get_contest(sim_id, contest_id)
    if contest is None:
        raise HTTPException(404, "Concours introuvable.")
    return {"ok": True, "contest": contest_out(contest)}


# ---------------------------------------------------------------------------
# Flux live (SSE) : un battement « les données ont changé », sans contenu.
# Les clients re-téléchargent ensuite leur vue via les endpoints filtrés.
# ---------------------------------------------------------------------------


# Durée de vie bornée : le client (EventSource) reconnecte automatiquement,
# ce qui purge les connexions zombies à travers les proxies et simplifie les tests.
STREAM_POLL_SECONDS = float(os.environ.get("RESULTS_STREAM_POLL", "2"))
STREAM_MAX_SECONDS = float(os.environ.get("RESULTS_STREAM_MAX_AGE", "300"))


@app.get("/api/v1/stream")
async def stream(request: Request):
    async def events():
        last = -1
        elapsed = 0.0
        while elapsed < STREAM_MAX_SECONDS:
            if await request.is_disconnected():
                break
            version = deps.store().data_version
            if version != last:
                last = version
                yield f"data: {version}\n\n"
            await asyncio.sleep(STREAM_POLL_SECONDS)
            elapsed += STREAM_POLL_SECONDS

    return StreamingResponse(
        events(),
        media_type="text/event-stream",
        headers={"X-Accel-Buffering": "no"},
    )


# ---------------------------------------------------------------------------
# Admin + SPA
# ---------------------------------------------------------------------------

app.include_router(admin_router)


@app.get("/{full_path:path}", include_in_schema=False)
def spa(full_path: str):
    if full_path.startswith("api/"):
        raise HTTPException(404, "Introuvable.")
    return FileResponse(STATIC_DIR / "index.html")
