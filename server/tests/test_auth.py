from pathlib import Path

from app.auth import AdminAuth, hash_password, verify_password
from app.db import connect


def _auth(tmp_path: Path) -> AdminAuth:
    return AdminAuth(connect(tmp_path / "t.sqlite"))


def test_hash_roundtrip():
    stored = hash_password("secret-admin")
    assert stored.startswith("pbkdf2_sha256$")
    assert verify_password("secret-admin", stored)
    assert not verify_password("other", stored)
    assert not verify_password("", stored)


def test_env_seeds_only_when_empty(tmp_path: Path):
    auth = _auth(tmp_path)
    assert not auth.has_password()
    auth.seed_from_env("  first-pass  ")
    assert auth.has_password()
    assert auth.verify("first-pass")

    auth.seed_from_env("changed-in-env")
    assert auth.verify("first-pass")
    assert not auth.verify("changed-in-env")


def test_empty_env_leaves_admin_open(tmp_path: Path):
    auth = _auth(tmp_path)
    auth.seed_from_env("")
    auth.seed_from_env("   ")
    assert not auth.has_password()


def test_change_password_ignores_later_env(tmp_path: Path):
    auth = _auth(tmp_path)
    auth.seed_from_env("initial")
    auth.set_password("from-web")
    auth.seed_from_env("initial")
    assert auth.verify("from-web")
    assert not auth.verify("initial")
