"""Activity rows for Fetcher Arr search jobs (scheduled zero-missing stays silent)."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.platform.activity import constants as C
from mediamop.platform.activity.service import record_activity_event


def _missing_zero_manual_detail(*, app: str, reason: str) -> str:
    label = "episodes" if app == "sonarr" else "movies"
    if reason == "retry_delay":
        return (
            "All eligible items are still waiting for retry delay to expire.\n"
            "MediaMop will try again automatically."
        )
    return (
        f"No {label} are eligible for a missing search right now.\n"
        "MediaMop will try again automatically."
    )


def _upgrade_zero_manual_detail(*, app: str, reason: str) -> str:
    if reason == "retry_delay":
        return (
            "All eligible items are still waiting for retry delay to expire.\n"
            "MediaMop will try again automatically."
        )
    unit = "episodes" if app == "sonarr" else "movies"
    return (
        f"No {unit} are eligible for an upgrade search right now.\n"
        "MediaMop will try again automatically."
    )


def record_missing_search_dispatched(
    db: Session,
    *,
    app: str,
    count: int,
    detail_lines: list[str],
) -> None:
    media = "TV" if app == "sonarr" else "Movies"
    unit = "episode" if app == "sonarr" else "movie"
    title = f"{media} · Missing search · {count} {unit}{'s' if count != 1 else ''} searched"
    detail = "\n".join(detail_lines) if detail_lines else None
    record_activity_event(
        db,
        event_type=C.FETCHER_ARR_SEARCH_MISSING_DISPATCHED,
        module="fetcher",
        title=title,
        detail=detail,
    )


def record_missing_search_zero_manual(
    db: Session,
    *,
    app: str,
    reason: str,
) -> None:
    media = "TV" if app == "sonarr" else "Movies"
    title = f"{media} · Missing search · No search started"
    record_activity_event(
        db,
        event_type=C.FETCHER_ARR_SEARCH_MISSING_ZERO_MANUAL,
        module="fetcher",
        title=title,
        detail=_missing_zero_manual_detail(app=app, reason=reason),
    )


def record_upgrade_search_dispatched(
    db: Session,
    *,
    app: str,
    count: int,
    detail_lines: list[str],
) -> None:
    media = "TV" if app == "sonarr" else "Movies"
    unit = "episode" if app == "sonarr" else "movie"
    title = f"{media} · Upgrade search · {count} {unit}{'s' if count != 1 else ''} searched"
    detail = "\n".join(detail_lines) if detail_lines else None
    record_activity_event(
        db,
        event_type=C.FETCHER_ARR_SEARCH_UPGRADE_DISPATCHED,
        module="fetcher",
        title=title,
        detail=detail,
    )


def record_upgrade_search_zero_manual(
    db: Session,
    *,
    app: str,
    reason: str,
) -> None:
    media = "TV" if app == "sonarr" else "Movies"
    title = f"{media} · Upgrade search · No search started"
    record_activity_event(
        db,
        event_type=C.FETCHER_ARR_SEARCH_UPGRADE_ZERO_MANUAL,
        module="fetcher",
        title=title,
        detail=_upgrade_zero_manual_detail(app=app, reason=reason),
    )
