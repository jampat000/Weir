"""Static guarantees for the Plex missing-primary preview collector (no apply / live removal in this module)."""

from __future__ import annotations

import ast
from pathlib import Path

import mediamop.modules.pruner.pruner_plex_missing_thumb_candidates as plex_cand


def test_plex_missing_thumb_candidates_has_no_apply_delete_imports() -> None:
    path = Path(plex_cand.__file__).resolve()
    src = path.read_text(encoding="utf-8")
    tree = ast.parse(src)
    forbidden = ("pruner_plex_library_delete", "pruner_apply_job_handler")
    for node in ast.walk(tree):
        if isinstance(node, ast.ImportFrom):
            mod = node.module or ""
            for sub in forbidden:
                assert sub not in mod, mod
        elif isinstance(node, ast.Import):
            for alias in node.names:
                name = alias.name
                for sub in forbidden:
                    assert sub not in name, name


def test_list_plex_missing_thumb_candidates_is_exported() -> None:
    assert callable(plex_cand.list_plex_missing_thumb_candidates)
