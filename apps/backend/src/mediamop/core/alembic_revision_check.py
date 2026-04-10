"""Fail fast when the SQLite database is not at this build's Alembic head.

Local installs must not run with silent schema drift (missing tables, wrong revision).
Verification logic is shared with ``scripts/verify_local_db.py``.
"""

from __future__ import annotations

from pathlib import Path

from alembic.config import Config
from alembic.runtime.migration import MigrationContext
from alembic.script import ScriptDirectory
from sqlalchemy import text
from sqlalchemy.engine import Engine


class DatabaseSchemaMismatch(RuntimeError):
    """Recorded Alembic revision does not match this application."""

    def __init__(self, message: str, *, kind: str) -> None:
        super().__init__(message)
        self.kind = kind


def _backend_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _script_and_head() -> tuple[ScriptDirectory, str]:
    backend = _backend_root()
    ini = backend / "alembic.ini"
    if not ini.is_file():
        msg = f"Missing {ini}; cannot verify database schema revision."
        raise RuntimeError(msg)

    cfg = Config(str(ini))
    # ``alembic.ini`` uses a relative ``script_location``; resolve against backend root so
    # checks work regardless of process cwd (API startup, verify script from repo root).
    cfg.set_main_option("script_location", str(backend / "alembic"))
    script = ScriptDirectory.from_config(cfg)
    heads = script.get_heads()
    if len(heads) != 1:
        msg = f"Expected a single Alembic head, got {heads!r}."
        raise RuntimeError(msg)
    return script, heads[0]


def _strictly_behind_head(script: ScriptDirectory, *, head: str, current: str) -> bool:
    """True if *current* is a strict ancestor of *head* on a linear down_revision chain."""

    rev = script.get_revision(head)
    seen: set[str] = set()
    while rev is not None and rev.revision not in seen:
        seen.add(rev.revision)
        dr = rev.down_revision
        if dr is None:
            break
        if isinstance(dr, tuple):
            return False
        if dr == current:
            return True
        rev = script.get_revision(dr)
    return False


def require_database_at_application_head(engine: Engine) -> None:
    """Raise :class:`DatabaseSchemaMismatch` or ``RuntimeError`` if the DB is not migration-ready.

    Call after the SQLAlchemy engine exists and before serving API traffic.
    """

    script, head = _script_and_head()

    with engine.connect() as conn:
        conn.execute(text("SELECT 1"))
        ctx = MigrationContext.configure(conn)
        current = ctx.get_current_revision()

    if current == head:
        return

    migrate_hint = (
        "Run .\\scripts\\dev-migrate.ps1 or, from apps/backend with PYTHONPATH=src, "
        "`alembic upgrade head`."
    )

    if current is None:
        raise DatabaseSchemaMismatch(
            "No Alembic revision is recorded for this database (migrations have not been applied). "
            f"This build requires schema revision {head!r}. "
            + migrate_hint,
            kind="unversioned",
        )

    try:
        script.get_revision(current)
    except Exception as exc:
        raise DatabaseSchemaMismatch(
            f"Database revision {current!r} is not recognized by this MediaMop build "
            f"(expected head {head!r}). The database may come from a newer release; "
            "upgrade the application or restore a backup that matches this version.",
            kind="unknown_revision",
        ) from exc

    if _strictly_behind_head(script, head=head, current=current):
        raise DatabaseSchemaMismatch(
            f"Database schema is behind this build (at {current!r}, required {head!r}). "
            + migrate_hint,
            kind="behind",
        )

    raise DatabaseSchemaMismatch(
        f"Database revision {current!r} does not match this build ({head!r}). "
        "Use a matching application version or run migrations if you downgraded the app.",
        kind="incompatible",
    )
