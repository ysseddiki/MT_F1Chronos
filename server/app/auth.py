from __future__ import annotations

import hashlib
import hmac
import os
import re
import sqlite3
import uuid

from . import db

PBKDF2_ITERATIONS = 100_000
HASH_LENGTH = 32
SALT_LENGTH = 16
MIN_PASSWORD_LENGTH = 8
MAX_EMAIL_LENGTH = 254

ROLE_ADMIN = "admin"
ROLE_VISITOR = "visitor"
ROLES = (ROLE_ADMIN, ROLE_VISITOR)

DEFAULT_ADMIN_EMAIL = "admin@localhost"
LEGACY_SETTINGS_KEY = "admin_password_hash"

# local@domaine — le domaine peut être sans point (ex. admin@localhost, serveur privé).
EMAIL_RE = re.compile(r"^[^@\s]{1,64}@[^@\s]{1,190}$")


def hash_password(password: str) -> str:
    salt = os.urandom(SALT_LENGTH)
    digest = hashlib.pbkdf2_hmac(
        "sha256",
        password.encode("utf-8"),
        salt,
        PBKDF2_ITERATIONS,
        dklen=HASH_LENGTH,
    )
    return f"pbkdf2_sha256${PBKDF2_ITERATIONS}${salt.hex()}${digest.hex()}"


def verify_password(password: str, stored: str) -> bool:
    if not password or not stored:
        return False
    try:
        algo, iters_s, salt_hex, digest_hex = stored.split("$", 3)
        if algo != "pbkdf2_sha256":
            return False
        iterations = int(iters_s)
        salt = bytes.fromhex(salt_hex)
        expected = bytes.fromhex(digest_hex)
        actual = hashlib.pbkdf2_hmac(
            "sha256",
            password.encode("utf-8"),
            salt,
            iterations,
            dklen=len(expected),
        )
        return hmac.compare_digest(actual, expected)
    except (ValueError, TypeError):
        return False


def normalize_email(email: str) -> str:
    return (email or "").strip().lower()


