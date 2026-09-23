import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
  LibraryCleanResult,
  LibraryFile,
  LibraryFilesResult,
  LibraryOverview,
} from "../../lib/processing/library-mode-api";
import { LibraryPage } from "./library-page";

const libraries = [
  {
    id: 1,
    name: "TV",
    enabled: true,
    media_type: "tv",
    watched_folder: "D:/tv",
  },
  {
    id: 2,
    name: "Movies",
    enabled: true,
    media_type: "movie",
    watched_folder: "D:/movies",
  },
];
const clean = vi.fn();
const setAside = vi.fn();
const rescan = vi.fn();
let filesResult: LibraryFilesResult;
let overviewResult: LibraryOverview;
const lastFilters: Record<string, unknown>[] = [];

vi.mock("../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({
    data: libraries,
    isPending: false,
    isError: false,
  }),
}));
vi.mock(
  "../../lib/processing/library-mode-queries",
  async (importOriginal) => ({
    ...(await importOriginal<
      typeof import("../../lib/processing/library-mode-queries")
    >()),
    useLibraryOverviewQuery: () => ({ data: overviewResult }),
    useLibraryFilesQuery: (_id: number, filters: Record<string, unknown>) => {
      lastFilters.push(filters);
      return { data: filesResult, isPending: false, isError: false };
    },
    useCleanLibraryFiles: () => ({
      mutate: clean,
      isPending: false,
      isError: false,
      error: null,
    }),
    useSetLibraryFileLeaveAlone: () => ({
      mutate: setAside,
      isPending: false,
    }),
    useTriggerLibraryScan: () => ({ mutate: rescan, isPending: false }),
  }),
);
vi.mock("../../lib/processing/rules-preview-api", () => ({
  previewProcessingRules: () =>
    Promise.resolve({
      tracks: [
        {
          index: 1,
          type: "audio",
          codec: "eac3",
          language: "English",
          channels: 6,
          action: "keep",
          reasons: ["Your first choice language"],
          default: true,
          forced: false,
          title: "",
        },
        {
          index: 2,
          type: "audio",
          codec: "aac",
          language: "Spanish",
          channels: 2,
          action: "drop",
          reasons: ["Not a language your rules keep"],
          default: false,
          forced: false,
          title: "",
        },
      ],
      notes: [],
      metadata_notes: [],
      remux_required: true,
      estimated_size_reduction_bytes: 333_000_000,
      estimated_size_reduction_is_estimate: true,
      original_language: null,
      library_id: 1,
      media_scope: "tv",
      inspected_path: "x",
    }),
}));
vi.mock("../../lib/settings/queries", () => ({
  useAppSettingsQuery: () => ({ data: { app_timezone: "UTC" } }),
}));
vi.mock("../../lib/activity/queries", () => ({
  useActivityRecentQuery: () => ({ data: { items: [] } }),
}));

function file(overrides: Partial<LibraryFile>): LibraryFile {
  return {
    path: "D:/tv/The Quiet Harbour/Season 01/The.Quiet.Harbour.S01E01.mkv",
    size_bytes: 2_437_000_000,
    modified_at: 1_758_000_000,
    classification: "would_change",
    summary: "Would remove 4 audio and 2 subtitle tracks",
    reason: null,
    removed_audio_tracks: 4,
    removed_subtitle_tracks: 2,
    estimated_bytes_saved: 333_000_000,
    manager_kind: "sonarr",
    manager_title: "The Quiet Harbour",
    video_codec: "h264",
    video_height: 1080,
    resolution_class: "1080p",
    audio_track_count: 5,
    subtitle_track_count: 7,
    audio_summary: "English 5.1 + 4 dubs",
    subtitle_summary: "7 languages",
    link_count: 1,
    problem_kind: null,
    cleaned_at: null,
    leave_alone: false,
    ...overrides,
  };
}

const totals = {
  files: 14,
  size_bytes: 30_000_000_000,
  matches: 3,
  would_change: 10,
  cannot_process: 1,
  total_removed_audio_tracks: 40,
  total_removed_subtitle_tracks: 20,
  estimated_bytes_saved: 3_300_000_000,
  cleaned: 2,
  left_alone: 1,
};

