import type { ComponentProps } from "react";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { LiveProgressEntry } from "../../lib/activity/use-activity-stream-invalidation";
import type { ProcessingFile } from "../../lib/processing/files-api";
import type { NextItem } from "./dashboard/next-model";
import type { NeedsPanel } from "./dashboard/needs-panel";
import { LEAVING_CARD_MS } from "./leaving-cards";
import { LIBRARY_CLEAN_JOB_KIND } from "./processing-model";
import { ProcessingPage } from "./processing-page";

type NeedsPanelProps = ComponentProps<typeof NeedsPanel>;

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
const fileLogState: { isError: boolean; error: unknown } = {
  isError: false,
  error: null,
};
const fileLogMutate = vi.fn();
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
    manager_connection_ids: [1],
    ready_after_seconds: 60,
  },
  {
    id: 2,
    name: "Movies",
    enabled: true,
    watched_folder: "D:/downloads/movies",
    manager_connection_ids: [1],
    ready_after_seconds: 60,
  },
];
const readiness = {
  worker_health: [] as { module: string; status: string; detail: string }[],
};
const stats = { files_processed: 38, net_space_saved_bytes: 44_236_078_284 };
const needsYou = { count: 0 };
const nextItems: NextItem[] = [];
const refetchFiles = vi.fn();
const refetchLibraries = vi.fn();

const liveProgress: Record<string, LiveProgressEntry> = {};
/** What the page gave the Needs you panel, each time it rendered. */
const needsProps: NeedsPanelProps[] = [];
/** What the Activity stream asked for, each time it asked. */
const streamFilters: { library_id?: number }[] = [];
/** The query keys each call to useActivityStreamInvalidations asked to refresh on activity. */
const invalidations: (readonly unknown[])[] = [];

