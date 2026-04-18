"""Unit tests for Podnapisi client (network mocked)."""

from __future__ import annotations

from unittest.mock import MagicMock, patch

from mediamop.modules.subber import subber_podnapisi_client as pnc


@patch("mediamop.modules.subber.subber_podnapisi_client.urllib.request.urlopen")
def test_podnapisi_search_parses_data_list(mock_urlopen: MagicMock) -> None:
    import json

    body = {"data": [{"id": "42", "language": "en", "flags": []}]}
    mock_resp = MagicMock()
    mock_resp.read.return_value = json.dumps(body).encode()
    mock_resp.status = 200
    mock_urlopen.return_value.__enter__.return_value = mock_resp
    items = pnc.search(
        query="Test Show",
        season_number=1,
        episode_number=2,
        languages=["en"],
        media_scope="tv",
    )
    assert len(items) == 1
    assert items[0]["id"] == "42"
