import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ProcessingFile } from "../../lib/processing/files-api";
import { LIBRARY_CLEAN_JOB_KIND } from "./processing-model";
import { ProcessingPage } from "./processing-page";

const files: {
  files: ProcessingFile[];
  status_counts: Record<string, number>;
} = {
  files: [],
  status_counts: {},
};
const jobs: Record<string, { jobs: unknown[] }> = {
  active: { jobs: [] },
  failed: { jobs: [] },
};
const activity: Record<string, { items: unknown[] }> = {};
const pause = {
  paused: false,
  reason: "",
  paused_until: null,
  scan_while_paused: true,
  in_flight_policy: "",
};
const libraries = [
  {
    id: 1,
    name: "TV",
    enabled: true,
    watched_folder: "D:/downloads/tv",
    min_file_age_seconds: 60,
  },
  {
    id: 2,
    name: "Movies",
    enabled: true,
    watched_folder: "D:/downloads/movies",
    min_file_age_seconds: 60,
  },
];
const readiness = {
  worker_health: [] as { module: string; status: string; detail: string }[],
};
const stats = { files_processed: 38, net_space_saved_bytes: 44_236_078_284 };

vi.mock("../../lib/activity/use-activity-stream-invalidation", () => ({
  useActivityStreamInvalidations: () => undefined,
}));
vi.mock("../../lib/processing/files-queries", () => ({
  processingFilesKey: () => ["processing", "files"],
  useProcessingFilesQuery: () => ({
    data: files,
    isPending: false,
    isError: false,
  }),
  useProcessingFileLog: () => ({
    mutate: vi.fn(),
    data: undefined,
    isPending: false,
    isError: false,
  }),
}));
vi.mock("../../lib/processing/queries", () => ({
  processingOverviewStatsQueryKey: ["processing", "overview-stats"],
  processingFilesAtOnceQueryKey: ["processing", "files-at-once"],
  useProcessingOverviewStatsQuery: () => ({ data: stats }),
  useProcessingFilesAtOnceQuery: () => ({
    data: {
      effective_files_at_once: 2,
      running: 1,
      waiting: 1,
      waiting_for: "free_slot",
      message: "1 waiting for a free lane",
    },
  }),
}));
vi.mock("../../lib/processing/jobs-inspection/queries", () => ({
  processingJobsInspectionQueryKey: (filter: string) => [
    "processing",
    "jobs",
    filter,
  ],
  useProcessingJobsInspectionQuery: (filter: string) => ({
    data: jobs[filter],
  }),
}));
vi.mock("../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({ data: libraries }),
}));
vi.mock("../../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => ({ data: readiness }),
}));
vi.mock("../../lib/pause/pause-queries", () => ({
  usePauseQuery: () => ({ data: pause }),
  useSavePause: () => ({ mutate: vi.fn(), isPending: false }),
}));
vi.mock("../../lib/auth/queries", () => ({
  useMeQuery: () => ({ data: { role: "operator" } }),
}));
vi.mock("../../lib/activity/queries", () => ({
  activityRecentKey: ["activity", "recent"],
  useActivityRecentQuery: (filters: { event_type: string }) => ({
    data: activity[filters.event_type] ?? { items: [] },
  }),
}));

function file(overrides: Partial<ProcessingFile>): ProcessingFile {
  return {
    id: 1,
    library_id: 1,
    library_name: "TV",
    relative_path: "The.Quiet.Harbour.S01E03.1080p.WEB-DL.mkv",
    status: "unprocessed",
    status_reason: "",
    blocked_by_connection: null,
    size_bytes: 2_437_000_000,
    failure_class: null,
    failure_attempts: 0,
    next_retry_at: null,
    output_collision_policy: null,
    output_collision_action: null,
    output_collision_reason: null,
    video_width: 1920,
    video_height: 1080,
    video_codec: "h264",
    audio_track_count: 5,
    subtitle_track_count: 7,
    duration_seconds: 2700,
    direct_play: [],
    progress_percent: null,
    progress_message: null,
    progress_eta_seconds: null,
    hold_until: null,
    size_changed_at: null,
    created_at: "2026-09-22T09:50:00",
    updated_at: "2026-09-22T09:59:00",
    last_seen_at: null,
    last_attempt_at: null,
    ...overrides,
  };
}

function renderLive() {
  return render(
    <MemoryRouter>
      <ProcessingPage />
    </MemoryRouter>,
  );
}

