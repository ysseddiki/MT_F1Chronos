from __future__ import annotations

import os
from pathlib import Path

from urllib.parse import quote

from fastapi import FastAPI, Form, HTTPException, Request
from fastapi.responses import HTMLResponse, JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from starlette.middleware.sessions import SessionMiddleware
from uvicorn.middleware.proxy_headers import ProxyHeadersMiddleware

from . import db
from .auth import MIN_PASSWORD_LENGTH, AdminAuth
from .store import ResultsStore, format_lap

BASE = Path(__file__).resolve().parent
DATA_DIR = Path(os.environ.get("RESULTS_DATA") or (BASE.parent / "data"))
SECRET = os.environ.get("RESULTS_SECRET") or "dev-change-me"

app = FastAPI(title="F1 Chronos — Résultats")
# Caddy termine TLS ; faire confiance aux en-têtes X-Forwarded-* pour les redirects / cookies.
app.add_middleware(ProxyHeadersMiddleware, trusted_hosts="*")
app.add_middleware(SessionMiddleware, secret_key=SECRET, same_site="lax")
app.mount("/static", StaticFiles(directory=BASE / "static"), name="static")
templates = Jinja2Templates(directory=str(BASE / "templates"))
templates.env.filters["lap"] = format_lap

_store: ResultsStore | None = None
_auth: AdminAuth | None = None


def _ensure() -> tuple[ResultsStore, AdminAuth]:
    global _store, _auth
    if _store is None:
        conn = db.connect(DATA_DIR / "results.sqlite")
        _store = ResultsStore(conn)
        _auth = AdminAuth(conn)
        _auth.seed_from_env(os.environ.get("RESULTS_ADMIN_PASSWORD") or "")
    assert _auth is not None
    return _store, _auth


def store() -> ResultsStore:
    return _ensure()[0]


def auth() -> AdminAuth:
    return _ensure()[1]


def page(request: Request, name: str, ctx: dict) -> HTMLResponse:
    ctx["request"] = request
    return templates.TemplateResponse(name, ctx)


def is_admin(request: Request) -> bool:
    if not auth().has_password():
        return True
    return request.session.get("admin") is True


def require_admin(request: Request) -> RedirectResponse | None:
    if is_admin(request):
        return None
    return RedirectResponse("/admin/login", status_code=303)


@app.get("/api/v1/health")
def health():
    return {"ok": True, "protocolVersion": 1, "authRequired": True}


@app.post("/api/v1/sync")
async def sync(request: Request):
    token = request.headers.get("X-Results-Token") or ""
    auth = request.headers.get("Authorization") or ""
    if auth.lower().startswith("bearer "):
        token = auth[7:].strip()
    sim = store().get_by_token(token)
    if sim is None:
        return JSONResponse(
            {"ok": False, "message": "Jeton invalide. Crée le simulateur dans l’admin web."},
            status_code=401,
        )
    try:
        payload = await request.json()
    except Exception:
        return JSONResponse({"ok": False, "message": "JSON invalide."}, status_code=400)

    jobs = store().ingest(sim, payload)
    return {
        "ok": True,
        "serverTime": db.utcnow(),
        "commands": store().jobs_as_commands(jobs),
    }


@app.get("/", response_class=HTMLResponse)
def home(request: Request, best: bool = False):
    sims = store().list_simulators()
    current = sims[0] if len(sims) == 1 else None
    tracks = store().track_summaries(current["id"]) if current else []
    preview = []
    preview_name = ""
    if current and tracks:
        focus = current["current_track_id"] if current["current_track_id"] >= 0 else tracks[0]["track_id"]
        preview = store().leaderboard(current["id"], focus, best_per_player=best)
        preview_name = next((t["track_name"] for t in tracks if t["track_id"] == focus), "")
    return page(request,
        "index.html",
        {
            "sims": sims,
            "sim": current,
            "tracks": tracks,
            "preview": preview,
            "preview_name": preview_name,
            "best": best,
            "admin": is_admin(request),
        },
    )


