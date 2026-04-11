"""Periodic asyncio enqueue for Arr search ``fetcher_jobs`` (four independent timers)."""

from __future__ import annotations

import asyncio
import logging

from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.config import MediaMopSettings
from mediamop.modules.fetcher.fetcher_arr_search_enqueue import arr_search_schedule_specs

logger = logging.getLogger(__name__)

FETCHER_ARR_SEARCH_ENQUEUE_FAILURE_COOLDOWN_SECONDS = 2.0


def start_fetcher_arr_search_enqueue_tasks(
    session_factory: sessionmaker[Session],
    *,
    stop_event: asyncio.Event,
    settings: MediaMopSettings,
) -> list[asyncio.Task[None]]:
    specs = arr_search_schedule_specs(settings)
    tasks: list[asyncio.Task[None]] = []
    for label, interval_seconds, enqueue_fn in specs:
        tasks.append(
            asyncio.create_task(
                _run_periodic_arr_search_enqueue(
                    session_factory,
                    stop_event=stop_event,
                    interval_seconds=interval_seconds,
                    log_label=label,
                    enqueue_fn=enqueue_fn,
                ),
                name=f"fetcher-arr-search-{label}",
            ),
        )
    return tasks


async def _run_periodic_arr_search_enqueue(
    session_factory: sessionmaker[Session],
    *,
    stop_event: asyncio.Event,
    interval_seconds: float,
    log_label: str,
    enqueue_fn,
) -> None:
    loop = asyncio.get_running_loop()
    while not stop_event.is_set():

        def _once() -> None:
            with session_factory() as session:
                enqueue_fn(session)
                session.commit()

        try:
            await asyncio.to_thread(_once)
        except asyncio.CancelledError:
            raise
        except Exception:
            logger.exception("Fetcher periodic Arr search enqueue failed label=%s", log_label)
            if stop_event.is_set():
                break
            fail_deadline = loop.time() + FETCHER_ARR_SEARCH_ENQUEUE_FAILURE_COOLDOWN_SECONDS
            while loop.time() < fail_deadline and not stop_event.is_set():
                remaining = fail_deadline - loop.time()
                if remaining <= 0:
                    break
                await asyncio.sleep(min(0.25, remaining))
            continue

        if stop_event.is_set():
            break
        deadline = loop.time() + interval_seconds
        while loop.time() < deadline and not stop_event.is_set():
            remaining = deadline - loop.time()
            if remaining <= 0:
                break
            await asyncio.sleep(min(0.25, remaining))


async def stop_fetcher_arr_search_enqueue_tasks(tasks: list[asyncio.Task[None]]) -> None:
    for t in tasks:
        if not t.done():
            t.cancel()
    if tasks:
        await asyncio.gather(*tasks, return_exceptions=True)
