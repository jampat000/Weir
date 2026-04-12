"""Unit tests for watched-folder scan filesystem helpers and duplicate guards."""

from __future__ import annotations

import json

from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.db import Base
from mediamop.modules.refiner.jobs_model import RefinerJob, RefinerJobStatus
from mediamop.modules.refiner.refiner_file_remux_pass_job_kinds import REFINER_FILE_REMUX_PASS_JOB_KIND
from mediamop.modules.refiner.refiner_watched_folder_remux_scan_dispatch_ops import (
    iter_watched_folder_media_candidate_files,
    refiner_active_remux_pass_exists_for_relative_path,
    relative_posix_path_under_watched,
)

import mediamop.modules.refiner.jobs_model  # noqa: F401


def test_iter_watched_folder_media_candidates_sorted(tmp_path) -> None:
    w = tmp_path / "w"
    w.mkdir()
    (w / "b.mkv").write_bytes(b"x")
    (w / "a.mkv").write_bytes(b"x")
    (w / "skip.txt").write_bytes(b"n")
    got = iter_watched_folder_media_candidate_files(w)
    assert [p.name for p in got] == ["a.mkv", "b.mkv"]


def test_relative_posix_under_watched(tmp_path) -> None:
    w = tmp_path / "root"
    w.mkdir()
    sub = w / "sub"
    sub.mkdir()
    f = sub / "f.mkv"
    f.write_bytes(b"1")
    assert relative_posix_path_under_watched(watched_root=w, file_path=f) == "sub/f.mkv"


def test_active_remux_detects_pending_payload_relative(tmp_path) -> None:
    url = f"sqlite:///{tmp_path / 't.sqlite'}"
    engine = create_engine(url, connect_args={"check_same_thread": False}, future=True)
    Base.metadata.create_all(engine)
    fac = sessionmaker(bind=engine, class_=Session, autoflush=False, autocommit=False, future=True)
    with fac() as s:
        s.add(
            RefinerJob(
                dedupe_key="x",
                job_kind=REFINER_FILE_REMUX_PASS_JOB_KIND,
                payload_json=json.dumps({"relative_media_path": "movies/a.mkv", "dry_run": True}),
                status=RefinerJobStatus.PENDING.value,
                max_attempts=3,
            ),
        )
        s.commit()
    with fac() as s:
        assert refiner_active_remux_pass_exists_for_relative_path(s, relative_posix="movies/a.mkv") is True
        assert refiner_active_remux_pass_exists_for_relative_path(s, relative_posix="other.mkv") is False