@app.get("/sim/{sim_id}", response_class=HTMLResponse)
def sim_home(request: Request, sim_id: str, best: bool = False):
    sim = store().get_simulator(sim_id)
    if sim is None:
        raise HTTPException(404, "Simulateur introuvable")
    tracks = store().track_summaries(sim_id)
    preview = []
    preview_name = ""
    if tracks:
        focus = sim["current_track_id"] if sim["current_track_id"] >= 0 else tracks[0]["track_id"]
        preview = store().leaderboard(sim_id, focus, best_per_player=best)
        preview_name = next((t["track_name"] for t in tracks if t["track_id"] == focus), "")
    return page(request,
        "index.html",
        {
            "sims": store().list_simulators(),
            "sim": sim,
            "tracks": tracks,
            "preview": preview,
            "preview_name": preview_name,
            "best": best,
            "admin": is_admin(request),
        },
    )


@app.get("/sim/{sim_id}/tracks/{track_id}", response_class=HTMLResponse)
def track_page(request: Request, sim_id: str, track_id: int, best: bool = False):
    sim = store().get_simulator(sim_id)
    if sim is None:
        raise HTTPException(404)
    rows = store().leaderboard(sim_id, track_id, best_per_player=best)
    name = rows[0]["track_name"] if rows else f"Circuit {track_id}"
    return page(request,
        "track.html",
        {"sim": sim, "track_id": track_id, "track_name": name, "rows": rows, "best": best, "admin": is_admin(request)},
    )


@app.get("/contests", response_class=HTMLResponse)
def contests_index(request: Request, sim: str | None = None):
    sims = store().list_simulators()
    current = next((s for s in sims if s["id"] == sim), None) or (sims[0] if sims else None)
    contests = store().list_contests(current["id"]) if current else []
    return page(request,
        "contests.html",
        {"sims": sims, "sim": current, "contests": contests, "admin": is_admin(request)},
    )


@app.get("/sim/{sim_id}/contests/{contest_id}", response_class=HTMLResponse)
def contest_page(request: Request, sim_id: str, contest_id: str, track_id: int | None = None, best: bool = False):
    sim = store().get_simulator(sim_id)
    contest = store().get_contest(sim_id, contest_id)
    if sim is None or contest is None:
        raise HTTPException(404)
    tracks = store().track_summaries(sim_id, contest_id)
    tid = track_id if track_id is not None else (tracks[0]["track_id"] if tracks else None)
    rows = store().leaderboard(sim_id, tid, contest_id, best) if tid is not None else []
    tname = next((t["track_name"] for t in tracks if t["track_id"] == tid), "")
    return page(request,
        "contest.html",
        {
            "sim": sim,
            "contest": contest,
            "tracks": tracks,
            "track_id": tid,
            "track_name": tname,
            "rows": rows,
            "best": best,
            "admin": is_admin(request),
        },
    )


@app.get("/admin/login", response_class=HTMLResponse)
def admin_login_get(request: Request):
    if is_admin(request):
        return RedirectResponse("/admin", status_code=303)
    return page(request, "admin_login.html", {"error": None})


@app.post("/admin/login")
def admin_login_post(request: Request, password: str = Form("")):
    if auth().has_password() and not auth().verify(password):
        return page(
            request, "admin_login.html", {"error": "Mot de passe incorrect."}
        )
    request.session["admin"] = True
    return RedirectResponse("/admin", status_code=303)


@app.get("/admin/logout")
def admin_logout(request: Request):
    request.session.clear()
    return RedirectResponse("/", status_code=303)


@app.get("/admin", response_class=HTMLResponse)
def admin_home(
    request: Request,
    sim: str | None = None,
    contest_id: str | None = None,
    track_id: int | None = None,
    status: str | None = None,
    new_token: str | None = None,
):
    gate = require_admin(request)
    if gate:
        return gate
    sims = store().list_simulators()
    current = next((s for s in sims if s["id"] == sim), None) or (sims[0] if sims else None)
    contests = store().list_contests(current["id"]) if current else []
    tracks = store().track_summaries(current["id"], contest_id) if current else []
    tid = track_id if track_id is not None else (tracks[0]["track_id"] if tracks else None)
    rows = store().leaderboard(current["id"], tid, contest_id) if current and tid is not None else []
    tname = next((t["track_name"] for t in tracks if t["track_id"] == tid), "")
    jobs = store().list_jobs(current["id"]) if current else []
    return page(request,
        "admin.html",
        {
            "sims": sims,
            "sim": current,
            "contests": contests,
            "contest_id": contest_id,
            "tracks": tracks,
            "track_id": tid,
            "track_name": tname,
            "rows": rows,
            "jobs": jobs,
            "status": status,
            "new_token": new_token,
            "admin": True,
            "has_password": auth().has_password(),
        },
    )


