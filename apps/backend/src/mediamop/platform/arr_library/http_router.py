"""HTTP routes for shared Sonarr/Radarr library settings."""

from __future__ import annotations

from fastapi import APIRouter

from mediamop.platform.arr_library.operator_settings_api import router as arr_library_operator_settings_router

router = APIRouter()
router.include_router(arr_library_operator_settings_router)
