"""A fake metadata gateway: Deluno's metadata service played by a local HTTP server.

Weir asks it for a title's poster (``GET /metadata/search``) and then for the image
(``GET /artwork/w342/{file}``). The fake records every request, answers only titles a test taught it, and
can answer "busy" the way the real service does when it is over its limit. No contract test reaches the
real service: a server under test is pointed here with ``WEIR_ARTWORK_GATEWAY_URL``, and every other
server runs with it ``off``.

    gateway.knows("nosferatu", "nosferatu.jpg", tmdb_id=653)
    server_factory(env={"WEIR_ARTWORK_GATEWAY_URL": gateway.base_url})
    gateway.searches()
"""

from __future__ import annotations

import json
import threading
import time
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

from tests.contract.support.fake_manager import RecordedRequest

#: The bytes the fake serves for every poster image: a JPEG header and a few more bytes.
IMAGE_BYTES = bytes([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5])


@dataclass(frozen=True)
class _Title:
    poster_file: str
    tmdb_id: int


class FakeGateway:
    """A recording gateway. Start it with ``with`` or :meth:`start`."""

    def __init__(self) -> None:
        self.requests: list[RecordedRequest] = []
        self._titles: dict[str, _Title] = {}
        self._by_id: dict[int, _Title] = {}
        self._images: set[str] = set()
        self._busy_retry_after: int | None = None
        self._busy = False
        self._lock = threading.Lock()
        self._server: ThreadingHTTPServer | None = None

    # --- scripting ------------------------------------------------------------------------------

    def knows(self, title: str, poster_file: str, *, tmdb_id: int = 1) -> None:
        """Answer a search for ``title`` (any letter case) with one result whose poster is ``poster_file``."""

        known = _Title(poster_file, tmdb_id)
        with self._lock:
            self._titles[title.lower()] = known
            self._by_id[tmdb_id] = known
            self._images.add(poster_file)

    def serves_image(self, poster_file: str) -> None:
        """Serve ``poster_file`` at the artwork route, as the real gateway serves any TMDb image."""

        with self._lock:
            self._images.add(poster_file)

    def busy(self, retry_after_s: int | None = 600) -> None:
        """Answer every search with 503 ``provider_busy`` and this ``Retry-After``."""

        with self._lock:
            self._busy = True
            self._busy_retry_after = retry_after_s

    def calm(self) -> None:
        with self._lock:
            self._busy = False

    # --- what Weir asked ------------------------------------------------------------------------

    def searches(self) -> list[RecordedRequest]:
        with self._lock:
            return [r for r in self.requests if r.path == "/metadata/search"]

    def searches_for(self, title: str) -> list[RecordedRequest]:
        return [r for r in self.searches() if r.query.get("query", [""])[0].lower() == title.lower()]

    def image_requests(self) -> list[RecordedRequest]:
        with self._lock:
            return [r for r in self.requests if r.path.startswith("/artwork/")]

    # --- server ---------------------------------------------------------------------------------

    @property
    def base_url(self) -> str:
        if self._server is None:
            raise RuntimeError("The fake gateway is not running.")
        return f"http://127.0.0.1:{self._server.server_address[1]}"

    def _answer_search(self, request: RecordedRequest) -> tuple[int, dict[str, str], bytes]:
        with self._lock:
            if self._busy:
                headers = {"Retry-After": str(self._busy_retry_after)} if self._busy_retry_after is not None else {}
                return 503, headers, json.dumps({"error": "provider_busy"}).encode()
            by_id = request.query.get("providerId", [""])[0]
            found = (
                self._by_id.get(int(by_id)) if by_id.isdigit() else self._titles.get(request.query["query"][0].lower())
            )
        if found is None:
            return 200, {}, json.dumps({"provider": "deluno-broker", "resultCount": 0, "results": []}).encode()
        result = {
            "provider": "tmdb",
            "providerId": str(found.tmdb_id),
            "title": request.query["query"][0],
            "posterUrl": f"{self.base_url}/artwork/w780/{found.poster_file}",
        }
        return 200, {}, json.dumps({"provider": "deluno-broker", "resultCount": 1, "results": [result]}).encode()

    def start(self) -> FakeGateway:
        gateway = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *_args: object) -> None:
                return

            def do_GET(self) -> None:  # noqa: N802
                split = urlsplit(self.path)
                request = RecordedRequest(
                    method="GET",
                    path=split.path,
                    query=parse_qs(split.query),
                    headers=dict(self.headers.items()),
                    body=b"",
                    at=time.time(),
                )
                with gateway._lock:
                    gateway.requests.append(request)
                headers: dict[str, str] = {"Content-Type": "application/json"}
                if split.path == "/metadata/search":
                    status, extra, payload = gateway._answer_search(request)
                    headers.update(extra)
                elif split.path.startswith("/artwork/w342/") and split.path.rsplit("/", 1)[-1] in gateway._images:
                    status, payload = 200, IMAGE_BYTES
                    headers["Content-Type"] = "image/jpeg"
                else:
                    status, payload = 404, json.dumps({"error": "not_found"}).encode()
                self.send_response(status)
                for key, value in headers.items():
                    self.send_header(key, value)
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)

        self._server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self._server.daemon_threads = True
        threading.Thread(target=self._server.serve_forever, name="fake-gateway", daemon=True).start()
        return self

    def stop(self) -> None:
        if self._server is not None:
            self._server.shutdown()
            self._server.server_close()
        self._server = None

    def __enter__(self) -> FakeGateway:
        return self.start()

    def __exit__(self, *_exc: object) -> None:
        self.stop()
