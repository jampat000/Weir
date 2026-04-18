"""Unit tests for Subber subtitle search helpers."""

from __future__ import annotations

from mediamop.modules.subber.subber_subtitle_search_service import apply_path_mapping


def test_apply_path_mapping_noop_when_disabled() -> None:
    assert apply_path_mapping("/mnt/x/a.mkv", "/arr/", "/sub/", False) == "/mnt/x/a.mkv"


def test_apply_path_mapping_replaces_prefix() -> None:
    assert apply_path_mapping("/arr/show/a.mkv", "/arr", "/mnt/nas", True) == "/mnt/nas/show/a.mkv"


def test_apply_path_mapping_empty_arr_path() -> None:
    assert apply_path_mapping("/arr/a.mkv", "", "/x", True) == "/arr/a.mkv"
