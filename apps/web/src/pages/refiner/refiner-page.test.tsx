import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import type { UserPublic } from "../../lib/api/types";
import { qk } from "../../lib/auth/queries";
import { refinerJobsInspectionQueryKey } from "../../lib/refiner/jobs-inspection/queries";
import {
  refinerOverviewStatsQueryKey,
  refinerPathSettingsQueryKey,
  refinerRemuxRulesSettingsQueryKey,
  refinerRuntimeSettingsQueryKey,
} from "../../lib/refiner/queries";
import type { RefinerPathSettingsOut, RefinerRemuxRulesSettingsOut, RefinerRuntimeSettingsOut } from "../../lib/refiner/types";
import { RefinerPage } from "./refiner-page";

const operatorMe: UserPublic = { id: 1, username: "alice", role: "operator" };

const minimalRefinerPathSettings: RefinerPathSettingsOut = {
  refiner_watched_folder: null,
  refiner_work_folder: null,
  refiner_output_folder: "",
  resolved_default_work_folder: "/tmp/mm-default-refiner-work",
  effective_work_folder: "/tmp/mm-default-refiner-work",
  refiner_tv_watched_folder: null,
  refiner_tv_work_folder: null,
  refiner_tv_output_folder: null,
  resolved_default_tv_work_folder: "/tmp/mm-default-refiner-tv-work",
  effective_tv_work_folder: "/tmp/mm-default-refiner-tv-work",
  updated_at: "2026-04-11T00:00:00Z",
};

const minimalRefinerRemuxRules: RefinerRemuxRulesSettingsOut = {
  movie: {
    primary_audio_lang: "eng",
    secondary_audio_lang: "jpn",
    tertiary_audio_lang: "",
    default_audio_slot: "primary",
    remove_commentary: true,
    subtitle_mode: "remove_all",
    subtitle_langs_csv: "",
    preserve_forced_subs: true,
    preserve_default_subs: true,
    audio_preference_mode: "preferred_langs_quality",
  },
  tv: {
    primary_audio_lang: "eng",
    secondary_audio_lang: "jpn",
    tertiary_audio_lang: "",
    default_audio_slot: "primary",
    remove_commentary: true,
    subtitle_mode: "remove_all",
    subtitle_langs_csv: "",
    preserve_forced_subs: true,
    preserve_default_subs: true,
    audio_preference_mode: "preferred_langs_quality",
  },
  updated_at: "2026-04-11T00:00:00Z",
};

const minimalRefinerRuntimeSettings: RefinerRuntimeSettingsOut = {
  in_process_refiner_worker_count: 0,
  in_process_workers_disabled: true,
  in_process_workers_enabled: false,
  worker_mode_summary: "In-process Refiner workers are off (0).",
  sqlite_throughput_note: "SQLite note for tests.",
  configuration_note: "Change MEDIAMOP_REFINER_WORKER_COUNT in apps/backend/.env, then restart.",
  visibility_note: "Snapshot note for tests.",
  refiner_watched_folder_remux_scan_dispatch_schedule_enabled: false,
  refiner_watched_folder_remux_scan_dispatch_schedule_interval_seconds: 3600,
  refiner_watched_folder_remux_scan_dispatch_periodic_enqueue_remux_jobs: false,
  refiner_watched_folder_remux_scan_dispatch_periodic_remux_dry_run: true,
  refiner_watched_folder_min_file_age_seconds: 300,
  watched_folder_scan_periodic_configuration_note:
    "MEDIAMOP_REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED long env note for tests.",
};

function wrap(ui: ReactNode, client: QueryClient) {
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

function seedRefinerQueries(qc: QueryClient) {
  qc.setQueryData(refinerRuntimeSettingsQueryKey, minimalRefinerRuntimeSettings);
  qc.setQueryData(refinerOverviewStatsQueryKey, {
    window_days: 30,
    files_processed: 42,
    success_rate_percent: 88.5,
    space_saved_gb: null,
    space_saved_available: false,
    space_saved_note: "not available",
  });
  qc.setQueryData(refinerPathSettingsQueryKey, minimalRefinerPathSettings);
  qc.setQueryData(refinerRemuxRulesSettingsQueryKey, minimalRefinerRemuxRules);
  qc.setQueryData(refinerJobsInspectionQueryKey("recent"), { jobs: [], default_recent_slice: true });
  qc.setQueryData(refinerJobsInspectionQueryKey("pending"), { jobs: [] });
  qc.setQueryData(refinerJobsInspectionQueryKey("leased"), { jobs: [] });
  qc.setQueryData(refinerJobsInspectionQueryKey("failed"), { jobs: [] });
}

function openTab(label: string) {
  fireEvent.click(screen.getByRole("tab", { name: label }));
}

function renderRefinerPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  seedRefinerQueries(qc);
  qc.setQueryData(qk.me, operatorMe);
  return render(wrap(<RefinerPage />, qc));
}