class UserAuth:
    """Users (admin/visitor) in SQLite. RESULTS_ADMIN_PASSWORD only seeds the first admin."""

    def __init__(self, conn: sqlite3.Connection):
        self._conn = conn

    # --- bootstrap ---

    def count_users(self) -> int:
        return self._conn.execute("SELECT COUNT(*) FROM users").fetchone()[0]

    def has_users(self) -> bool:
        return self.count_users() > 0

    def seed_from_env(self, env_password: str) -> None:
        """First boot: legacy settings hash → admin user, else env password → admin user."""
        if self.has_users():
            return
        legacy = self._legacy_hash()
        if legacy:
            self._insert_user(DEFAULT_ADMIN_EMAIL, legacy, ROLE_ADMIN)
            self._conn.execute("DELETE FROM settings WHERE key = ?", (LEGACY_SETTINGS_KEY,))
            self._conn.commit()
            return
        trimmed = (env_password or "").strip()
        if trimmed:
            self._insert_user(DEFAULT_ADMIN_EMAIL, hash_password(trimmed), ROLE_ADMIN)

    def _legacy_hash(self) -> str | None:
        row = self._conn.execute(
            "SELECT value FROM settings WHERE key = ?", (LEGACY_SETTINGS_KEY,)
        ).fetchone()
        if row is None:
            return None
        value = row["value"] if isinstance(row, sqlite3.Row) else row[0]
        return value or None

    # --- reads ---

    def _user_with_access(self, row: sqlite3.Row | None) -> dict | None:
        if row is None:
            return None
        user = dict(row)
        user["disabled"] = bool(user.get("disabled"))
        user["tenant_ids"] = self.tenant_ids_for_user(user["id"])
        return user

    def get_user(self, user_id: str) -> dict | None:
        row = self._conn.execute("SELECT * FROM users WHERE id = ?", (user_id,)).fetchone()
        return self._user_with_access(row)

    def get_user_by_email(self, email: str) -> dict | None:
        row = self._conn.execute(
            "SELECT * FROM users WHERE email = ?", (normalize_email(email),)
        ).fetchone()
        return self._user_with_access(row)

    def list_users(self) -> list[dict]:
        rows = self._conn.execute(
            "SELECT * FROM users ORDER BY email COLLATE NOCASE"
        ).fetchall()
        return [self._user_with_access(r) for r in rows]

    def tenant_ids_for_user(self, user_id: str) -> list[str]:
        rows = self._conn.execute(
            "SELECT tenant_id FROM user_tenant_access WHERE user_id = ?", (user_id,)
        ).fetchall()
        return [r["tenant_id"] for r in rows]

    def verify_credentials(self, email: str, password: str) -> dict | None:
        user = self.get_user_by_email(email)
        if user is None or user["disabled"]:
            return None
        if not verify_password(password, user["password_hash"]):
            return None
        return user

    def _active_admin_count(self) -> int:
        return self._conn.execute(
            "SELECT COUNT(*) FROM users WHERE role = ? AND disabled = 0", (ROLE_ADMIN,)
        ).fetchone()[0]

    # --- writes ---

    def _insert_user(self, email: str, password_hash: str, role: str) -> dict:
        user_id = uuid.uuid4().hex
        self._conn.execute(
            "INSERT INTO users (id, email, password_hash, role, disabled, created_at) VALUES (?, ?, ?, ?, 0, ?)",
            (user_id, email, password_hash, role, db.utcnow()),
        )
        self._conn.commit()
        user = self.get_user(user_id)
        assert user is not None
        return user

    def _validate(self, email: str, password: str | None, role: str) -> str:
        email = normalize_email(email)
        if not email or len(email) > MAX_EMAIL_LENGTH or not EMAIL_RE.match(email):
            raise ValueError("Adresse e-mail invalide.")
        if role not in ROLES:
            raise ValueError("Rôle invalide.")
        if password is not None and len(password) < MIN_PASSWORD_LENGTH:
            raise ValueError(
                f"Le mot de passe doit faire au moins {MIN_PASSWORD_LENGTH} caractères."
            )
        return email

    def create_user(
        self,
        email: str,
        password: str,
        role: str,
        tenant_ids: list[str] | None = None,
    ) -> dict:
        email = self._validate(email, password, role)
        if self.get_user_by_email(email) is not None:
            raise ValueError("Un compte existe déjà avec cette adresse.")
        user = self._insert_user(email, hash_password(password), role)
        if tenant_ids:
            self.set_tenant_access(user["id"], tenant_ids)
            user = self.get_user(user["id"])
        return user

    def update_user(
        self,
        user_id: str,
        *,
        role: str | None = None,
        disabled: bool | None = None,
        password: str | None = None,
        tenant_ids: list[str] | None = None,
    ) -> dict:
        user = self.get_user(user_id)
        if user is None:
            raise ValueError("Compte introuvable.")
        if role is not None and role not in ROLES:
            raise ValueError("Rôle invalide.")
        if password is not None and len(password) < MIN_PASSWORD_LENGTH:
            raise ValueError(
                f"Le mot de passe doit faire au moins {MIN_PASSWORD_LENGTH} caractères."
            )

        demotes_admin = (
            user["role"] == ROLE_ADMIN
            and not user["disabled"]
            and ((role is not None and role != ROLE_ADMIN) or disabled is True)
        )
        if demotes_admin and self._active_admin_count() <= 1:
            raise ValueError("Impossible : il doit rester au moins un administrateur actif.")

        if role is not None:
            self._conn.execute("UPDATE users SET role = ? WHERE id = ?", (role, user_id))
        if disabled is not None:
            self._conn.execute(
                "UPDATE users SET disabled = ? WHERE id = ?", (1 if disabled else 0, user_id)
            )
        if password is not None:
            self._conn.execute(
                "UPDATE users SET password_hash = ? WHERE id = ?",
                (hash_password(password), user_id),
            )
        self._conn.commit()
        if tenant_ids is not None:
            self.set_tenant_access(user_id, tenant_ids)
        updated = self.get_user(user_id)
        assert updated is not None
        return updated

    def delete_user(self, user_id: str) -> None:
        user = self.get_user(user_id)
        if user is None:
            raise ValueError("Compte introuvable.")
        if user["role"] == ROLE_ADMIN and not user["disabled"] and self._active_admin_count() <= 1:
            raise ValueError("Impossible : il doit rester au moins un administrateur actif.")
        self._conn.execute("DELETE FROM user_tenant_access WHERE user_id = ?", (user_id,))
        self._conn.execute("DELETE FROM users WHERE id = ?", (user_id,))
        self._conn.commit()

    def set_tenant_access(self, user_id: str, tenant_ids: list[str]) -> None:
        self._conn.execute("DELETE FROM user_tenant_access WHERE user_id = ?", (user_id,))
        for tenant_id in dict.fromkeys(tenant_ids):
            self._conn.execute(
                "INSERT OR IGNORE INTO user_tenant_access (user_id, tenant_id) VALUES (?, ?)",
                (user_id, tenant_id),
            )
        self._conn.commit()

    def change_password(self, user_id: str, current_password: str, new_password: str) -> None:
        user = self.get_user(user_id)
        if user is None:
            raise ValueError("Compte introuvable.")
        if not verify_password(current_password, user["password_hash"]):
            raise ValueError("Mot de passe actuel incorrect.")
        if len(new_password) < MIN_PASSWORD_LENGTH:
            raise ValueError(
                f"Le nouveau mot de passe doit faire au moins {MIN_PASSWORD_LENGTH} caractères."
            )
        self._conn.execute(
            "UPDATE users SET password_hash = ? WHERE id = ?",
            (hash_password(new_password), user_id),
        )
        self._conn.commit()
