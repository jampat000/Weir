"""Handler for ``subber.subtitle_upgrade.v1`` — periodic re-search for better subtitles."""

from __future__ import annotations

import json
from collections.abc import Callable

from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.config import MediaMopSettings
from mediamop.modules.subber import subber_activity
from mediamop.modules.subber.subber_settings_service import ensure_subber_settings_row
from mediamop.modules.subber.subber_opensubtitles_client import SubberRateLimitError
from mediamop.modules.subber.subber_subtitle_search_service import search_and_download_subtitle, subber_any_search_configured
from mediamop.modules.subber.subber_subtitle_state_service import get_candidates_for_upgrade, mark_for_upgrade
from mediamop.modules.subber.worker_loop import SubberJobWorkContext
from mediamop.platform.activity import constants as C


def make_subber_subtitle_upgrade_handler(
    settings: MediaMopSettings,
    session_factory: sessionmaker[Session],
) -> Callable[[SubberJobWorkContext], None]:
    def handle(ctx: SubberJobWorkContext) -> None:
        _ = json.loads(ctx.payload_json or "{}")
        upgraded = 0
        attempted = 0
        with session_factory() as session:
            with session.begin():
                settings_row = ensure_subber_settings_row(session)
                if not settings_row.enabled or not bool(settings_row.upgrade_enabled):
                    subber_activity.record_subber_activity(
                        session,
                        event_type=C.SUBBER_SUBTITLE_UPGRADE_COMPLETED,
                        title="Subtitle upgrade skipped (disabled)",
                        detail={"attempted": 0, "upgraded": 0},
                    )
                    return
                if not subber_any_search_configured(settings, settings_row, session):
                    subber_activity.record_subber_activity(
                        session,
                        event_type=C.SUBBER_SUBTITLE_UPGRADE_COMPLETED,
                        title="Subtitle upgrade skipped (no providers)",
                        detail={"attempted": 0, "upgraded": 0},
                    )
                    return
                interval_sec = max(60, int(settings_row.upgrade_schedule_interval_seconds or 604800))
                since_days = max(1, int(interval_sec // 86400) or 7)
                for row in get_candidates_for_upgrade(session, since_days):
                    attempted += 1
                    try:
                        ok = search_and_download_subtitle(
                            settings=settings,
                            settings_row=settings_row,
                            state_row=row,
                            db=session,
                            providers=None,
                            retain_found_on_failure=True,
                        )
                    except SubberRateLimitError:
                        raise
                    mark_for_upgrade(session, int(row.id), increment_count=bool(ok))
                    if ok:
                        upgraded += 1
                subber_activity.record_subber_activity(
                    session,
                    event_type=C.SUBBER_SUBTITLE_UPGRADE_COMPLETED,
                    title="Subtitle upgrade finished",
                    detail={"attempted": attempted, "upgraded": upgraded},
                )

    return handle


def register_subtitle_upgrade_handler(
    settings: MediaMopSettings,
    session_factory: sessionmaker[Session],
) -> dict[str, Callable[[SubberJobWorkContext], None]]:
    from mediamop.modules.subber.subber_job_kinds import SUBBER_JOB_KIND_SUBTITLE_UPGRADE

    return {SUBBER_JOB_KIND_SUBTITLE_UPGRADE: make_subber_subtitle_upgrade_handler(settings, session_factory)}