describe("ProcessingPage", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date("2026-09-22T10:00:00Z"));
    files.files = [];
    files.status_counts = {};
    jobs.active = { jobs: [] };
    jobs.failed = { jobs: [] };
    activity["processing.file_remux_pass_completed"] = { items: [] };
    activity["library.file_cleaned"] = { items: [] };
    pause.paused = false;
    readiness.worker_health = [];
  });

  it("puts each file in the lane its state names, with what it is doing", () => {
    files.files = [
      file({
        id: 1,
        status: "on_hold",
        status_reason:
          "This file changed too recently. Weir waits 60s after the last change.",
        hold_until: "2026-09-22T10:00:21",
        size_changed_at: "2026-09-22T09:59:21",
      }),
      file({
        id: 2,
        status: "unprocessed",
        relative_path: "Northbound.S01E02.720p.WEB-DL.mkv",
      }),
      file({
        id: 3,
        status: "processing",
        relative_path: "Glass.Orchard.S01E04.2160p.WEB-DL.mkv",
        progress_percent: 46,
        progress_eta_seconds: 41,
        progress_speed: "148x",
        progress_removed_audio: ["German", "French", "Spanish"],
        progress_removed_subtitles: ["German"],
      }),
      file({
        id: 4,
        status: "processing",
        relative_path: "Seoul.Nights.S01E08.mkv",
        progress_status: "finishing",
      }),
    ];
    renderLive();

    expect(
      within(screen.getByTestId("live-lane-arriving")).getByTestId(
        "live-arriving",
      ),
    ).toHaveTextContent("The Quiet Harbour S01E03");
    expect(screen.getByTestId("live-arriving")).toHaveTextContent("21s");
    expect(screen.getByTestId("live-waiting")).toHaveTextContent("1st in line");
    expect(screen.getByTestId("live-waiting")).toHaveTextContent(
      "Download · TV",
    );

    const working = screen.getByTestId("live-working");
    expect(working).toHaveTextContent("Glass Orchard S01E04");
    expect(working).toHaveTextContent("46%");
    expect(working).toHaveTextContent("41 s left");
    expect(working).toHaveTextContent("Removing 3 audio, 1 subtitle");
    expect(working).toHaveTextContent("148× real time");
    expect(screen.getByTestId("live-handing")).toHaveTextContent(
      "Seoul Nights S01E08",
    );
    expect(screen.getByTestId("live-lane-working")).toHaveTextContent(
      "of 2 lanes",
    );
  });

  it("shows a library clean as work of its own, and the filter narrows to one kind", () => {
    files.files = [file({ id: 1, status: "unprocessed" })];
    jobs.active = {
      jobs: [
        {
          id: 40,
          dedupe_key: "clean-40",
          job_kind: LIBRARY_CLEAN_JOB_KIND,
          status: "leased",
          attempt_count: 0,
          max_attempts: 3,
          lease_owner: "w",
          lease_expires_at: null,
          last_error: null,
          payload_json: JSON.stringify({
            library_id: 2,
            path: "Paper Lanterns (2023)/Paper.Lanterns.2023.mkv",
          }),
          created_at: "2026-09-22T09:40:00",
          updated_at: "2026-09-22T09:40:00",
        },
      ],
    };
    renderLive();

    expect(screen.getByTestId("live-working")).toHaveTextContent(
      "Paper Lanterns (2023)",
    );
    expect(screen.getByTestId("live-working")).toHaveTextContent(
      "Library · Movies",
    );
    expect(screen.getByTestId("live-waiting")).toHaveTextContent(
      "The Quiet Harbour S01E03",
    );

    fireEvent.click(screen.getByRole("button", { name: "Library cleaning" }));

    expect(screen.getByTestId("live-working")).toHaveTextContent(
      "Paper Lanterns (2023)",
    );
    expect(screen.queryByTestId("live-waiting")).toBeNull();
    expect(screen.getByTestId("live-lane-waiting")).toHaveTextContent(
      "Nothing waiting",
    );
  });

  it("says what needs a person, and nothing when a healthy install has nothing to say", () => {
    renderLive();
    expect(screen.queryByTestId("live-needs")).toBeNull();

    files.files = [
      file({
        id: 9,
        status: "processing_failed",
        relative_path: "Ember.and.Ash.S01E02.mkv",
      }),
    ];
    jobs.failed = { jobs: [{ id: 3 }, { id: 4 }] };
    readiness.worker_health = [
      {
        module: "processing",
        status: "degraded",
        detail: "No worker has taken a job for 20 minutes.",
      },
    ];
    renderLive();

    const needs = screen.getByTestId("live-needs");
    expect(needs).toHaveTextContent("2 jobs failed");
    expect(needs).toHaveTextContent(
      "Background work has stopped. No worker has taken a job for 20 minutes.",
    );
    expect(needs).toHaveTextContent("Ember and Ash S01E02 is stuck");
    expect(
      within(needs).getByRole("link", { name: /Deal with them/ }),
    ).toHaveAttribute(
      "href",
      "/system?tab=history&show=downloads&status=processing_failed",
    );
  });

  it("says when processing is paused, and what that means for work already running", () => {
    pause.paused = true;
    pause.reason = "Paused by alice until 07:00.";
    renderLive();

    expect(screen.getByTestId("live-paused")).toHaveTextContent(
      "Paused by alice until 07:00. Files already being written finish; nothing new starts.",
    );
    expect(screen.getByTestId("live-lane-working")).toHaveTextContent(
      "Paused. Nothing new starts until you resume.",
    );
  });

  it("lists what just finished, in the words of what Weir did", () => {
    activity["processing.file_remux_pass_completed"] = {
      items: [
        {
          id: 501,
          created_at: "2026-09-22T09:58:00",
          event_type: "processing.file_remux_pass_completed",
          title: "x",
          library_id: 1,
          relative_path: "The.Quiet.Harbour.S01E06.mkv",
          detail: JSON.stringify({
            outcome: "live_output_written",
            ok: true,
            relative_media_path: "The.Quiet.Harbour.S01E06.mkv",
            source_size_bytes: 2_437_000_000,
            output_size_bytes: 2_103_000_000,
            removed_audio: ["a", "b", "c", "d"],
            removed_subtitles: ["e", "f"],
          }),
        },
      ],
    };
    renderLive();

    const finished = screen.getByTestId("live-finished");
    expect(finished).toHaveTextContent("The Quiet Harbour S01E06");
    expect(finished).toHaveTextContent(
      "Saved 319 MB · removed 4 audio, 2 subtitles",
    );
    expect(finished).toHaveTextContent("2 min ago");
    expect(screen.getByTestId("live-done-today")).toHaveTextContent("38");
  });
});
