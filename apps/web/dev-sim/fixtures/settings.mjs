/** The singletons behind Settings and System: what a fresh, working install answers before anyone changes a thing. */
import { shaped } from "../openapi/skeleton.mjs";
import { toWire, HOUR_MS, DAY_MS } from "../wire-time.mjs";

export const APP_VERSION = "3.2.16";
/** How long Weir had been running when the session opened. */
export const WEIR_UPTIME_AT_START_MS = 6 * HOUR_MS;
export const FILES_AT_ONCE_DEFAULT = 2;
const WORKER_SLOTS = 4;

const appTimezone = () =>
  Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";

export function initialSuiteSettings() {
  return shaped("SuiteSettingsOut", {
    app_timezone: appTimezone(),
    setup_wizard_state: "completed",
    log_retention_days: 30,
    activity_retention_days: 90,
    configuration_backup_enabled: true,
    configuration_backup_interval_hours: 24,
    configuration_backup_preferred_time: "02:00",
    configuration_backup_last_run_at: toWire(Date.now() - 9 * HOUR_MS),
    signed_in_home_notice: null,
    updated_at: toWire(Date.now() - 3 * DAY_MS),
  });
}

export function initialOperatorSettings() {
  const schedule = {
    days: "",
    enabled: true,
    end: "23:59",
    hours_limited: false,
    start: "00:00",
  };
  return shaped("ProcessingOperatorSettingsOut", {
    max_concurrent_files: FILES_AT_ONCE_DEFAULT,
    file_log_retention_days: 90,
    keep_failed_work_files: false,
    runner_capacity: 8,
    runner_budget_enabled: false,
    runner_cost_sd: 1,
    runner_cost_720p: 2,
    runner_cost_1080p: 4,
    runner_cost_4k: 8,
    schedule_timezone: appTimezone(),
    movie_schedule_days: schedule.days,
    movie_schedule_enabled: schedule.enabled,
    movie_schedule_end: schedule.end,
    movie_schedule_hours_limited: schedule.hours_limited,
    movie_schedule_start: schedule.start,
    tv_schedule_days: schedule.days,
    tv_schedule_enabled: schedule.enabled,
    tv_schedule_end: schedule.end,
    tv_schedule_hours_limited: schedule.hours_limited,
    tv_schedule_start: schedule.start,
    work_temp_stale_sweep_enabled: true,
    work_temp_stale_sweep_interval_seconds: 3600,
    unclaimed_handback_cleanup_enabled: true,
    unclaimed_handback_window_days: 7,
    unclaimed_handback_cleanup_interval_seconds: 21600,
    updated_at: toWire(Date.now() - 3 * DAY_MS),
  });
}

export function filesAtOnceLimits() {
  return { worker_slots: WORKER_SLOTS };
}

export function initialUpdateSettings() {
  return shaped("UpdateSettingsOut", {
    mode: "NotifyOnly",
    check_interval_minutes: 360,
    check_on_startup: true,
  });
}

export function updateStatus() {
  return shaped("SuiteUpdateStatusOut", {
    current_version: APP_VERSION,
    install_type: "windows",
    status: "up_to_date",
    summary: `Weir ${APP_VERSION} is the newest release.`,
    latest_version: APP_VERSION,
    latest_name: `Weir ${APP_VERSION}`,
    in_app_upgrade_supported: true,
  });
}

export function initialMetadataProvider() {
  return shaped("MetadataProviderOut", {
    provider: "",
    base_url: "",
    key_configured: false,
    known_providers: ["tmdb"],
    artwork_enabled: true,
  });
}

export function initialDirectPlayDevices() {
  const device = (id, name, note) => ({
    id,
    name,
    note,
    selected: false,
    source: "builtin",
  });
  return {
    customised: false,
    devices: [
      device(
        "living-room-tv",
        "Living-room TV",
        "A 2020 smart TV that plays H.264 and HEVC, with Dolby Digital audio.",
      ),
      device("phone", "Phone", "A recent phone using the media server's app."),
      device(
        "web-browser",
        "Web browser",
        "Plays H.264 with AAC or Dolby Digital audio.",
      ),
    ],
  };
}

export function readiness() {
  return {
    machine_name: "MEDIA-PC",
    machine_name_looks_generated: false,
    ready: true,
    startup_seconds: 1.4,
    status: "ready",
    version: APP_VERSION,
    steps: [],
    worker_health: [],
  };
}

export function mediaTools() {
  return { ffmpeg: "7.1.1", mkvmerge: "v89.0.0" };
}

export function maintenanceFamilies() {
  const family = (fields) =>
    shaped("MaintenanceFamilyStateOut", { pending: 0, running: 0, ...fields });
  return [
    family({
      family: "work_temp_stale_sweep",
      enabled: true,
      description:
        "Deletes half-written copies Weir left in its work folders once they are old.",
      interval_seconds: 3600,
      last_completed_at: toWire(Date.now() - 40 * 60_000),
      next_run_at: toWire(Date.now() + 20 * 60_000),
    }),
    family({
      family: "unclaimed_handbacks",
      enabled: true,
      description:
        "Deletes Weir's own cleaned copy from a hand-back folder when no media manager imported it in time.",
      window_days: 7,
      interval_seconds: 21600,
      last_completed_at: toWire(Date.now() - 3 * HOUR_MS),
      next_run_at: toWire(Date.now() + 3 * HOUR_MS),
    }),
  ];
}
