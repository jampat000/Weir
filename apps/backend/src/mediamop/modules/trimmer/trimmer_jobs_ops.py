"""Atomic claim / lease / complete / fail for :class:`~mediamop.modules.trimmer.trimmer_jobs_model.TrimmerJob`."""

from __future__ import annotations

from datetime import datetime, timezone

from sqlalchemy import select, text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from mediamop.modules.queue_worker.job_kind_boundaries import validate_trimmer_enqueue_job_kind
from mediamop.modules.trimmer.trimmer_jobs_model import TrimmerJob, TrimmerJobStatus


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


_CLAIM_NEXT_SQL = """
UPDATE trimmer_jobs
SET
  status = :leased,
  lease_owner = :owner,
  lease_expires_at = :lease_exp,
  updated_at = CURRENT_TIMESTAMP,
  attempt_count = attempt_count + 1
WHERE id = (
  SELECT id FROM trimmer_jobs
  WHERE status = :pending
     OR (
       status = :leased
       AND (lease_expires_at IS NULL OR lease_expires_at < :now)
     )
  ORDER BY id ASC
  LIMIT 1
)
RETURNING id
"""


def trimmer_enqueue_or_get_job(
    session: Session,
    *,
    dedupe_key: str,
    job_kind: str,
    payload_json: str | None = None,
    max_attempts: int = 3,
) -> TrimmerJob:
    """Insert a ``pending`` job or return the existing row for ``dedupe_key``."""

    validate_trimmer_enqueue_job_kind(job_kind)

    existing = session.scalar(select(TrimmerJob).where(TrimmerJob.dedupe_key == dedupe_key))
    if existing is not None:
        return existing

    row = TrimmerJob(
        dedupe_key=dedupe_key,
        job_kind=job_kind,
        payload_json=payload_json,
        status=TrimmerJobStatus.PENDING.value,
        max_attempts=max(1, max_attempts),
    )
    with session.begin_nested():
        session.add(row)
        try:
            session.flush()
        except IntegrityError:
            pass
        else:
            return row

    found = session.scalar(select(TrimmerJob).where(TrimmerJob.dedupe_key == dedupe_key))
    if found is None:
        msg = "trimmer job dedupe race: row missing after IntegrityError"
        raise RuntimeError(msg)
    return found


def claim_next_eligible_trimmer_job(
    session: Session,
    *,
    lease_owner: str,
    lease_expires_at: datetime,
    now: datetime | None = None,
) -> TrimmerJob | None:
    """Atomically lease the next ``pending`` or expired ``leased`` row."""

    when = now if now is not None else _utc_now()
    result = session.execute(
        text(_CLAIM_NEXT_SQL),
        {
            "leased": TrimmerJobStatus.LEASED.value,
            "pending": TrimmerJobStatus.PENDING.value,
            "owner": lease_owner,
            "lease_exp": lease_expires_at,
            "now": when,
        },
    )
    row = result.fetchone()
    if row is None:
        return None
    job_id = int(row[0])
    return session.scalars(select(TrimmerJob).where(TrimmerJob.id == job_id)).one()


def complete_claimed_trimmer_job(
    session: Session,
    *,
    job_id: int,
    lease_owner: str,
    now: datetime | None = None,
) -> bool:
    """Mark ``completed`` only when ``lease_owner`` matches and lease is still valid."""

    when = now if now is not None else _utc_now()
    job = session.scalars(select(TrimmerJob).where(TrimmerJob.id == job_id)).one_or_none()
    if job is None:
        return False
    if job.status != TrimmerJobStatus.LEASED.value:
        return False
    if job.lease_owner != lease_owner:
        return False
    if job.lease_expires_at is None or job.lease_expires_at < when:
        return False

    job.status = TrimmerJobStatus.COMPLETED.value
    job.lease_owner = None
    job.lease_expires_at = None
    session.flush()
    return True


def fail_claimed_trimmer_job(
    session: Session,
    *,
    job_id: int,
    lease_owner: str,
    error_message: str,
    now: datetime | None = None,
) -> bool:
    """After a failed processing attempt: requeue as ``pending`` or mark ``failed`` if attempts exhausted."""

    when = now if now is not None else _utc_now()
    job = session.scalars(select(TrimmerJob).where(TrimmerJob.id == job_id)).one_or_none()
    if job is None:
        return False
    if job.status != TrimmerJobStatus.LEASED.value:
        return False
    if job.lease_owner != lease_owner:
        return False
    if job.lease_expires_at is None or job.lease_expires_at < when:
        return False

    job.last_error = error_message
    job.lease_owner = None
    job.lease_expires_at = None
    if job.attempt_count >= job.max_attempts:
        job.status = TrimmerJobStatus.FAILED.value
    else:
        job.status = TrimmerJobStatus.PENDING.value
    session.flush()
    return True


def fail_leased_trimmer_job_after_complete_failure(
    session: Session,
    *,
    job_id: int,
    lease_owner: str,
    error_message: str,
    now: datetime | None = None,
) -> bool:
    """Terminal handler_ok_finalize_failed when finalize after a successful handler did not apply."""

    when = now if now is not None else _utc_now()
    job = session.scalars(select(TrimmerJob).where(TrimmerJob.id == job_id)).one_or_none()
    if job is None:
        return False
    if job.status != TrimmerJobStatus.LEASED.value:
        return False
    if job.lease_owner != lease_owner:
        return False
    if job.lease_expires_at is None or job.lease_expires_at < when:
        return False

    job.status = TrimmerJobStatus.HANDLER_OK_FINALIZE_FAILED.value
    job.lease_owner = None
    job.lease_expires_at = None
    job.last_error = error_message[:10_000]
    session.flush()
    return True
