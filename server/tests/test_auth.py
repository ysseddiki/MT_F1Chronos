from pathlib import Path

import pytest

from app.auth import (
    DEFAULT_ADMIN_EMAIL,
    MIN_PASSWORD_LENGTH,
    UserAuth,
    hash_password,
    verify_password,
)
from app import db
from app.db import connect


def _auth(tmp_path: Path) -> UserAuth:
    return UserAuth(connect(tmp_path / "t.sqlite"))


def test_hash_roundtrip():
    stored = hash_password("secret-admin")
    assert stored.startswith("pbkdf2_sha256$")
    assert verify_password("secret-admin", stored)
    assert not verify_password("other", stored)
    assert not verify_password("", stored)


def test_env_seeds_default_admin_once(tmp_path: Path):
    auth = _auth(tmp_path)
    assert not auth.has_users()
    auth.seed_from_env("  first-pass-1  ")
    user = auth.verify_credentials(DEFAULT_ADMIN_EMAIL, "first-pass-1")
    assert user is not None and user["role"] == "admin"

    # Un second seed ne change rien
    auth.seed_from_env("changed-in-env")
    assert auth.verify_credentials(DEFAULT_ADMIN_EMAIL, "first-pass-1")
    assert auth.verify_credentials(DEFAULT_ADMIN_EMAIL, "changed-in-env") is None


def test_empty_env_leaves_no_users(tmp_path: Path):
    auth = _auth(tmp_path)
    auth.seed_from_env("")
    auth.seed_from_env("   ")
    assert not auth.has_users()


def test_legacy_settings_hash_migrates_to_admin_user(tmp_path: Path):
    conn = connect(tmp_path / "t.sqlite")
    conn.execute(
        "INSERT INTO settings (key, value) VALUES ('admin_password_hash', ?)",
        (hash_password("old-env-pass"),),
    )
    conn.commit()

    auth = UserAuth(conn)
    auth.seed_from_env("new-env-pass")

    assert auth.verify_credentials(DEFAULT_ADMIN_EMAIL, "old-env-pass") is not None
    assert auth.verify_credentials(DEFAULT_ADMIN_EMAIL, "new-env-pass") is None
    row = conn.execute(
        "SELECT COUNT(*) FROM settings WHERE key = 'admin_password_hash'"
    ).fetchone()
    assert row[0] == 0


def test_create_and_verify_visitor(tmp_path: Path):
    auth = _auth(tmp_path)
    auth.create_user("Pilote@Club.fr", "motdepasse", "visitor")
    user = auth.verify_credentials("pilote@club.fr", "motdepasse")
    assert user is not None
    assert user["role"] == "visitor"
    assert auth.verify_credentials("pilote@club.fr", "mauvais") is None


def test_create_and_verify_simracer(tmp_path: Path):
    auth = _auth(tmp_path)
    user = auth.create_user("sim@club.fr", "motdepasse", "simracer")
    assert user["role"] == "simracer"
    assert user.get("sim_pseudo", "") == ""
    updated = auth.update_sim_pseudo(user["id"], "  Capitaine  ")
    assert updated["sim_pseudo"] == "Capitaine"
    with pytest.raises(ValueError):
        auth.update_sim_pseudo(user["id"], "   ")


def test_create_user_validates_input(tmp_path: Path):
    auth = _auth(tmp_path)
    with pytest.raises(ValueError):
        auth.create_user("pas-un-email", "motdepasse", "visitor")
    with pytest.raises(ValueError):
        auth.create_user("a@b.co", "court", "visitor")
    with pytest.raises(ValueError):
        auth.create_user("a@b.co", "motdepasse", "superuser")
    auth.create_user("a@b.co", "motdepasse", "visitor")
    with pytest.raises(ValueError):
        auth.create_user("A@b.co", "motdepasse", "visitor")  # doublon (case-insensitive)


def test_disabled_user_cannot_login(tmp_path: Path):
    auth = _auth(tmp_path)
    user = auth.create_user("v@club.fr", "motdepasse", "visitor")
    auth.update_user(user["id"], disabled=True)
    assert auth.verify_credentials("v@club.fr", "motdepasse") is None
    auth.update_user(user["id"], disabled=False)
    assert auth.verify_credentials("v@club.fr", "motdepasse") is not None


def test_last_admin_is_protected(tmp_path: Path):
    auth = _auth(tmp_path)
    admin = auth.create_user(DEFAULT_ADMIN_EMAIL, "motdepasse", "admin")
    with pytest.raises(ValueError):
        auth.update_user(admin["id"], role="visitor")
    with pytest.raises(ValueError):
        auth.update_user(admin["id"], disabled=True)
    with pytest.raises(ValueError):
        auth.delete_user(admin["id"])

    # Avec un second admin, la protection saute
    auth.create_user("second@localhost", "motdepasse", "admin")
    auth.update_user(admin["id"], role="visitor")
    assert auth.get_user(admin["id"])["role"] == "visitor"


def test_tenant_access_roundtrip(tmp_path: Path):
    conn = connect(tmp_path / "t.sqlite")
    conn.execute(
        "INSERT INTO tenants (id, label, visibility, slug, created_at) VALUES (?, ?, 'public', ?, ?)",
        ("t1", "A", "a", db.utcnow()),
    )
    conn.execute(
        "INSERT INTO tenants (id, label, visibility, slug, created_at) VALUES (?, ?, 'public', ?, ?)",
        ("t2", "B", "b", db.utcnow()),
    )
    conn.commit()
    auth = UserAuth(conn)
    user = auth.create_user("v@club.fr", "motdepasse", "visitor")
    auth.set_tenant_access(user["id"], ["t1", "t2", "t1"])
    assert sorted(auth.tenant_ids_for_user(user["id"])) == ["t1", "t2"]
    with pytest.raises(ValueError):
        auth.set_tenant_access(user["id"], ["missing"])
    auth.set_tenant_access(user["id"], [])
    assert auth.tenant_ids_for_user(user["id"]) == []


def test_admin_promotion_preserves_tenant_access(tmp_path: Path):
    from app.db import connect as db_connect
    from app.store import ResultsStore

    conn = db_connect(tmp_path / "t.sqlite")
    store = ResultsStore(conn)
    tenant = store.create_tenant("Club")
    auth = UserAuth(conn)
    user = auth.create_user("v@club.fr", "motdepasse", "visitor", [tenant["id"]])
    auth.update_user(user["id"], role="admin", tenant_ids=[])
    assert auth.tenant_ids_for_user(user["id"]) == [tenant["id"]]


def test_change_password(tmp_path: Path):
    auth = _auth(tmp_path)
    user = auth.create_user("a@b.co", "ancien-mdp", "visitor")
    with pytest.raises(ValueError):
        auth.change_password(user["id"], "mauvais", "nouveau-mdp")
    with pytest.raises(ValueError):
        auth.change_password(user["id"], "ancien-mdp", "x" * (MIN_PASSWORD_LENGTH - 1))
    auth.change_password(user["id"], "ancien-mdp", "nouveau-mdp")
    assert auth.verify_credentials("a@b.co", "nouveau-mdp") is not None
