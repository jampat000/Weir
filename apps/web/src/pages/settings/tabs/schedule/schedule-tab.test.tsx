import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, expect, it, vi } from "vitest";

import * as authQueries from "../../../../lib/auth/queries";
import * as maintenanceApi from "../../../../lib/processing/maintenance-api";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import * as librariesApi from "../../../../lib/processing/libraries-api";
import * as settingsApi from "../../../../lib/settings/queries";
import type { AppSettings } from "../../../../lib/settings/types";
import { ScheduleTab } from "./schedule-tab";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return (
    <MemoryRouter>
      <QueryClientProvider client={qc}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

const SETTINGS: AppSettings = {
  product_display_name: "Weir",
  signed_in_home_notice: null,
  setup_wizard_state: "completed",
  app_timezone: "UTC",
  log_retention_days: 30,
  activity_retention_days: 90,
  configuration_backup_enabled: false,
  configuration_backup_interval_hours: 24,
  configuration_backup_preferred_time: "02:00",
  configuration_backup_last_run_at: null,
  updated_at: "2026-04-11T00:00:00Z",
};

function library(over: Partial<ProcessingLibrary> = {}): ProcessingLibrary {
  return {
    id: 1,
    name: "Movies",
    enabled: true,
    media_type: "movie",
    display_order: 0,
    watched_folder: "/srv/movies/in",
    work_folder: "",
    output_folder: "/srv/movies/out",
    media_extensions_csv: ".mkv,.mp4",
    exclude_markers_csv: "__admin__",
    include_patterns_csv: "",
    exclude_patterns_csv: "",
    min_file_size_mb: 0,
    max_file_size_mb: 0,
    rejected_file_action: "leave",
    min_file_age_seconds: 60,
    created_after: null,
    created_before: null,
    modified_after: null,
    modified_before: null,
    exclude_hidden: true,
    top_level_only: false,
    scan_interval_seconds: 300,
    hold_minutes: 0,
    sidecar_patterns_csv: ".srt,.nfo",
    preserve_original_timestamps: false,
    remove_original_after_success: true,
    output_collision_policy: "replace",
    hardware_decode_mode: "off",
    hardware_device: "",
    hardware_disabled_vendors_csv: "",
    ffmpeg_strictness: "normal",
    file_detection_interval_seconds: 30,
    ignore_size_changes: false,
    skip_access_tests: false,
    file_system_events_enabled: true,
    max_attempts: 3,
    retry_backoff_seconds: 300,
    retry_execution_failures: true,
    retry_preflight_failures: false,
    failure_policy: "pass_through",
    schedule_grid: "",
    schedule_enabled: true,
    schedule_hours_limited: false,
    schedule_days: "",
    schedule_start: "00:00",
    schedule_end: "23:59",
    max_concurrent_files: 1,
    priority: 0,
    rule_set_id: null,
    manager_connection_ids: [],
    manager_coverage: "no_upstream_signal",
    manager_coverage_detail:
      "No media manager has been tested for this library.",
    discovered_from_connection_id: null,
    discovered_library_key: null,
    active_job_count: 0,
    updated_at: null,
    ...over,
  };
}

function asOperator() {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(settingsApi, "useAppSettingsQuery").mockReturnValue({
    isPending: false,
    isError: false,
    data: SETTINGS,
  } as ReturnType<typeof settingsApi.useAppSettingsQuery>);
  vi.spyOn(maintenanceApi, "fetchProcessingMaintenance").mockResolvedValue({
    families: [],
  });
}

afterEach(() => {
  vi.restoreAllMocks();
});

it("shows an explicit load error instead of an empty schedule when libraries fail to load", async () => {
  asOperator();
  vi.spyOn(librariesApi, "fetchProcessingLibraries").mockRejectedValue(
    new Error("offline"),
  );

  render(<ScheduleTab />, { wrapper });

  expect(await screen.findByTestId("settings-load-error")).toHaveTextContent(
    "Weir couldn’t load your schedules. Reload the page to try again.",
  );
});

it("asks before an unsaved library's hours are replaced by another library's", async () => {
  asOperator();
  vi.spyOn(librariesApi, "fetchProcessingLibraries").mockResolvedValue([
    library({ id: 1, name: "Movies" }),
    library({ id: 2, name: "TV" }),
  ]);

  render(<ScheduleTab />, { wrapper });

  fireEvent.click(
    (await screen.findAllByRole("button", { name: "Change hours" }))[0],
  );
  fireEvent.pointerDown(screen.getByTestId("schedule-cell-0-9"));
  fireEvent.click(screen.getAllByRole("button", { name: "Change hours" })[1]);

  expect(screen.getByTestId("settings-unsaved-changes")).toHaveTextContent(
    "You have unsaved changes to Movies's hours. Leave without saving?",
  );
  fireEvent.click(screen.getByTestId("settings-unsaved-changes-cancel"));
  expect(screen.getByTestId("schedule-library-editor")).toHaveAccessibleName(
    "Movies hours",
  );
});
