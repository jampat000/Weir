"""Bringing up and tearing down the disposable Weir server ``screenshot-site.py`` shoots against.

Split out of the main script (#747). ``REPO_ROOT`` here is recomputed from this file's own
location rather than imported from the entry script, so this module has no import-order
dependency on it.
"""

from __future__ import annotations

import shutil
import sqlite3
import sys
from pathlib import Path

from tests.e2e.weir import _runtime as runtime
from tests.e2e.weir.utils import db_path_for_home

REPO_ROOT = Path(__file__).resolve().parents[2]
WEB_DIST = REPO_ROOT / "apps" / "web" / "dist"
SERVER_PROJECT = REPO_ROOT / "apps" / "server" / "src" / "Weir.Host"


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def check_prerequisites() -> None:
    """Fail loudly and early rather than screenshotting whatever happens to be on disk."""

    if not (WEB_DIST / "index.html").is_file():
        fail("apps/web/dist is missing its build. Run this first:\n    (cd apps/web && npm ci && npm run build)")
    server_dlls = list((REPO_ROOT / "apps" / "server").glob("src/Weir.Host/bin/*/*/Weir.dll"))
    if not server_dlls:
        fail("The .NET server has not been built. Run this first:\n    dotnet build apps/server/src/Weir.Host")
    if shutil.which("dotnet") is None:
        fail("The dotnet CLI is not on PATH.")


def ledger_for_this_checkout() -> Path:
    """Match tests/e2e/weir/conftest.py: parallel worktrees must not reap each other's servers."""

    import hashlib
    import tempfile

    checksum = hashlib.sha1(str(REPO_ROOT.resolve()).encode("utf-8"), usedforsecurity=False).hexdigest()[:10]
    return Path(tempfile.gettempdir()) / f"weir-screenshot-servers-{checksum}.json"


runtime.LEDGER = ledger_for_this_checkout()


def start_weir_server(session_secret: str) -> tuple[runtime.Server, str, str]:
    """A fresh, disposable WEIR_HOME with its own server process. Caller must stop_server() it."""

    import os

    api_port = runtime.pick_free_port()
    home = runtime.run_home(None)
    api_internal = f"http://127.0.0.1:{api_port}"
    logs = Path(home) / "e2e-logs"
    env = {
        **os.environ,
        "WEIR_HOME": home,
        "WEIR_SESSION_SECRET": session_secret,
        "WEIR_CORS_ORIGINS": api_internal,
        "WEIR_WEB_DIST": str(WEB_DIST),
        # One throwaway admin and a handful of logins per server; do not let production rate
        # limits (10 bootstraps/hour, 10 sign-ins/minute) turn a screenshot run into a stuck run.
        "WEIR_BOOTSTRAP_RATE_MAX_ATTEMPTS": "10000",
        "WEIR_AUTH_LOGIN_RATE_MAX_ATTEMPTS": "10000",
    }
    env.pop("WEIR_ENV", None)
    command = [
        "dotnet",
        "run",
        "--project",
        str(SERVER_PROJECT),
        "--no-build",
        "--",
        "--host",
        "127.0.0.1",
        "--port",
        str(api_port),
    ]
    server = runtime.start_server(
        "Weir screenshot server",
        command,
        cwd=REPO_ROOT,
        env=env,
        port=api_port,
        url=f"{api_internal}/health",
        log_path=logs / "api.log",
    )
    runtime.wait_until_up(server, timeout_s=90.0)
    if not db_path_for_home(home).is_file():
        runtime.stop_server(server)
        fail(f"The server did not create its database at {db_path_for_home(home)}.")
    return server, home, api_internal


def set_wizard_state(home: str, state: str) -> None:
    conn = sqlite3.connect(str(db_path_for_home(home)), timeout=30)
    try:
        with conn:
            conn.execute("UPDATE suite_settings SET setup_wizard_state = ?", (state,))
    finally:
        conn.close()
