"""``AuditProcessingMixin``: the Processing pass-through lifecycle proof (a real ffmpeg fixture,
optional), and Settings/System URL history and the not-found route. Assumes ``AuditCore`` and
``AuditShellMixin`` in the same instance.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import time
import uuid
from pathlib import Path

from .config import ARTIFACT_DIR, BASE_URL, FIXTURE_FFMPEG, FIXTURE_HOST_ROOT_RAW, FIXTURE_SERVER_ROOT


class AuditProcessingMixin:
    def processing_pass_through_lifecycle(self) -> None:
        """Prove an unchanged file reaches output before its watched source is removed."""

        configured = bool(FIXTURE_HOST_ROOT_RAW or FIXTURE_SERVER_ROOT)
        if not configured:
            self.record(
                "Processing pass-through lifecycle skipped (no controlled fixture mount)"
            )
            return
        self.require(
            bool(FIXTURE_HOST_ROOT_RAW and FIXTURE_SERVER_ROOT),
            "both Processing fixture host and server roots are required",
        )
        self.require(bool(FIXTURE_FFMPEG), "Processing fixture FFmpeg command is required")

        host_root = Path(FIXTURE_HOST_ROOT_RAW).expanduser().resolve()
        host_root.mkdir(parents=True, exist_ok=True)
        fixture_name = f"pass-through-{uuid.uuid4().hex}"
        fixture_root = host_root / fixture_name
        watch = fixture_root / "watch"
        work = fixture_root / "work"
        output = fixture_root / "processed"
        release = watch / "ForeignFilm"
        for directory in (fixture_root, watch, work, output, release):
            directory.mkdir(parents=True, exist_ok=False)
            if os.name != "nt":
                directory.chmod(0o777)

        source = release / "foreign-only.mkv"
        subprocess.run(
            [
                FIXTURE_FFMPEG,
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-f",
                "lavfi",
                "-i",
                "color=c=black:s=320x180:d=2",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=440:duration=2",
                "-map",
                "0:v",
                "-map",
                "1:a",
                "-c:v",
                "mpeg4",
                "-c:a",
                "aac",
                "-metadata:s:a:0",
                "language=jpn",
                "-y",
                str(source),
            ],
            check=True,
            timeout=60,
        )
        self.require(source.is_file(), "FFmpeg did not create the pass-through fixture")
        old_time = time.time() - 600
        os.utime(source, (old_time, old_time))
        source_size = source.stat().st_size
        source_hash = hashlib.sha256(source.read_bytes()).hexdigest()

        server_separator = (
            "\\" if re.match(r"^[A-Za-z]:[\\/]", FIXTURE_SERVER_ROOT) else "/"
        )

        def server_path(*parts: str) -> str:
            root = FIXTURE_SERVER_ROOT.rstrip("/\\")
            return root + server_separator + server_separator.join(parts)

        # The seeded Movies library is configured directly; the path-settings route was retired in #460.
        libraries = self.browser_api("GET", "/api/v1/processing/libraries")
        movies = next(
            (row for row in (libraries.get("payload") or []) if row.get("media_type") == "movie"),
            None,
        )
        self.require(movies is not None, "the install has no Movies library to configure")
        path_result = self.browser_api(
            "PUT",
            f"/api/v1/processing/libraries/{movies['id']}",
            {
                "csrf_token": self.csrf_token(),
                "name": movies["name"],
                "media_type": "movie",
                "watched_folder": server_path(fixture_name, "watch"),
                "work_folder": server_path(fixture_name, "work"),
                "output_folder": server_path(fixture_name, "processed"),
                "manager_connection_ids": movies.get("manager_connection_ids") or [],
            },
        )
        self.require(
            path_result["status"] == 200,
            f"could not configure pass-through fixture paths: {path_result['payload']}",
        )
        enqueue = self.browser_api(
            "POST",
            "/api/v1/processing/jobs/file-remux-pass/enqueue",
            {
                "csrf_token": self.csrf_token(),
                "relative_media_path": "ForeignFilm/foreign-only.mkv",
                "media_scope": "movie",
                "pass_through_unchanged": True,
            },
        )
        self.require(
            enqueue["status"] == 200,
            f"could not enqueue pass-through fixture: {enqueue['payload']}",
        )
        job_id = int((enqueue.get("payload") or {}).get("job_id") or 0)
        self.require(job_id > 0, "pass-through enqueue did not return a job id")

        delivered = output / "ForeignFilm" / "foreign-only.mkv"
        terminal_status = ""
        last_error = ""
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            inspection = self.browser_api(
                "GET", "/api/v1/processing/jobs/inspection?limit=100"
            )
            self.require(
                inspection["status"] == 200,
                "could not inspect the pass-through job",
            )
            rows = (inspection.get("payload") or {}).get("jobs") or []
            row = next((item for item in rows if item.get("id") == job_id), None)
            if row:
                terminal_status = str(row.get("status") or "")
                last_error = str(row.get("last_error") or "")
                if terminal_status in {"failed", "cancelled"}:
                    break
            if (
                terminal_status == "completed"
                and delivered.is_file()
                and not source.exists()
            ):
                break
            time.sleep(0.25)

        self.require(
            delivered.is_file(),
            f"pass-through output was not created (status={terminal_status}, error={last_error})",
        )
        self.require(not source.exists(), "pass-through source was not cleaned up")
        self.require(
            terminal_status == "completed",
            f"pass-through job did not complete (status={terminal_status}, error={last_error})",
        )
        output_size = delivered.stat().st_size
        output_hash = hashlib.sha256(delivered.read_bytes()).hexdigest()
        self.require(output_size == source_size, "pass-through output size changed")
        self.require(output_hash == source_hash, "pass-through output bytes changed")

        proof = {
            "job_id": job_id,
            "job_status": terminal_status,
            "source_removed": True,
            "output_created": True,
            "source_bytes": source_size,
            "output_bytes": output_size,
            "source_sha256": source_hash,
            "output_sha256": output_hash,
            "result": "passed",
        }
        ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
        (ARTIFACT_DIR / "pass-through-proof.json").write_text(
            json.dumps(proof, indent=2), encoding="utf-8"
        )
        self.record(
            "Processing pass-through placed byte-identical output before cleaning the watched source"
        )

    def settings_history_and_navigation(self) -> None:
        for sidebar, first, first_id, second, second_param, second_id in (
            (
                "Settings",
                "Libraries",
                "processing-libraries-section",
                "Schedule",
                "tab=schedule",
                "processing-schedules-section",
            ),
            (
                "System",
                "About",
                "suite-settings-global",
                "Security",
                "tab=security",
                "suite-settings-security",
            ),
        ):
            self.open_tab(sidebar, first)
            self.visible(
                self.page.get_by_test_id(first_id), f"{sidebar} history origin {first}"
            )
            self.click(
                self.page.get_by_role("tab", name=second, exact=True),
                f"exercise {sidebar} URL history forward target",
            )
            self.require(
                second_param in self.page.url,
                f"{sidebar} {second} tab is not represented in the URL",
            )
            self.page.go_back()
            self.visible(
                self.page.get_by_test_id(first_id),
                f"{sidebar} browser-back returns {first}",
            )
            self.page.go_forward()
            self.visible(
                self.page.get_by_test_id(second_id),
                f"{sidebar} browser-forward returns {second}",
            )
        self.page.goto(BASE_URL + "/not-a-real-screen", wait_until="domcontentloaded")
        self.visible(
            self.page.get_by_text("This page doesn't exist.", exact=False),
            "not-found route",
        )
        self.record("Settings and System URL history and not-found route")
