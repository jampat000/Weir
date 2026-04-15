"""HTTP for Fetcher-owned Sonarr/Radarr search preferences (SQLite), connections (SQLite + encrypted keys), and tests."""

from __future__ import annotations

from enum import Enum

from fastapi import APIRouter, HTTPException, Request
from starlette import status

from mediamop.api.deps import DbSessionDep, SettingsDep
from mediamop.core.config import MediaMopSettings
from mediamop.modules.fetcher.fetcher_arr_http_resolve import (
    preview_radarr_http_credentials_after_put,
    preview_sonarr_http_credentials_after_put,
    resolve_radarr_http_credentials,
    resolve_sonarr_http_credentials,
)
from mediamop.modules.fetcher.fetcher_arr_operator_settings_schemas import (
    FetcherArrConnectionPutIn,
    FetcherArrConnectionTestIn,
    FetcherArrConnectionTestOut,
    FetcherArrOperatorSettingsLanePutIn,
    FetcherArrOperatorSettingsOut,
    FetcherArrOperatorSettingsPutIn,
)
from mediamop.modules.fetcher.fetcher_arr_operator_settings_service import (
    apply_fetcher_arr_connection_put_radarr,
    apply_fetcher_arr_connection_put_sonarr,
    apply_fetcher_arr_operator_settings_lane_put,
    apply_fetcher_arr_operator_settings_put,
    build_fetcher_arr_operator_settings_out,
    record_connection_test_result_radarr,
    record_connection_test_result_sonarr,
)
from mediamop.modules.fetcher.fetcher_arr_v3_http import FetcherArrV3Client, FetcherArrV3HttpError
from mediamop.platform.activity import constants as act_c
from mediamop.platform.activity.service import record_activity_event
from mediamop.platform.auth.authorization import RequireOperatorDep
from mediamop.platform.auth.csrf import (
    require_session_secret,
    validate_browser_post_origin,
    verify_csrf_token,
)
from mediamop.platform.auth.deps_auth import UserPublicDep

router = APIRouter(tags=["fetcher"])


class ArrSearchLaneKey(str, Enum):
    """URL path keys for single-lane search preference saves."""

    sonarr_missing = "sonarr_missing"
    sonarr_upgrade = "sonarr_upgrade"
    radarr_missing = "radarr_missing"
    radarr_upgrade = "radarr_upgrade"


def _verify_csrf(request: Request, settings: MediaMopSettings, token: str) -> None:
    validate_browser_post_origin(request, settings)
    secret = require_session_secret(settings)
    if not verify_csrf_token(secret, token):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid or expired CSRF token.",
        )