@app.post("/admin/simulators")
def admin_create_sim(request: Request, label: str = Form("Simulateur")):
    gate = require_admin(request)
    if gate:
        return gate
    created, token = store().create_simulator(label)
    return RedirectResponse(
        f"/admin?sim={created['id']}&new_token={token}&status=Simulateur créé — copie le jeton dans l’admin F1 Chronos.",
        status_code=303,
    )


@app.post("/admin/laps/{entry_id}/delete")
def admin_delete(
    request: Request,
    entry_id: str,
    sim: str = Form(...),
    contest_id: str = Form(""),
    track_id: str = Form(""),
):
    gate = require_admin(request)
    if gate:
        return gate
    store().admin_delete_entry(sim, entry_id)
    q = _admin_qs(sim, contest_id, track_id, "Chrono retiré — job en file pour le simu.")
    return RedirectResponse(f"/admin?{q}", status_code=303)


@app.post("/admin/laps/{entry_id}/rename")
def admin_rename_one(
    request: Request,
    entry_id: str,
    sim: str = Form(...),
    new_name: str = Form(...),
    contest_id: str = Form(""),
    track_id: str = Form(""),
):
    gate = require_admin(request)
    if gate:
        return gate
    store().admin_rename_entry(sim, entry_id, new_name)
    q = _admin_qs(sim, contest_id, track_id, "Pseudo modifié (ce chrono) — job en file.")
    return RedirectResponse(f"/admin?{q}", status_code=303)


@app.post("/admin/players/rename")
def admin_rename_all(
    request: Request,
    sim: str = Form(...),
    old_name: str = Form(...),
    new_name: str = Form(...),
    contest_id: str = Form(""),
    track_id: str = Form(""),
):
    gate = require_admin(request)
    if gate:
        return gate
    cid = contest_id or None
    store().admin_rename_player(sim, cid, old_name, new_name)
    q = _admin_qs(sim, contest_id, track_id, "Pseudo modifié (tous les temps) — job en file.")
    return RedirectResponse(f"/admin?{q}", status_code=303)


@app.post("/admin/password")
def admin_change_password(
    request: Request,
    current_password: str = Form(""),
    new_password: str = Form(""),
    confirm_password: str = Form(""),
):
    gate = require_admin(request)
    if gate:
        return gate
    if auth().has_password() and not auth().verify(current_password):
        msg = "Mot de passe actuel incorrect."
    elif len(new_password) < MIN_PASSWORD_LENGTH:
        msg = f"Le nouveau mot de passe doit faire au moins {MIN_PASSWORD_LENGTH} caractères."
    elif new_password != confirm_password:
        msg = "La confirmation ne correspond pas."
    else:
        auth().set_password(new_password)
        request.session["admin"] = True
        msg = "Mot de passe admin mis à jour. Le .env n’est plus utilisé pour le login."
    return RedirectResponse(f"/admin?status={quote(msg)}", status_code=303)


@app.post("/admin/jobs/{job_id}/revert")
def admin_revert(request: Request, job_id: str, sim: str = Form(...)):
    gate = require_admin(request)
    if gate:
        return gate
    err = store().revert_job(sim, job_id)
    msg = "Job revert." if err is None else f"Revert impossible : {err}"
    return RedirectResponse(f"/admin?sim={sim}&status={msg}", status_code=303)


def _admin_qs(sim: str, contest_id: str, track_id: str, status: str) -> str:
    parts = [f"sim={sim}", f"status={quote(status)}"]
    if contest_id:
        parts.append(f"contest_id={contest_id}")
    if track_id:
        parts.append(f"track_id={track_id}")
    return "&".join(parts)
