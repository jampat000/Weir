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
  LibrarySettings,
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
let librarySettingsResult: LibrarySettings;
let signedInRole: "operator" | undefined;
const lastFilters: Record<string, unknown>[] = [];
const previewRequests: Record<string, unknown>[] = [];

vi.mock("../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({
    data: libraries,
    isPending: false,
    isError: false,
  }),
  useProcessingRuleSetsQuery: () => ({ data: [] }),
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
    useLibrarySettingsQuery: () => ({ data: librarySettingsResult }),
    useLibraryRedownloadsQuery: () => ({
      data: { library_id: 1, titles: [], total: 0 },
      isError: false,
    }),
  }),
);
vi.mock("../../lib/processing/rules-preview-api", () => ({
  previewProcessingRules: (request: Record<string, unknown>) => {
    previewRequests.push(request);
    return Promise.resolve({
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
    });
  },
}));
vi.mock("../../lib/settings/queries", () => ({
  useAppSettingsQuery: () => ({ data: { app_timezone: "UTC" } }),
}));
vi.mock("../../lib/auth/queries", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../lib/auth/queries")>()),
  useMeQuery: () => ({
    data: signedInRole ? { role: signedInRole } : undefined,
  }),
}));
vi.mock("../../lib/pause/pause-queries", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../lib/pause/pause-queries")>()),
  usePauseQuery: () => ({ data: undefined }),
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

function renderLibrary(address = "/library") {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[address]}>
        <LibraryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function settings(over: Partial<LibrarySettings>): LibrarySettings {
  return {
    library_folders: ["D:/tv"],
    library_schedule_enabled: false,
    clean_hardlinked_files: false,
    skip_if_manager_would_redownload: true,
    keep_original_after_clean: false,
    originals_folder: "",
    library_rule_set_id: null,
    ...over,
  };
}

describe("LibraryPage", () => {
  beforeEach(() => {
    localStorage.clear();
    clean.mockReset();
    setAside.mockReset();
    rescan.mockReset();
    lastFilters.length = 0;
    previewRequests.length = 0;
    signedInRole = undefined;
    librarySettingsResult = settings({});
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

  it("names the library beside the title, and groups files under the title they belong to", () => {
    renderLibrary();

    expect(screen.getByTestId("library-picker")).toHaveTextContent("TV");
    expect(screen.getAllByTestId("library-row")).toHaveLength(3);
    expect(screen.getByText("Northbound")).toBeInTheDocument();
    expect(
      screen.getByText(/Sonarr · 2 files · 1 would change/),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/back if everything that would change is cleaned/),
    ).toBeInTheDocument();
  });

  it("puts the figures in the Files card's count, with what Weir does with the files on hover", () => {
    renderLibrary();

    const count = screen.getByText("14 files · 27.94 GB");
    expect(count).toHaveAttribute(
      "title",
      expect.stringContaining("Weir reads them where they are"),
    );
    expect(
      screen.getByText(/14 files, 27.94 GB on your storage/),
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
    // The header has room for a few words; the whole sentence is the tooltip.
    expect(screen.getByTestId("library-schedule")).toHaveTextContent(
      /^· next /,
    );
    expect(screen.getByTestId("library-scan")).toHaveAttribute(
      "title",
      expect.stringMatching(/next scheduled check and clean .*2999/),
    );
    next.unmount();

    overviewResult = {
      ...overviewResult,
      schedule: { enabled: true, next_run_at: "2020-01-01T00:00:00Z" },
    };
    const due = renderLibrary();
    expect(screen.getByTestId("library-schedule")).toHaveTextContent(
      "next starting now",
    );
    expect(screen.getByTestId("library-scan")).toHaveAttribute(
      "title",
      expect.stringContaining("scheduled check and clean starting now"),
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

  it("cleans exactly the selection it confirmed, even if the checkboxes change before you confirm", () => {
    // A second file to change the selection into, after the confirmation has already been asked for.
    filesResult = {
      ...filesResult,
      files: [
        ...filesResult.files,
        file({
          path: "D:/tv/The Quiet Harbour/Season 01/The.Quiet.Harbour.S01E03.mkv",
        }),
      ],
      total: filesResult.files.length + 1,
    };
    clean.mockImplementation(
      (
        vars: { paths: string[]; confirm: boolean },
        options?: { onSuccess?: (result: unknown) => void },
      ) => {
        if (!vars.confirm) {
          options?.onSuccess?.({
            kind: "confirmation_required",
            detail: "",
            files_count: vars.paths.length,
            tracks_count: 1,
            estimated_bytes_saved: 0,
          });
        }
      },
    );
    renderLibrary();
    const box = (name: string) =>
      within(
        screen
          .getAllByTestId("library-row")
          .find((row) => row.textContent?.includes(name))!,
      ).getByRole("checkbox");

    fireEvent.click(box("The.Quiet.Harbour.S01E01.mkv"));
    fireEvent.click(screen.getByRole("button", { name: "Clean these files" }));

    // Checking another file while the confirmation is open must not widen what gets cleaned (#700).
    fireEvent.click(box("The.Quiet.Harbour.S01E03.mkv"));
    fireEvent.click(screen.getByTestId("library-confirm-confirm"));

    expect(clean).toHaveBeenLastCalledWith(
      {
        paths: [
          "D:/tv/The Quiet Harbour/Season 01/The.Quiet.Harbour.S01E01.mkv",
        ],
        confirm: true,
        manual: undefined,
      },
      expect.anything(),
    );
  });

  it("shows a clean's warnings in its confirmation, before anything is cleaned", () => {
    clean.mockImplementation(
      (
        vars: { confirm: boolean },
        options?: { onSuccess?: (result: unknown) => void },
      ) => {
        if (!vars.confirm) {
          options?.onSuccess?.({
            kind: "confirmation_required",
            detail: "",
            files_count: 1,
            tracks_count: 4,
            estimated_bytes_saved: 0,
            warnings: ["This file is still shared with a download."],
          });
        }
      },
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

    expect(screen.getByTestId("library-confirm-warnings")).toHaveTextContent(
      "This file is still shared with a download.",
    );
    expect(clean).not.toHaveBeenCalledWith(
      expect.objectContaining({ confirm: true }),
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
      expect(drawer).toHaveTextContent("Audio 1 · English · 5.1 E-AC-3"),
    );
    expect(drawer).toHaveTextContent("Not a language your rules keep");
    expect(
      within(drawer).getByRole("button", { name: "Clean this file" }),
    ).toBeEnabled();
  });

  it("previews a file with the profile the library cleans by, or its workflow's until it has one", async () => {
    librarySettingsResult = settings({ library_rule_set_id: 7 });
    const { unmount } = renderLibrary();
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    await waitFor(() =>
      expect(previewRequests.at(-1)).toMatchObject({ ruleSetId: 7 }),
    );
    unmount();

    librarySettingsResult = settings({ library_rule_set_id: null });
    renderLibrary();
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    await waitFor(() =>
      expect(previewRequests.at(-1)).toMatchObject({ ruleSetId: undefined }),
    );
  });

  it("says removed tracks are gone for good, or recoverable when the library keeps originals", async () => {
    renderLibrary();
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    const drawer = await screen.findByTestId("library-file-drawer");
    expect(drawer).toHaveTextContent("Removed tracks are gone for good");

    fireEvent.click(within(drawer).getByRole("button", { name: "Close" }));
    librarySettingsResult = settings({
      keep_original_after_clean: true,
      originals_folder: "D:\\Media\\Movies\\.weir-originals",
    });
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    const reopened = await screen.findByTestId("library-file-drawer");
    expect(reopened).toHaveTextContent(
      "Removed tracks are recoverable: Weir keeps the original in D:\\Media\\Movies\\.weir-originals.",
    );
  });

  it("lets you pick the tracks for one file yourself, and sends exactly those", async () => {
    // Single-file clean asks the same confirmation as bulk clean (#690): the server is asked what the
    // clean would remove before anything runs, and the actual clean only follows once that is confirmed.
    clean.mockImplementation(
      (
        vars: { paths: string[]; confirm: boolean; manual?: unknown },
        options?: { onSuccess?: (result: unknown) => void },
      ) => {
        if (!vars.confirm) {
          options?.onSuccess?.({
            kind: "confirmation_required",
            detail: "",
            files_count: vars.paths.length,
            tracks_count: 1,
            estimated_bytes_saved: 0,
          });
        }
      },
    );
    renderLibrary();
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    const drawer = await screen.findByTestId("library-file-drawer");
    await waitFor(() =>
      expect(drawer).toHaveTextContent("Audio 1 · English · 5.1 E-AC-3"),
    );

    fireEvent.click(
      within(drawer).getByRole("button", { name: "Choose tracks" }),
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

    const confirm = await screen.findByTestId("library-confirm");
    fireEvent.click(within(confirm).getByTestId("library-confirm-confirm"));

    expect(clean).toHaveBeenCalledTimes(2);
    const [, [sent]] = clean.mock.calls as [
      unknown,
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
      expect(drawer).toHaveTextContent("Audio 1 · English · 5.1 E-AC-3"),
    );
    fireEvent.click(
      within(drawer).getByRole("button", { name: "Choose tracks" }),
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

  it("leaves a file alone, and says so", async () => {
    renderLibrary();
    fireEvent.click(
      screen.getByRole("button", { name: "The.Quiet.Harbour.S01E01.mkv" }),
    );
    const drawer = await screen.findByTestId("library-file-drawer");

    fireEvent.click(
      within(drawer).getByRole("checkbox", { name: /Left alone/ }),
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
    expect(said).toHaveTextContent(
      "Weir skipped 1 that is still seeding: Northbound.S01E01.mkv",
    );
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

  it("shows a library with no folders a step to set it up, not a link back to Settings", () => {
    signedInRole = "operator";
    librarySettingsResult = settings({ library_folders: [] });

    renderLibrary();

    const prompt = screen.getByTestId("library-setup-prompt");
    expect(prompt).toHaveTextContent("Set up this library");
    expect(screen.queryByTestId("library-empty")).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();

    fireEvent.click(
      within(prompt).getByRole("button", { name: "Set up this library" }),
    );
    expect(screen.getByTestId("library-setup-panel")).toBeInTheDocument();
    expect(screen.getByLabelText("Folder to add")).toBeInTheDocument();
  });

  it("tells a viewer that an operator or admin sets a library up", () => {
    librarySettingsResult = settings({ library_folders: [] });

    renderLibrary();

    expect(screen.getByTestId("library-setup-prompt")).toHaveTextContent(
      "An operator or an admin can set this library up.",
    );
    expect(
      screen.queryByRole("button", { name: "Set up this library" }),
    ).not.toBeInTheDocument();
  });

  it("opens the setup from the header once a library has folders", () => {
    signedInRole = "operator";

    renderLibrary();
    fireEvent.click(screen.getByRole("button", { name: "Library setup" }));

    const panel = screen.getByTestId("library-setup-panel");
    expect(within(panel).getByText("D:/tv")).toBeInTheDocument();
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

  it("opens the library the address names", () => {
    renderLibrary("/library?library=2");

    expect(screen.getByTestId("library-picker")).toHaveTextContent("Movies");
  });

  it("opens the library last picked here when the address names none", () => {
    const first = renderLibrary();
    fireEvent.click(
      within(screen.getByTestId("library-picker")).getByRole("button", {
        name: /TV/,
      }),
    );
    fireEvent.click(screen.getByRole("option", { name: /Movies/ }));
    first.unmount();

    renderLibrary();

    expect(screen.getByTestId("library-picker")).toHaveTextContent("Movies");
  });

  it("prefers the library in the address to the one last picked", () => {
    localStorage.setItem("weir-library-last", "2");

    renderLibrary("/library?library=1");

    expect(screen.getByTestId("library-picker")).toHaveTextContent("TV");
  });
});
