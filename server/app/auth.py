from __future__ import annotations

import hashlib
import hmac
import os
import sqlite3

PBKDF2_ITERATIONS = 100_000
HASH_LENGTH = 32
SALT_LENGTH = 16
MIN_PASSWORD_LENGTH = 4
SETTINGS_KEY = "admin_password_hash"


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


class AdminAuth:
    """Admin password lives in SQLite. RESULTS_ADMIN_PASSWORD only seeds an empty DB."""

    def __init__(self, conn: sqlite3.Connection):
        self._conn = conn

    def has_password(self) -> bool:
        return self._stored_hash() is not None

    def seed_from_env(self, env_password: str) -> None:
        if self.has_password():
            return
        trimmed = (env_password or "").strip()
        if not trimmed:
            return
        self.set_password(trimmed)

    def set_password(self, password: str) -> None:
        stored = hash_password(password)
        self._conn.execute(
            """INSERT INTO settings (key, value) VALUES (?, ?)
               ON CONFLICT(key) DO UPDATE SET value = excluded.value""",
            (SETTINGS_KEY, stored),
        )
        self._conn.commit()

    def verify(self, password: str) -> bool:
        stored = self._stored_hash()
        if stored is None:
            return False
        return verify_password(password, stored)

    def _stored_hash(self) -> str | None:
        row = self._conn.execute(
            "SELECT value FROM settings WHERE key = ?",
            (SETTINGS_KEY,),
        ).fetchone()
        if row is None:
            return None
        value = row["value"] if isinstance(row, sqlite3.Row) else row[0]
        return value if value else None
