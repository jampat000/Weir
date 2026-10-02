"""Reading the Activity stream (server-sent events) from a contract test."""

from __future__ import annotations

import json
from collections.abc import Iterator
from contextlib import contextmanager
from typing import Any

import httpx

from tests.contract.support.client import API, WeirClient

# Short enough that a stream that never sends fails the test quickly instead of hanging it.
STREAM_TIMEOUT = httpx.Timeout(10.0, read=10.0)


class SseReader:
    """Reads one SSE stream block by block (blocks end with a blank line)."""

    def __init__(self, response: httpx.Response) -> None:
        self.response = response
        self._lines = response.iter_lines()

    def next_block(self) -> list[str]:
        block: list[str] = []
        for line in self._lines:
            if line == "":
                if block:
                    return block
                continue
            block.append(line)
        raise AssertionError(f"The stream ended; partial block {block!r}")

    def next_event(self) -> tuple[str, dict[str, Any]]:
        """The next named event, skipping ``retry:`` and ``: keepalive`` blocks."""

        while True:
            block = self.next_block()
            event = next((line[len("event:") :].strip() for line in block if line.startswith("event:")), None)
            if event is None:
                continue
            data = "\n".join(line[len("data:") :].strip() for line in block if line.startswith("data:"))
            return event, json.loads(data)

    def next_event_named(self, name: str) -> dict[str, Any]:
        """The data of the next ``name`` event, skipping every other one."""

        while True:
            event, data = self.next_event()
            if event == name:
                return data


@contextmanager
def open_stream(server, client: WeirClient) -> Iterator[SseReader]:
    with (
        httpx.Client(base_url=server.base_url, cookies=client.cookies, timeout=STREAM_TIMEOUT) as http,
        http.stream("GET", f"{API}/activity/stream") as response,
    ):
        yield SseReader(response)