function renderLibrary() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <LibraryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("LibraryPage", () => {
  beforeEach(() => {
    clean.mockReset();
    setAside.mockReset();
    rescan.mockReset();
    lastFilters.length = 0;
    overviewResult = {
      library_id: 1,
      folders_configured: 1,
      schedule: { enabled: false, next_run_at: null },
      scan: {
        job_id: 1,
        status: "completed",
        running: false,
        generated_at: Math.floor(Date.now() / 1000) - 120,
        errors: [],
      },
      totals,
      breakdowns: {
        video_codec: [],
        resolution: [],
        audio: [],
        audio_language: [],
        subtitle_language: [],
      },
      problems: [
        {
          kind: "seeding",
          title: "Still seeding",
          what_to_do: "Wait",
          files: 1,
          size_bytes: 1,
          sample_paths: [],
        },
      ],
    };
    filesResult = {
      library_id: 1,
      scan: overviewResult.scan,
      summary: totals,
      filtered: totals,
      files: [
        file({}),
        file({
          path: "D:/tv/The Quiet Harbour/Season 01/The.Quiet.Harbour.S01E02.mkv",
          classification: "matches",
          summary: "Already matches your rules",
          estimated_bytes_saved: 0,
        }),
        file({
          path: "D:/tv/Northbound/Season 01/Northbound.S01E01.mkv",
          manager_title: "Northbound",
          classification: "cannot_process",
          summary: "Still seeding, so Weir left it alone",
          problem_kind: "seeding",
          estimated_bytes_saved: 0,
        }),
      ],
      total: 3,
      page: 1,
      page_size: 200,
      sort: "path",
      direction: "asc",
    };
  });

  it("names the library in the title, and groups files under the title they belong to", () => {
    renderLibrary();

    expect(screen.getByTestId("library-picker")).toHaveTextContent("TV");
    expect(
      screen.getByRole("heading", { name: "Library" }),
    ).toBeInTheDocument();
    expect(screen.getAllByTestId("library-row")).toHaveLength(3);
    expect(screen.getByText("Northbound")).toBeInTheDocument();
    expect(
      screen.getByText(/Sonarr · 2 files · 1 would change/),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/back if everything that would change is cleaned/),
    ).toBeInTheDocument();
  });

  it("says when the scheduled check and clean next runs, and nothing while it is off", () => {
    const { unmount } = renderLibrary();
    expect(screen.queryByTestId("library-schedule")).not.toBeInTheDocument();
    unmount();

    overviewResult = {
      ...overviewResult,
      schedule: { enabled: true, next_run_at: "2999-01-02T02:00:00Z" },
    };
    const next = renderLibrary();
    expect(screen.getByTestId("library-schedule")).toHaveTextContent(
      /next scheduled check and clean .*2999/,
    );
    next.unmount();

    overviewResult = {
      ...overviewResult,
      schedule: { enabled: true, next_run_at: "2020-01-01T00:00:00Z" },
    };
    const due = renderLibrary();
    expect(screen.getByTestId("library-schedule")).toHaveTextContent(
      "scheduled check and clean starting now",
    );
    due.unmount();

    overviewResult = {
      ...overviewResult,
      schedule: { enabled: true, next_run_at: null },
    };
    renderLibrary();
    expect(screen.getByTestId("library-schedule")).toHaveTextContent(
      /cannot run/,
    );
  });

  it("makes each count a filter, and asks the server for that filter", () => {
    renderLibrary();
    const chip = screen.getByRole("button", { name: /Would change/ });
    expect(chip).toHaveTextContent("10");

    fireEvent.click(chip);

    expect(chip).toHaveAttribute("aria-pressed", "true");
    expect(lastFilters.at(-1)).toMatchObject({
      classification: "would_change",
    });

    fireEvent.click(chip);
    expect(lastFilters.at(-1)?.classification).toBeUndefined();
  });

  it("only offers to clean what it would change, and asks before doing it", () => {
    renderLibrary();
    const box = (name: string) =>
      within(
        screen
          .getAllByTestId("library-row")
          .find((row) => row.textContent?.includes(name))!,
      ).getByRole("checkbox");
    // Only a file Weir would change can be cleaned; one that already matches, or that Weir will not touch, cannot.
    expect(box("The.Quiet.Harbour.S01E02.mkv")).toBeDisabled();
    expect(box("Northbound.S01E01.mkv")).toBeDisabled();

    fireEvent.click(box("The.Quiet.Harbour.S01E01.mkv"));

    const bulk = screen.getByTestId("library-bulk");
    expect(bulk).toHaveTextContent("1 selected");
    expect(bulk).toHaveTextContent("318 MB back");

    fireEvent.click(
      within(bulk).getByRole("button", { name: "Clean these files" }),
    );

    expect(clean).toHaveBeenCalledWith(
      {
        paths: [
          "D:/tv/The Quiet Harbour/Season 01/The.Quiet.Harbour.S01E01.mkv",
        ],
        confirm: false,
      },
      expect.anything(),
    );
  });

  it("opens a file and says track by track what would go, and why", async () => {
    renderLibrary();

    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );

    const drawer = await screen.findByTestId("library-file-drawer");
    expect(drawer).toHaveTextContent(
      "Would remove 4 audio and 2 subtitle tracks",
    );
    await waitFor(() =>
      expect(drawer).toHaveTextContent("English · EAC3 · 6ch"),
    );
    expect(drawer).toHaveTextContent("Not a language your rules keep");
    expect(
      within(drawer).getByRole("button", { name: "Clean this file" }),
    ).toBeEnabled();
  });

  it("lets you pick the tracks for one file yourself, and sends exactly those", async () => {
    renderLibrary();
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    const drawer = await screen.findByTestId("library-file-drawer");
    await waitFor(() =>
      expect(drawer).toHaveTextContent("English · EAC3 · 6ch"),
    );

    fireEvent.click(
      within(drawer).getByRole("button", {
        name: "Choose the tracks yourself",
      }),
    );
    // Seeded from what the rules would do, so "choose it yourself" starts from the answer you were shown.
    const boxes = within(drawer).getAllByRole("checkbox");
    expect(boxes[0]).toBeChecked();
    expect(boxes[1]).not.toBeChecked();

    // Keep the Spanish track and drop the English one: the opposite of the rules, for this file only.
    fireEvent.click(boxes[0]!);
    fireEvent.click(boxes[1]!);
    fireEvent.click(
      within(drawer).getByRole("button", { name: "Clean with these tracks" }),
    );

    expect(clean).toHaveBeenCalledTimes(1);
    const [[sent]] = clean.mock.calls as [
      [{ paths: string[]; confirm: boolean; manual?: unknown }],
    ];
    expect(sent.paths).toEqual([
      "D:/tv/The Quiet Harbour/Season 01/The.Quiet.Harbour.S01E01.mkv",
    ]);
    expect(sent.confirm).toBe(true);
    expect(sent.manual).toEqual({
      keep: [{ index: 2, default: true, forced: false }],
      order: [2],
      // The size Weir last saw: a file that changed since is refused rather than cleaned to this choice.
      expected_size_bytes: 2_437_000_000,
    });
  });

  it("refuses a choice that would keep no audio, and one that changes nothing", async () => {
    renderLibrary();
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    const drawer = await screen.findByTestId("library-file-drawer");
    await waitFor(() =>
      expect(drawer).toHaveTextContent("English · EAC3 · 6ch"),
    );
    fireEvent.click(
      within(drawer).getByRole("button", {
        name: "Choose the tracks yourself",
      }),
    );
    const clean1 = within(drawer).getByRole("button", {
      name: "Clean with these tracks",
    });

    // Keeping everything is what the file already holds.
    fireEvent.click(within(drawer).getAllByRole("checkbox")[1]!);
    expect(clean1).toBeDisabled();
    expect(drawer).toHaveTextContent("there would be nothing to do");

    // And nothing may keep no audio at all.
    for (const box of within(drawer).getAllByRole("checkbox")) {
      if ((box as HTMLInputElement).checked) fireEvent.click(box);
    }
    expect(clean1).toBeDisabled();
    expect(drawer).toHaveTextContent("Keep at least one audio track");
  });

  it("sets a file aside, and says so", async () => {
    renderLibrary();
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    const drawer = await screen.findByTestId("library-file-drawer");

    fireEvent.click(
      within(drawer).getByRole("checkbox", { name: /Leave this file alone/ }),
    );

    expect(setAside).toHaveBeenCalledWith({
      path: "D:/tv/The Quiet Harbour/Season 01/The.Quiet.Harbour.S01E01.mkv",
      leaveAlone: true,
    });
  });

  it("says what a clean actually did, including what it would not touch", () => {
    clean.mockImplementation(
      (
        _vars: unknown,
        options?: { onSuccess?: (result: LibraryCleanResult) => void },
      ) =>
        options?.onSuccess?.({
          kind: "cleaned",
          queued: 1,
          job_ids: [7],
          files_count: 1,
          tracks_count: 4,
          estimated_bytes_saved: 0,
          skipped_paths: ["D:/tv/Northbound/Season 01/Northbound.S01E01.mkv"],
          warnings: ["Northbound.S01E01.mkv: still shared with a download."],
        }),
    );
    renderLibrary();
    fireEvent.click(
      within(
        screen
          .getAllByTestId("library-row")
          .find((row) => row.textContent?.includes("S01E01.mkv"))!,
      ).getByRole("checkbox"),
    );

    fireEvent.click(screen.getByRole("button", { name: "Clean these files" }));

    const said = screen.getByTestId("library-outcome");
    expect(said).toHaveTextContent("1 file is queued to clean");
    expect(said).toHaveTextContent("Weir left 1 alone: Northbound.S01E01.mkv");
    expect(said).toHaveTextContent("still shared with a download");
  });

  it("counts what Weir has done with these files, and filters by it", () => {
    renderLibrary();

    const cleaned = screen.getByRole("button", { name: /Cleaned/ });
    expect(cleaned).toHaveTextContent("2");
    expect(
      screen.getByRole("button", { name: /Left alone/ }),
    ).toHaveTextContent("1");

    fireEvent.click(cleaned);

    expect(lastFilters.at(-1)).toMatchObject({ state: "cleaned" });
  });

  it("switches library from the title", () => {
    renderLibrary();

    fireEvent.click(
      within(screen.getByTestId("library-picker")).getByRole("button", {
        name: /TV/,
      }),
    );
    fireEvent.click(screen.getByRole("option", { name: /Movies/ }));

    expect(screen.getByTestId("library-picker")).toHaveTextContent("Movies");
  });
});
