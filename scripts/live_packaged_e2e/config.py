"""Environment-derived configuration for the packaged live audit, and the setup-code lookup.

Kept separate from the audit mixins so every part of the audit reads the same values.
"""

from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path

BASE_URL = os.environ.get("WEIR_LIVE_BASE_URL", "").strip().rstrip("/")
AUDIT_USER = os.environ.get("WEIR_LIVE_E2E_USER", "live-audit-admin").strip()
AUDIT_PASSWORD = os.environ.get(
    "WEIR_LIVE_E2E_PASSWORD", "live-audit-pass-20260831"
)
ARTIFACT_DIR = Path(
    os.environ.get("WEIR_LIVE_E2E_ARTIFACTS", "artifacts/live-packaged-e2e")
)
FIXTURE_HOST_ROOT_RAW = os.environ.get(
    "WEIR_LIVE_E2E_FIXTURE_HOST_ROOT", ""
).strip()
FIXTURE_SERVER_ROOT = os.environ.get(
    "WEIR_LIVE_E2E_FIXTURE_SERVER_ROOT", ""
).strip()
FIXTURE_FFMPEG = os.environ.get("WEIR_LIVE_E2E_FFMPEG", "ffmpeg").strip()
# The candidate's peer, seen from inside a Docker container, is the bridge gateway rather than
# loopback, so bootstrap needs the one-time setup code the same way a remote operator would get it.
# Unset for a target the audit reaches over real loopback (the developer fixture, a Windows smoke).
DOCKER_CONTAINER = os.environ.get("WEIR_LIVE_E2E_DOCKER_CONTAINER", "").strip()
DOCKER_WEIR_HOME = os.environ.get("WEIR_LIVE_E2E_DOCKER_HOME", "/data/weir").strip()
TIMEOUT_MS = 30_000


def project_version() -> str:
    """Resolve the expected packaged version without hard-coding a release."""

    explicit = os.environ.get("WEIR_LIVE_EXPECTED_VERSION", "").strip()
    if explicit:
        return explicit
    props = Path(__file__).resolve().parents[2] / "apps/server/Directory.Build.props"
    match = re.search(r"<WeirVersion>([^<]+)</WeirVersion>", props.read_text(encoding="utf-8"))
    if match is None:
        raise RuntimeError(f"WeirVersion was not found in {props}")
    return match.group(1).strip()


EXPECTED_VERSION = project_version()

SETUP_CODE_LOG_PATTERN = re.compile(r"enter this setup code:\s*([A-Z0-9]{4}-[A-Z0-9]{4})")


def read_docker_setup_code(container: str) -> str:
    """Reads the one-time setup code the way a remote operator is told to: from the container's
    own log line, or the setup-code file in its data folder if the log has since rotated past it."""

    logs = subprocess.run(
        ["docker", "logs", container],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    match = SETUP_CODE_LOG_PATTERN.search(logs.stdout + logs.stderr)
    if match:
        return match.group(1)

    cat = subprocess.run(
        ["docker", "exec", container, "cat", f"{DOCKER_WEIR_HOME}/setup-code"],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    code = cat.stdout.strip()
    if not code:
        raise RuntimeError(
            f"Found no setup code in {container}'s log or {DOCKER_WEIR_HOME}/setup-code."
        )
    return code
