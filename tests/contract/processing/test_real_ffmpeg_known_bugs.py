"""Real-ffmpeg checks for #539 items 1 and 3, and #494: behaviour that only shows up against real ffmpeg.

The fake ffmpeg tool hands a classification straight to Weir (``probe_error=...``), so it never
exercises how Weir reads ffprobe's own stderr. Against real ffmpeg:

- On a zero-filled ``.mkv``, ffprobe exits 1, and the classification markers Weir looks for
  (``EBML header parsing failed``, etc.) reach stderr only with ``-v error``, not with ``-v quiet``.
  Weir runs ffprobe with ``-v error`` (``FfmpegCommands.BuildFfprobeArgv``), so the file is classified
  unreadable and, under the reject policy, the hand-off is reported ``failed`` with
  ``disposition: rejected`` (#539 item 1, #494).
- On an MKV truncated after encoding, ``ffmpeg ... -f null -`` prints ``File ended prematurely`` but
  still exits 0, and the container header still names the original duration, so an exit-code check
  alone would accept the file as whole (#539 item 3).
"""

from __future__ import annotations

import subprocess
from pathlib import Path

import pytest

from tests.contract.processing import _helpers as h
from tests.contract.support.client import API
from tests.contract.support.polling import never_within

pytestmark = pytest.mark.real_ffmpeg


def _tool(env: dict[str, str], name: str) -> str:
    folder = Path(env["WEIR_FFMPEG_DIR"])
    for candidate in (folder / f"{name}.exe", folder / name):
        if candidate.is_file():
            return str(candidate)
    raise AssertionError(f"{name} not found in {folder}")


def test_unreadable_zero_filled_media_is_classified_unreadable_and_rejected(
    server_factory, client_factory, fake_managers, real_ffmpeg_env, tmp_path: Path
) -> None:
    folders = h.Folders.make(tmp_path)
    release = folders.watched / "Zero.Filled.494"
    release.mkdir()
    source = release / "film.mkv"
    # All zero bytes, like the 10GB file from #494 (`fsutil file createnew`) but small enough for a
    # contract test: real ffprobe reports the same "EBML header parsing failed" either way.
    source.write_bytes(b"\x00" * (2 * 1024 * 1024))

    server = server_factory(
        env={
            **real_ffmpeg_env,
            "WEIR_PROCESSING_WORKER_COUNT": "1",
            "WEIR_MEDIA_MANAGER_WEBHOOK_SECRET": h.WEBHOOK_SECRET,
        }
    )
    admin = client_factory(server)
    admin.ensure_admin()
    fake, library = h.deluno_setup(
        admin, fake_managers, folders, capabilities=["processor-reject-regrab"], failure_policy="reject"
    )

    h.post_handoff(admin, handoff_id="handoff-zero-494", source_path=source)
    h.wait_for_handoff_state(admin, "handoff-zero-494", "rejected", timeout_s=90)

    reports = h.callbacks(fake, "handoff-zero-494")
    assert reports, "the rejection must be reported to the manager"
    report = reports[-1]
    assert report["status"] == "failed"
    assert report["disposition"] == "rejected"
    assert not report.get("outputPath"), (
        "an unreadable file must never be reported completed with a copy of itself as output (#494)"
    )

    # GET /processing/files must not 500 after this (#494 item 3 / #530), and must show the rejection.
    files_response = admin.get(f"{API}/processing/files")
    assert files_response.status_code == 200, files_response.text
    row = h.file_row(admin, library["id"], "Zero.Filled.494/film.mkv")
    assert row is not None, "the rejection must leave a Files row behind"
    assert row["status"] == "rejected"


def test_a_truncated_mkv_never_completes_as_if_it_were_whole(
    server_factory, client_factory, fake_managers, real_ffmpeg_env, tmp_path: Path
) -> None:
    ffmpeg = _tool(real_ffmpeg_env, "ffmpeg")
    good = tmp_path / "good-source.mkv"
    subprocess.run(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-f",
            "lavfi",
            "-i",
            "testsrc=duration=3:size=64x64:rate=10",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440:duration=3",
            "-map",
            "0:v",
            "-map",
            "1:a",
            "-c:v",
            "mpeg4",
            "-c:a",
            "mp2",
            str(good),
        ],
        check=True,
        capture_output=True,
    )
    data = good.read_bytes()

    folders = h.Folders.make(tmp_path)
    release = folders.watched / "Truncated.539"
    release.mkdir()
    source = release / "truncated.mkv"
    # Cut well short of the end: the container header still names the full duration, so only an
    # end-to-end integrity read can catch this.
    source.write_bytes(data[: len(data) * 60 // 100])

    server = server_factory(
        env={
            **real_ffmpeg_env,
            "WEIR_PROCESSING_WORKER_COUNT": "1",
            "WEIR_MEDIA_MANAGER_WEBHOOK_SECRET": h.WEBHOOK_SECRET,
        }
    )
    admin = client_factory(server)
    admin.ensure_admin()
    _fake, _library = h.deluno_setup(admin, fake_managers, folders)

    h.post_handoff(admin, handoff_id="handoff-truncated-539", source_path=source)

    never_within(
        lambda: h.handoff_status(admin, "handoff-truncated-539")["state"] == "completed",
        seconds=20,
        what="a truncated file to be wrongly reported completed with a false, pre-truncation duration",
    )
