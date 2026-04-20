"""Handler for ``broker.indexer.test.v1`` — HTTP probe of indexer ``url``."""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from collections.abc import Callable
from datetime import datetime, timezone

from sqlalchemy.orm import Session, sessionmaker

from mediamop.modules.broker.broker_indexers_service import get_indexer_by_id
from mediamop.modules.broker.broker_job_kinds import BROKER_JOB_KIND_INDEXER_TEST_V1
from mediamop.modules.broker.broker_job_context import BrokerJobWorkContext


def register_indexer_test_handler(
    session_factory: sessionmaker[Session],
) -> dict[str, Callable[[BrokerJobWorkContext], None]]:
    def handle(ctx: BrokerJobWorkContext) -> None:
        payload = json.loads(ctx.payload_json or "{}")
        indexer_id = int(payload.get("indexer_id") or 0)
        if indexer_id <= 0:
            raise RuntimeError("indexer_id missing")
        with session_factory() as session:
            with session.begin():
                row = get_indexer_by_id(session, indexer_id)
                if row is None:
                    raise RuntimeError("indexer not found")
                url = (row.url or "").strip()
                if not url:
                    raise RuntimeError("indexer url empty")
                ok = False
                err: str | None = None
                try:
                    req = urllib.request.Request(url, method="GET")
                    with urllib.request.urlopen(req, timeout=15.0) as resp:
                        ok = 200 <= int(resp.status) < 500
                except urllib.error.HTTPError as e:
                    err = f"HTTP {e.code}"
                except Exception as e:
                    err = str(e)[:500]
                row.last_tested_at = datetime.now(timezone.utc)
                row.last_test_ok = 1 if ok else 0
                row.last_test_error = None if ok else (err or "probe failed")
                session.flush()
                if not ok:
                    raise RuntimeError(row.last_test_error or "indexer test failed")

    return {BROKER_JOB_KIND_INDEXER_TEST_V1: handle}
