"""Stable ``event_type`` strings for persisted activity (append-only contract)."""

# Auth / platform
AUTH_LOGIN_SUCCEEDED = "auth.login_succeeded"
AUTH_LOGIN_FAILED = "auth.login_failed"
AUTH_LOGOUT = "auth.logout"
AUTH_BOOTSTRAP_SUCCEEDED = "auth.bootstrap_succeeded"
AUTH_BOOTSTRAP_DENIED = "auth.bootstrap_denied"

# Fetcher bridge (throttled in service — not per-request spam)
FETCHER_PROBE_SUCCEEDED = "fetcher.probe_succeeded"
FETCHER_PROBE_FAILED = "fetcher.probe_failed"
