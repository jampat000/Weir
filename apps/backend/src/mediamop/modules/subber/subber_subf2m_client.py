"""Subf2m subtitle client — web scraper (fragile; may break if site changes).

NOTE: Subf2m does not have a public API. This client scrapes HTML.
It may stop working without notice if Subf2m changes their markup.
"""

from __future__ import annotations

import logging
import re
import urllib.parse
import urllib.request
from typing import Any

BASE = "https://subf2m.co"
USER_AGENT = "Mozilla/5.0 (compatible; MediaMop/1.0)"
logger = logging.getLogger(__name__)


def _get_html(url: str) -> str:
    req = urllib.request.Request(  # noqa: S310
        url,
        headers={"User-Agent": USER_AGENT, "Accept": "text/html"},
        method="GET",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return resp.read().decode("utf-8", errors="replace")
    except Exception:
        logger.exception("Subf2m request failed: %s", url)
        return ""


def search(
    *,
    query: str,
    season_number: int | None,
    episode_number: int | None,
    languages: list[str],
    media_scope: str,
) -> list[dict[str, Any]]:
    """Search Subf2m. Returns list of dicts with download_page_url."""
    encoded = urllib.parse.quote(query.strip().replace(" ", "-"))
    url = f"{BASE}/subtitles/searchbytitle?query={encoded}&l="
    html = _get_html(url)
    if not html:
        return []
    # Extract first result link
    matches = re.findall(r'href="(/subtitles/[^"]+)"', html)
    if not matches:
        return []
    sub_page = BASE + matches[0]
    sub_html = _get_html(sub_page)
    if not sub_html:
        return []
    # Find download links
    dl_matches = re.findall(r'href="(/subtitle/[^"]+\.zip[^"]*)"', sub_html)
    if not dl_matches:
        dl_matches = re.findall(r'href="(/subtitle/[^"]+)"', sub_html)
    lang = (languages[0] if languages else "en").lower()[:10]
    out = []
    for dl in dl_matches[:3]:
        out.append({"download_url": BASE + dl, "language": lang, "hearing_impaired": False})
    return out


def download(*, download_url: str) -> bytes:
    """Download subtitle from Subf2m."""
    req = urllib.request.Request(  # noqa: S310
        download_url,
        headers={"User-Agent": USER_AGENT},
        method="GET",
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        return resp.read()
