"""In-process Trimmer worker handler for ``trimmer.trim_plan.constraints_check.v1``."""

from __future__ import annotations

import json
from collections.abc import Callable
from typing import Any

from sqlalchemy.orm import Session, sessionmaker

from mediamop.modules.trimmer.trimmer_trim_plan_constraints_check_activity import (
    record_trimmer_trim_plan_constraints_check_completed,
)
from mediamop.modules.trimmer.trimmer_trim_plan_constraints_evaluate import (
    evaluate_trim_plan_constraints,
)
from mediamop.modules.trimmer.worker_loop import TrimmerJobWorkContext


def make_trimmer_trim_plan_constraints_check_handler(
    session_factory: sessionmaker[Session],
) -> Callable[[TrimmerJobWorkContext], None]:
    """Validate supplied trim segments in JSON only (no files, no FFmpeg, no Fetcher/Refiner)."""

    def _run(ctx: TrimmerJobWorkContext) -> None:
        raw = (ctx.payload_json or "").strip()
        if not raw:
            detail_obj: dict[str, Any] = {"job_id": ctx.id, "ok": False, "reason": "missing payload_json"}
            detail = json.dumps(detail_obj, separators=(",", ":"))[:10_000]
            with session_factory() as session:
                with session.begin():
                    record_trimmer_trim_plan_constraints_check_completed(session, detail=detail)
            return

        try:
            data = json.loads(raw)
        except json.JSONDecodeError as exc:
            detail_obj = {"job_id": ctx.id, "ok": False, "reason": f"invalid json: {exc}"}
            detail = json.dumps(detail_obj, separators=(",", ":"))[:10_000]
            with session_factory() as session:
                with session.begin():
                    record_trimmer_trim_plan_constraints_check_completed(session, detail=detail)
            return

        if not isinstance(data, dict):
            detail_obj = {"job_id": ctx.id, "ok": False, "reason": "payload must be a JSON object"}
            detail = json.dumps(detail_obj, separators=(",", ":"))[:10_000]
            with session_factory() as session:
                with session.begin():
                    record_trimmer_trim_plan_constraints_check_completed(session, detail=detail)
            return

        ok, reason, ev_detail = evaluate_trim_plan_constraints(data)
        detail_obj: dict[str, Any] = {"job_id": ctx.id, "ok": ok}
        if reason:
            detail_obj["reason"] = reason
        detail_obj.update(ev_detail)
        detail = json.dumps(detail_obj, separators=(",", ":"))[:10_000]
        with session_factory() as session:
            with session.begin():
                record_trimmer_trim_plan_constraints_check_completed(session, detail=detail)

    return _run
