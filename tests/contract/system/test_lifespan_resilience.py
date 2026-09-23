"""A failing non-essential startup step must not stop the server starting."""

from __future__ import annotations


def test_non_essential_startup_failure_does_not_abort_startup(server_factory, client_factory) -> None:
    """A log file the startup clean-up cannot read (not UTF-8) must not stop the server starting.

    The startup log-retention step fails on its own here, because the active log it rewrites holds
    bytes that are not text.
    """

    sut = server_factory(start=False)
    logs = sut.home / "logs"
    logs.mkdir(parents=True, exist_ok=True)
    (logs / "weir.log").write_bytes(b'{"timestamp": "\xff\xfe\xfa", "message": "\x80\x81"}\n\xc3\x28\xa0\xa1\n')

    sut.start()

    c = client_factory(sut)
    response = c.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"
    ready = c.get("/ready")
    assert ready.status_code == 200, ready.text
    assert ready.json()["ready"] is True