vi.mock("../../lib/activity/use-activity-stream-invalidation", () => ({
  useActivityStreamInvalidations: (keys: readonly unknown[]) => {
    invalidations.push(keys);
  },
  useLiveProgress: () => liveProgress,
}));
vi.mock("../../lib/processing/files-queries", () => ({
  useProcessingFilesQuery: () => ({
    // A new object each call, so a test that changes `files` and re-renders sees the lists change.
    data: { ...files },
    isPending: false,
    isError: false,
    refetch: refetchFiles,
  }),
  useProcessingFileLog: () => ({
    mutate: fileLogMutate,
    data: undefined,
    isPending: false,
    isError: fileLogState.isError,
    error: fileLogState.error,
  }),
  useRequeueProcessingFile: () => ({ mutate: vi.fn(), isPending: false }),
}));
vi.mock("../../lib/processing/queries", () => ({
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
vi.mock("./dashboard/use-needs-you", () => ({
  useNeedsYou: () => ({ groups: [], count: needsYou.count }),
}));
vi.mock("../../lib/processing/jobs-inspection/queries", () => ({
  useProcessingJobsInspectionQuery: (filter: string) => ({
    data: jobs[filter],
  }),
}));
vi.mock("../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({
    data: libraries,
    refetch: refetchLibraries,
  }),
}));
vi.mock("../../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => ({ data: readiness }),
}));
vi.mock("../../lib/pause/pause-queries", () => ({
  usePauseQuery: () => ({ data: pause }),
  useSavePause: () => ({ mutate: vi.fn(), isPending: false }),
}));
vi.mock("../../lib/settings/queries", () => ({
  useAppSettingsQuery: () => ({ data: undefined }),
}));
vi.mock("../../lib/auth/queries", () => ({
  useMeQuery: () => ({ data: { role: "operator" } }),
  useSetThemeMutation: () => ({ mutate: vi.fn(), isError: false }),
}));
vi.mock("../../lib/activity/queries", () => ({
  // The stream asks for the newest events of every kind; Just finished and the chart ask for one kind.
  useActivityRecentQuery: (filters: {
    event_type?: string;
    library_id?: number;
  }) => {
    if (!filters.event_type) streamFilters.push(filters);
    return {
      data: (filters.event_type
        ? activity[filters.event_type]
        : activity.stream) ?? {
        items: [],
      },
    };
  },
  useActivityWindowQuery: (filters: { event_type: string }) => {
    const items = activity[filters.event_type]?.items ?? [];
    return { data: { items, total: items.length, complete: true } };
  },
}));
// The next-up list and the Health panel read their own endpoints; their tests are beside them.
vi.mock("./dashboard/use-next-items", () => ({
  useNextItems: () => nextItems,
}));
vi.mock("./dashboard/needs-panel", () => ({
  NeedsPanel: (props: NeedsPanelProps) => {
    needsProps.push(props);
    return (
      <button type="button" onClick={() => props.onOpen?.(file({ id: 9 }))}>
        Open the story
      </button>
    );
  },
}));
vi.mock("./dashboard/health-panel", () => ({
  HealthPanel: () => <div data-testid="health-panel" />,
}));
vi.mock("../history/history-rejected-again", () => ({
  ProcessRejectedAgain: () => <button type="button">Process all again</button>,
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
    created_at: "2026-08-18T09:50:00",
    updated_at: "2026-08-18T09:59:00",
    last_seen_at: null,
    last_attempt_at: null,
    ...overrides,
  };
}

/** A library clean that is running for the Movies workflow. */
function libraryCleanJob(id: number) {
  return {
    id,
    dedupe_key: `clean-${id}`,
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
    created_at: "2026-08-18T09:40:00",
    updated_at: "2026-08-18T09:40:00",
  };
}

function renderLive(address = "/") {
  return render(
    <MemoryRouter initialEntries={[address]}>
      <ProcessingPage />
    </MemoryRouter>,
  );
}

describe("ProcessingPage", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date("2026-08-18T10:00:00Z"));
    files.files = [];
    files.status_counts = {};
    for (const path of Object.keys(liveProgress)) delete liveProgress[path];
    jobs.active = { jobs: [] };
    jobs.failed = { jobs: [] };
    activity["processing.file_remux_pass_completed"] = { items: [] };
    activity["library.file_cleaned"] = { items: [] };
    activity.stream = { items: [] };
    nextItems.length = 0;
    pause.paused = false;
    needsYou.count = 0;
    readiness.worker_health = [];
    refetchFiles.mockClear();
    refetchLibraries.mockClear();
    invalidations.length = 0;
    streamFilters.length = 0;
    needsProps.length = 0;
    fileLogState.isError = false;
    fileLogState.error = null;
  });

  afterEach(() => vi.useRealTimers());

  it("fetches fresh data once a countdown runs out, instead of saying it is checking for ever", () => {
    files.files = [
      file({
        id: 1,
        status: "on_hold",
        status_reason: "This file changed too recently.",
        hold_until: "2026-08-18T09:59:55Z",
      }),
    ];
    renderLive();

    expect(refetchFiles).toHaveBeenCalled();
    expect(refetchLibraries).toHaveBeenCalled();
  });

  it("filters what the Pipeline shows from the header, which starts on Everything", () => {
    files.files = [file({ id: 1, status: "unprocessed" })];
    jobs.active = { jobs: [libraryCleanJob(40)] };
    renderLive();
    expect(screen.getByRole("button", { name: "Everything" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    const board = screen.getByTestId("pipeline-board");
    expect(board).toHaveTextContent("The Quiet Harbour S01E03");
    expect(board).toHaveTextContent("Paper Lanterns (2023)");

    fireEvent.click(screen.getByRole("button", { name: "Library cleaning" }));

    expect(board).not.toHaveTextContent("The Quiet Harbour S01E03");
    expect(board).toHaveTextContent("Paper Lanterns (2023)");
    expect(
      screen.getByRole("button", { name: "Library cleaning" }),
    ).toHaveAttribute("aria-pressed", "true");
  });

  describe("narrowed to one workflow from the address", () => {
    beforeEach(() => {
      files.files = [file({ id: 1, status: "unprocessed" })];
      jobs.active = { jobs: [libraryCleanJob(40)] };
    });

    it("shows the Pipeline of that workflow's files and library cleans only", () => {
      const tv = renderLive("/?workflow=1");
      const tvBoard = screen.getByTestId("pipeline-board");
      expect(tvBoard).toHaveTextContent("The Quiet Harbour S01E03");
      expect(tvBoard).not.toHaveTextContent("Paper Lanterns (2023)");
      tv.unmount();

      renderLive("/?workflow=2");
      const moviesBoard = screen.getByTestId("pipeline-board");
      expect(moviesBoard).not.toHaveTextContent("The Quiet Harbour S01E03");
      expect(moviesBoard).toHaveTextContent("Paper Lanterns (2023)");
    });

    it("lists only that workflow's files under Working on now", () => {
      files.files = [
        file({ id: 3, status: "processing", library_id: 1 }),
        file({
          id: 4,
          status: "processing",
          library_id: 2,
          relative_path: "Glass.Orchard.S01E04.2160p.WEB-DL.mkv",
        }),
      ];
      renderLive("/?workflow=2");

      const tile = screen.getByRole("region", { name: "Working on now" });
      expect(within(tile).getByTestId("live-working")).toHaveTextContent(
        "Glass Orchard S01E04",
      );
      expect(within(tile).getByTestId("live-working")).not.toHaveTextContent(
        "The Quiet Harbour",
      );
    });

    it("asks the Activity stream only for that workflow's entries", () => {
      renderLive("/?workflow=2");

      expect(streamFilters.at(-1)).toMatchObject({ library_id: 2 });
    });

    it("asks for every workflow's entries when none is chosen", () => {
      renderLive("/");

      expect(streamFilters.at(-1)?.library_id).toBeUndefined();
    });
  });

  describe("the band", () => {
    it("says how many files are being worked on out of how many may be at once, and names each", () => {
      files.files = [
        file({
          id: 3,
          status: "processing",
          relative_path: "Glass.Orchard.S01E04.2160p.WEB-DL.mkv",
          progress_percent: 46,
          progress_stage: "writing",
        }),
      ];
      renderLive();

      const tile = screen.getByRole("region", { name: "Working on now" });
      expect(within(tile).getByTestId("live-working-count")).toHaveTextContent(
        "1",
      );
      expect(tile).toHaveTextContent("of 2 at once");
      expect(within(tile).getByTestId("live-working")).toHaveTextContent(
        "Glass Orchard S01E04 · Writing46%",
      );
    });

    it("opens a working file's story from its row", () => {
      files.files = [
        file({
          id: 3,
          status: "processing",
          relative_path: "Glass.Orchard.S01E04.2160p.WEB-DL.mkv",
          progress_percent: 46,
        }),
      ];
      renderLive();

      fireEvent.click(
        within(screen.getByTestId("live-working")).getByRole("button"),
      );

      expect(fileLogMutate).toHaveBeenCalledWith(3);
    });

    it("names the wait every workflow holds a new download for, and links to where it is changed", () => {
      const before = libraries.map((library) => library.ready_after_seconds);
      libraries.forEach((library) => {
        library.ready_after_seconds = 10;
      });
      try {
        renderLive();

        const tile = screen.getByRole("region", { name: "Working on now" });
        expect(tile).toHaveTextContent("new downloads wait 10s");
        expect(
          within(tile).getByRole("link", { name: "Change" }),
        ).toHaveAttribute("href", "/settings?tab=performance");
      } finally {
        libraries.forEach((library, index) => {
          library.ready_after_seconds = before[index];
        });
      }
    });

    it("keeps counting files being worked on when the page is narrowed to another kind of work", () => {
      files.files = [
        file({
          id: 3,
          status: "processing",
          relative_path: "Glass.Orchard.S01E04.2160p.WEB-DL.mkv",
          progress_percent: 46,
        }),
      ];
      renderLive();

      fireEvent.click(screen.getByRole("button", { name: "Library cleaning" }));

      expect(screen.getByTestId("live-working-count")).toHaveTextContent("1");
    });

    it("says what has been cleaned today, and offers the files that need a look", () => {
      needsYou.count = 3;
      renderLive();

      const tile = screen.getByRole("region", { name: "Today" });
      expect(within(tile).getByTestId("live-done-today")).toHaveTextContent(
        "38",
      );
      expect(tile).toHaveTextContent("41.20 GB saved");
      expect(
        within(tile).getByRole("button", { name: "3 need a look →" }),
      ).toBeInTheDocument();
    });

    it("says Paused in Next, and that work already running finishes", () => {
      pause.paused = true;
      renderLive();

      const tile = screen.getByRole("region", { name: "Next" });
      expect(tile).toHaveTextContent("Paused");
      expect(tile).toHaveTextContent("Running files finish first.");
      expect(screen.getByTestId("pipeline-board")).toHaveTextContent(
        "Paused · nothing new starts.",
      );
    });

    it("counts down to the next thing Weir does on its own", () => {
      nextItems.push({
        key: "scan-1",
        label: "Look for new downloads in TV",
        to: "/settings?tab=libraries&edit=1",
        at: Date.parse("2026-08-18T10:00:42Z"),
        intervalSeconds: 300,
      });
      renderLive();

      const tile = screen.getByRole("region", { name: "Next" });
      expect(within(tile).getByTestId("live-next-figure")).toHaveTextContent(
        "42 s",
      );
      expect(tile).toHaveTextContent("Look for new downloads in TV");
    });
  });

  describe("Needs you", () => {
    it("is given the workflow the page is narrowed to", () => {
      renderLive("/?workflow=2");

      expect(needsProps.at(-1)?.workflowId).toBe(2);
    });

    it("is not narrowed when no workflow is chosen", () => {
      renderLive();

      expect(needsProps.at(-1)?.workflowId).toBeNull();
    });

    it("opens a file's story from a row", () => {
      files.files = [file({ id: 9, status: "processing_failed" })];
      renderLive();

      fireEvent.click(screen.getByRole("button", { name: "Open the story" }));

      expect(fileLogMutate).toHaveBeenCalledWith(9);
    });
  });

  it("lists what Weir just did, in the words of what happened to each file", () => {
    activity.stream = {
      items: [
        {
          id: 501,
          created_at: "2026-08-18T09:58:00",
          event_type: "processing.file_remux_pass_completed",
          title: "x",
          module: "processing",
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

    const stream = screen.getByTestId("live-stream");
    expect(stream).toHaveTextContent("The Quiet Harbour S01E06 cleaned");
    expect(stream).toHaveTextContent(
      "Saved 319 MB · removed 4 audio, 2 subtitles",
    );
    expect(stream).toHaveTextContent("2 min ago");
  });

  it("says a file's story could not load, through the shared load-error wording", () => {
    files.files = [
      file({
        id: 3,
        status: "processing",
        relative_path: "Glass.Orchard.S01E04.2160p.WEB-DL.mkv",
        progress_percent: 46,
      }),
    ];
    fileLogState.isError = true;
    fileLogState.error = new Error("boom");
    renderLive();

    fireEvent.click(
      within(screen.getByTestId("live-working")).getByRole("button"),
    );

    expect(
      screen.getByText(
        "Weir couldn't load what happened to this file. Reload the page to try again.",
      ),
    ).toBeInTheDocument();
  });

  describe("a file that leaves Working", () => {
    const RUNNING = {
      id: 1,
      relative_path: "Glass.Orchard.S01E04.2160p.WEB-DL.mkv",
      progress_percent: 60,
      progress_stage: "writing",
    };

    function rerenderLive(view: ReturnType<typeof renderLive>) {
      view.rerender(
        <MemoryRouter>
          <ProcessingPage />
        </MemoryRouter>,
      );
    }

    function startRunning() {
      files.files = [file({ ...RUNNING, status: "processing" })];
      const view = renderLive();
      expect(screen.getByTestId("live-working")).toBeInTheDocument();
      return view;
    }

    it("stays on the Pipeline as Delivered for a moment when it finishes, then lets it drop", () => {
      const view = startRunning();

      files.files = [file({ id: 1, status: "processed" })];
      rerenderLive(view);

      const board = screen.getByTestId("pipeline-board");
      expect(board).toHaveTextContent("Glass Orchard S01E04");
      expect(board).toHaveTextContent("✓ Delivered");
      expect(screen.queryByTestId("live-working")).toBeNull();

      act(() => {
        vi.advanceTimersByTime(LEAVING_CARD_MS);
      });
      expect(board).not.toHaveTextContent("Glass Orchard S01E04");
    });

    it("says why it stopped when it fails", () => {
      const view = startRunning();

      files.files = [
        file({
          id: 1,
          status: "processing_failed",
          status_reason: "The new file would not play. The original is safe.",
        }),
      ];
      rerenderLive(view);

      expect(screen.getByTestId("pipeline-board")).toHaveTextContent(
        "Couldn't finish · original kept",
      );
    });

    it("leaves no card behind for a file that only went back to waiting", () => {
      const view = startRunning();

      files.files = [file({ id: 1, status: "unprocessed" })];
      rerenderLive(view);

      expect(screen.getByTestId("pipeline-board")).not.toHaveTextContent(
        "✓ Delivered",
      );
    });
  });

  describe("a pass that has started before the file list says so", () => {
    const path = "The.Quiet.Harbour.S01E03.1080p.WEB-DL.mkv";
    const frame: LiveProgressEntry = {
      relativePath: path,
      status: "processing",
      stage: "writing",
      percent: 12,
      etaSeconds: 30,
      message: "Weir is writing the cleaned-up file.",
      speed: "148x",
      elapsedSeconds: 2,
      removedAudio: [],
      removedSubtitles: [],
    };

    it("counts the file as being worked on as soon as a progress frame arrives for it", () => {
      files.files = [file({ id: 1, status: "unprocessed" })];
      const { rerender } = renderLive();
      expect(screen.getByTestId("live-working-count")).toHaveTextContent("0");

      liveProgress[path] = frame;
      rerender(
        <MemoryRouter>
          <ProcessingPage />
        </MemoryRouter>,
      );

      expect(screen.getByTestId("live-working-count")).toHaveTextContent("1");
      expect(screen.getByTestId("live-working")).toHaveTextContent(
        "The Quiet Harbour S01E03 · Writing12%",
      );
    });

    it("shows a file the server has just claimed on its Checking step, before it has reported any progress", () => {
      files.files = [
        file({
          id: 1,
          status: "processing",
          status_reason: "Weir has claimed this file and is checking it now.",
        }),
      ];
      renderLive();

      expect(screen.getByTestId("live-working")).toHaveTextContent(
        "The Quiet Harbour S01E03 · Checking",
      );
    });
  });
});
