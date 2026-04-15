/** GET/PUT ``/api/v1/fetcher/arr-operator-settings`` — shapes match backend JSON. */

export type FetcherArrConnectionPanel = {
  enabled: boolean;
  base_url: string;
  api_key_is_saved: boolean;
  last_test_ok: boolean | null;
  last_test_at: string | null;
  last_test_detail: string | null;
  status_headline: string;
  effective_base_url: string | null;
};

export type FetcherArrSearchLane = {
  enabled: boolean;
  max_items_per_run: number;
  retry_delay_minutes: number;
  schedule_enabled: boolean;
  schedule_days: string;
  schedule_start: string;
  schedule_end: string;
  schedule_interval_seconds: number;
};

export type FetcherArrOperatorSettingsOut = {
  sonarr_missing: FetcherArrSearchLane;
  sonarr_upgrade: FetcherArrSearchLane;
  radarr_missing: FetcherArrSearchLane;
  radarr_upgrade: FetcherArrSearchLane;
  sonarr_connection: FetcherArrConnectionPanel;
  radarr_connection: FetcherArrConnectionPanel;
  schedule_timezone: string;
  connection_note: string;
  interval_restart_note: string;
  sonarr_server_configured: boolean;
  radarr_server_configured: boolean;
  sonarr_server_url: string | null;
  radarr_server_url: string | null;
  updated_at: string;
};

export type FetcherArrOperatorSettingsPutBody = {
  sonarr_missing: FetcherArrSearchLane;
  sonarr_upgrade: FetcherArrSearchLane;
  radarr_missing: FetcherArrSearchLane;
  radarr_upgrade: FetcherArrSearchLane;
};

/** Path segment for ``PUT …/arr-operator-settings/lanes/{key}`` — one lane per request. */
export type FetcherArrSearchLaneKey =
  | "sonarr_missing"
  | "sonarr_upgrade"
  | "radarr_missing"
  | "radarr_upgrade";

export type FetcherArrConnectionPutBody = {
  enabled: boolean;
  base_url: string;
  api_key: string;
};

/** POST ``…/connection-test`` — same connection fields as save, plus ``app``. */
export type FetcherArrConnectionTestBody = { app: "sonarr" | "radarr" } & FetcherArrConnectionPutBody;

export type FetcherArrConnectionTestOut = {
  ok: boolean;
  message: string;
};
