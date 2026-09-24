import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { vi } from "vitest";

import * as authQueries from "../../../../lib/auth/queries";
import * as managerApi from "../../../../lib/media-managers/media-managers-api";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import * as ruleSetsApi from "../../../../lib/processing/rule-sets-api";

/** What the Libraries tab's tests share: a saved library, the providers, and an operator's session. */
export function library(
  over: Partial<ProcessingLibrary> = {},
): ProcessingLibrary {
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

export function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return (
    <MemoryRouter>
      <QueryClientProvider client={qc}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

export function asOperator() {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(managerApi, "fetchMediaManagerConnections").mockResolvedValue([]);
  vi.spyOn(ruleSetsApi, "fetchProcessingRuleSets").mockResolvedValue([]);
}