@router.get(
    "/fetcher/arr-operator-settings",
    response_model=FetcherArrOperatorSettingsOut,
)
def get_fetcher_arr_operator_settings(
    _user: UserPublicDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherArrOperatorSettingsOut:
    """Fetcher: read automatic search lanes and connection panels (saved in this app where applicable)."""

    return build_fetcher_arr_operator_settings_out(db, settings)


@router.put(
    "/fetcher/arr-operator-settings",
    response_model=FetcherArrOperatorSettingsOut,
)
def put_fetcher_arr_operator_settings(
    body: FetcherArrOperatorSettingsPutIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherArrOperatorSettingsOut:
    """Fetcher: save automatic search lane preferences (does not change connection fields on this route)."""

    _verify_csrf(request, settings, body.csrf_token)
    try:
        return apply_fetcher_arr_operator_settings_put(db, settings, body)
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=str(e) or "Could not save these settings.",
        ) from e


@router.put(
    "/fetcher/arr-operator-settings/lanes/{lane_key}",
    response_model=FetcherArrOperatorSettingsOut,
)
def put_fetcher_arr_operator_settings_lane(
    lane_key: ArrSearchLaneKey,
    body: FetcherArrOperatorSettingsLanePutIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherArrOperatorSettingsOut:
    """Fetcher: save one automatic search lane (missing or upgrade for TV or movies)."""

    _verify_csrf(request, settings, body.csrf_token)
    try:
        return apply_fetcher_arr_operator_settings_lane_put(
            db,
            settings,
            lane_key=lane_key.value,
            lane=body.lane,
        )
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=str(e) or "Could not save this lane.",
        ) from e


@router.put(
    "/fetcher/arr-connection/sonarr",
    response_model=FetcherArrOperatorSettingsOut,
)
def put_fetcher_arr_connection_sonarr(
    body: FetcherArrConnectionPutIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherArrOperatorSettingsOut:
    _verify_csrf(request, settings, body.csrf_token)
    try:
        return apply_fetcher_arr_connection_put_sonarr(db, settings, body)
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=str(e) or "Could not save TV library connection.",
        ) from e


@router.put(
    "/fetcher/arr-connection/radarr",
    response_model=FetcherArrOperatorSettingsOut,
)
def put_fetcher_arr_connection_radarr(
    body: FetcherArrConnectionPutIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherArrOperatorSettingsOut:
    _verify_csrf(request, settings, body.csrf_token)
    try:
        return apply_fetcher_arr_connection_put_radarr(db, settings, body)
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=str(e) or "Could not save movie library connection.",
        ) from e


@router.post(
    "/fetcher/arr-operator-settings/connection-test",
    response_model=FetcherArrConnectionTestOut,
)
def post_fetcher_arr_connection_test(
    body: FetcherArrConnectionTestIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherArrConnectionTestOut:
    """Try a reachability check to Sonarr or Radarr; outcome is saved on Activity and on the settings row.

    When ``enabled`` is present, ``base_url`` and ``api_key`` are interpreted like ``PUT …/arr-connection/*`` (draft
    test without saving). When ``enabled`` is omitted, credentials come only from stored settings and the server file.
    """

    _verify_csrf(request, settings, body.csrf_token)
    app = (body.app or "").strip().lower()
    if app not in ("sonarr", "radarr"):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Choose Sonarr or Radarr.",
        )

    if app == "sonarr":
        if body.enabled is not None:
            bu = "" if body.base_url is None else body.base_url
            ak = "" if body.api_key is None else body.api_key
            base, key = preview_sonarr_http_credentials_after_put(
                db,
                settings,
                enabled=body.enabled,
                base_url=bu,
                api_key=ak,
            )
        else:
            base, key = resolve_sonarr_http_credentials(db, settings)
        configured = bool((base or "").strip() and (key or "").strip())
        if not configured:
            detail = (
                "TV library connection is not set up yet. Turn the connection on, enter an address and API key here, "
                "or add them to the MediaMop server configuration file."
            )
            record_activity_event(
                db,
                event_type=act_c.FETCHER_ARR_CONNECTION_TEST_FAILED,
                module="fetcher",
                title="TV library connection check did not run",
                detail=detail,
            )
            record_connection_test_result_sonarr(db, ok=False, detail=detail)
            return FetcherArrConnectionTestOut(ok=False, message=detail)
        try:
            FetcherArrV3Client(base.strip(), key.strip()).health_ok()
        except FetcherArrV3HttpError as e:
            msg = (
                "MediaMop could reach your TV library app, but it did not accept the request. "
                "Check the address, API key, and that the app is running."
            )
            record_activity_event(
                db,
                event_type=act_c.FETCHER_ARR_CONNECTION_TEST_FAILED,
                module="fetcher",
                title="TV library connection check failed",
                detail=f"{msg} ({e})",
            )
            record_connection_test_result_sonarr(db, ok=False, detail=msg)
            return FetcherArrConnectionTestOut(ok=False, message=msg)
        except OSError as e:
            msg = "MediaMop could not open a network connection to your TV library app. Check the address and network."
            record_activity_event(
                db,
                event_type=act_c.FETCHER_ARR_CONNECTION_TEST_FAILED,
                module="fetcher",
                title="TV library connection check failed",
                detail=f"{msg} ({e})",
            )
            record_connection_test_result_sonarr(db, ok=False, detail=msg)
            return FetcherArrConnectionTestOut(ok=False, message=msg)

        ok_detail = "MediaMop reached your TV library app and received a normal response."
        record_activity_event(
            db,
            event_type=act_c.FETCHER_ARR_CONNECTION_TEST_SUCCEEDED,
            module="fetcher",
            title="TV library connection check succeeded",
            detail=ok_detail,
        )
        record_connection_test_result_sonarr(db, ok=True, detail="Connection status: OK")
        return FetcherArrConnectionTestOut(
            ok=True,
            message="Connection looks good. This was a one-time check — it is not continuous monitoring.",
        )

    if body.enabled is not None:
        bu = "" if body.base_url is None else body.base_url
        ak = "" if body.api_key is None else body.api_key
        base, key = preview_radarr_http_credentials_after_put(
            db,
            settings,
            enabled=body.enabled,
            base_url=bu,
            api_key=ak,
        )
    else:
        base, key = resolve_radarr_http_credentials(db, settings)
    configured = bool((base or "").strip() and (key or "").strip())
    if not configured:
        detail = (
            "Movie library connection is not set up yet. Turn the connection on, enter an address and API key here, "
            "or add them to the MediaMop server configuration file."
        )
        record_activity_event(
            db,
            event_type=act_c.FETCHER_ARR_CONNECTION_TEST_FAILED,
            module="fetcher",
            title="Movie library connection check did not run",
            detail=detail,
        )
        record_connection_test_result_radarr(db, ok=False, detail=detail)
        return FetcherArrConnectionTestOut(ok=False, message=detail)
    try:
        FetcherArrV3Client(base.strip(), key.strip()).health_ok()
    except FetcherArrV3HttpError as e:
        msg = (
            "MediaMop could reach your movie library app, but it did not accept the request. "
            "Check the address, API key, and that the app is running."
        )
        record_activity_event(
            db,
            event_type=act_c.FETCHER_ARR_CONNECTION_TEST_FAILED,
            module="fetcher",
            title="Movie library connection check failed",
            detail=f"{msg} ({e})",
        )
        record_connection_test_result_radarr(db, ok=False, detail=msg)
        return FetcherArrConnectionTestOut(ok=False, message=msg)
    except OSError as e:
        msg = "MediaMop could not open a network connection to your movie library app. Check the address and network."
        record_activity_event(
            db,
            event_type=act_c.FETCHER_ARR_CONNECTION_TEST_FAILED,
            module="fetcher",
            title="Movie library connection check failed",
            detail=f"{msg} ({e})",
        )
        record_connection_test_result_radarr(db, ok=False, detail=msg)
        return FetcherArrConnectionTestOut(ok=False, message=msg)

    ok_detail = "MediaMop reached your movie library app and received a normal response."
    record_activity_event(
        db,
        event_type=act_c.FETCHER_ARR_CONNECTION_TEST_SUCCEEDED,
        module="fetcher",
        title="Movie library connection check succeeded",
        detail=ok_detail,
    )
    record_connection_test_result_radarr(db, ok=True, detail="Connection status: OK")
    return FetcherArrConnectionTestOut(
        ok=True,
        message="Connection looks good. This was a one-time check — it is not continuous monitoring.",
    )