describe("RefinerPage", () => {
  it("does not host Fetcher failed-import UI", () => {
    renderRefinerPage();
    expect(screen.queryByTestId("fetcher-failed-imports-workspace")).toBeNull();
    expect(screen.queryByTestId("fetcher-failed-imports-settings")).toBeNull();
    expect(screen.queryByTestId("fetcher-failed-imports-status-filter")).toBeNull();
  });

  it("Overview is the default tab, stays Refiner-scoped, and links Activity without leaking env keys", () => {
    renderRefinerPage();
    expect(screen.getByTestId("refiner-overview-panel")).toBeInTheDocument();
    const overview = screen.getByTestId("refiner-overview-panel").textContent ?? "";
    expect(overview).toMatch(/At a glance/i);
    expect(overview).not.toMatch(/Fetcher/i);
    expect(overview).not.toMatch(/MEDIAMOP_/i);
    expect(overview).not.toMatch(/refiner_jobs/i);
    expect(screen.getByRole("link", { name: "Activity" })).toHaveAttribute("href", "/app/activity");
  });

  it("Overview surfaces saved audio/subtitle defaults at a glance", () => {
    renderRefinerPage();
    const card = screen.getByTestId("refiner-overview-audio-subtitles-glance");
    expect(card.textContent).toMatch(/Audio & subtitles/i);
    expect(card.textContent).toMatch(/English/i);
    expect(card.textContent).toMatch(/Japanese/i);
    expect(card.textContent).toMatch(/Remove all subtitles/i);
  });

  it("Overview shows last-30-day stats card", () => {
    renderRefinerPage();
    const panel = screen.getByTestId("refiner-overview-at-a-glance");
    expect(panel.textContent).toMatch(/Last 30 days/i);
    expect(panel.textContent).toMatch(/Files processed/i);
    expect(panel.textContent).toMatch(/42/i);
    expect(panel.textContent).toMatch(/Success rate/i);
    expect(panel.textContent).toMatch(/88.5%/i);
  });

  it("Jobs tab shows Refiner jobs inspection with honest split from Activity", () => {
    renderRefinerPage();
    openTab("Jobs");
    const block = screen.getByTestId("refiner-jobs-inspection-section");
    expect(block.textContent).toMatch(/Activity/i);
    expect(block.textContent).toMatch(/queue view/i);
  });

  it("Libraries tab folder-check copy stays explicit about not being a filesystem watcher", () => {
    renderRefinerPage();
    openTab("Libraries");
    const section = screen.getByTestId("refiner-watched-folder-scan-section");
    expect(section.textContent).toMatch(/not.*filesystem watcher/i);
  });

  it("Workers tab explains timed scan interval and job-type purpose in plain language", () => {
    renderRefinerPage();
    openTab("Workers");
    const block = screen.getByTestId("refiner-runtime-settings");
    const text = block.textContent ?? "";
    expect(text).toMatch(/every 3600 seconds/i);
    expect(text).toMatch(/not live filesystem watching/i);
    expect(text).toMatch(/What each Refiner job does/i);
  });

  it("has no Fetcher link on the page", () => {
    renderRefinerPage();
    expect(screen.queryByRole("link", { name: "Fetcher" })).toBeNull();
  });

  it("Workers tab still surfaces full server configuration including env keys for operators who need them", () => {
    renderRefinerPage();
    openTab("Workers");
    const block = screen.getByTestId("refiner-runtime-settings");
    expect(block).toBeInTheDocument();
    expect(block.textContent).toMatch(/MEDIAMOP_REFINER_WORKER_COUNT/i);
    expect(block.textContent).toMatch(/0/);
    expect(block.textContent?.toLowerCase()).toMatch(/sqlite/);
  });
});
