import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
  ProcessingFile,
  ProcessingFileLog,
} from "../../lib/processing/files-api";
import type { LibraryClean } from "../../lib/processing/library-cleans-api";
import { HistoryPage } from "./history-page";

const files: {
  files: ProcessingFile[];
  status_counts: Record<string, number>;
} = { files: [], status_counts: {} };
const cleans: { cleans: LibraryClean[] } = { cleans: [] };
const filesQueryState: {
  isLoading: boolean;
  isError: boolean;
  error: unknown;
} = { isLoading: false, isError: false, error: null };
const requeue = vi.fn();
const requeueFiles = vi.fn();
const processNow = vi.fn();
const fetchLog = vi.fn<(id: number) => Promise<ProcessingFileLog>>();

vi.mock("../../lib/processing/files-queries", async (importOriginal) => {
  const mutation = (fn = vi.fn()) => ({
    mutateAsync: fn,
    mutate: (
      input: unknown,
      opts?: { onSuccess?: (r: unknown) => void; onError?: () => void },
    ) => void Promise.resolve(fn(input)).then(opts?.onSuccess, opts?.onError),
    isPending: false,
  });
  return {
    ...(await importOriginal<
      typeof import("../../lib/processing/files-queries")
    >()),
    useFileHistoryQuery: () => ({
      data: files,
      isLoading: filesQueryState.isLoading,
      isError: filesQueryState.isError,
      error: filesQueryState.error,
      refetch: vi.fn(),
    }),
    useLibraryCleansQuery: () => ({
      data: {
        cleans: cleans.cleans,
        returned: cleans.cleans.length,
        limit: 200,
      },
      isLoading: false,
      isError: false,
    }),
    useRequeueProcessingFiles: () => mutation(requeueFiles),
    useRequeueProcessingFile: () => mutation(requeue),
    useForgetProcessingFile: () => mutation(),
    useProcessingFileRemoveOptions: () => mutation(),
    useMoveProcessingFileToTop: () => mutation(),
    useProcessingWhyHeld: () => mutation(),
    useProcessProcessingFileNow: () => mutation(processNow),
    useProcessingCheckLibraryAgain: () => mutation(),
    useProcessingFileTracks: () => mutation(),
    useSubmitProcessingManualPlan: () => mutation(),
  };
});
vi.mock("../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({
    data: [{ id: 1, name: "TV", media_type: "tv" }],
  }),
}));
vi.mock("../../lib/auth/queries", () => ({
  useMeQuery: () => ({ data: { role: "admin" } }),
}));
vi.mock("../../lib/pause/pause-queries", () => ({
  usePauseQuery: () => ({ data: { paused: false } }),
}));
vi.mock("../../components/shell/page-header", () => ({
  PageHeader: ({ title }: { title: ReactNode }) => <h1>{title}</h1>,
}));
vi.mock("../../lib/processing/files-api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../lib/processing/files-api")>()),
  fetchProcessingFileLog: (id: number) => fetchLog(id),
}));

function file(partial: Partial<ProcessingFile>): ProcessingFile {
  return {
    id: 1,
    library_id: 1,
    library_name: "TV",
    relative_path: "Northbound/Northbound.S08E09.mkv",
    status: "processed",
    status_reason: "Finished processing this file.",
    blocked_by_connection: null,
    size_bytes: 4_000_000_000,
    failure_class: null,
    failure_attempts: 0,
    next_retry_at: null,
    output_collision_policy: null,
    output_collision_action: null,
    output_collision_reason: null,
    video_width: null,
    video_height: null,
    video_codec: null,
    audio_track_count: null,
    subtitle_track_count: null,
    duration_seconds: null,
    direct_play: [],
    progress_percent: null,
    progress_message: null,
    progress_eta_seconds: null,
    hold_until: null,
    size_changed_at: null,
    created_at: "2026-08-19T04:00:00",
    updated_at: "2026-08-19T04:00:00",
    last_seen_at: null,
    last_attempt_at: null,
    ...partial,
  };
}

function renderPage(entry = "/history") {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[entry]}>
        <HistoryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("HistoryPage", () => {
  beforeEach(() => {
    requeue.mockReset();
    requeueFiles.mockReset();
    requeueFiles.mockResolvedValue({
      requeued: 2,
      skipped: 0,
      detail: "Queued 2 files again.",
    });
    processNow.mockReset();
    fetchLog.mockReset();
    cleans.cleans = [];
    filesQueryState.isLoading = false;
    filesQueryState.isError = false;
    filesQueryState.error = null;
    files.files = [
      file({ id: 1 }),
      file({
        id: 2,
        relative_path: "Glass Orchard/Glass.Orchard.S03E03.mkv",
        status: "processing_failed",
        status_reason: "The download ended early.",
        updated_at: "2026-08-19T03:00:00",
      }),
    ];
  });

  it("says its file history could not load, through the shared load-error wording", () => {
    filesQueryState.isError = true;
    filesQueryState.error = new Error("boom");
    renderPage();

    expect(
      screen.getByText(
        "Weir couldn't load your file history. Reload the page to try again.",
      ),
    ).toBeInTheDocument();
  });

  it("counts every file under the chip it belongs to", () => {
    fetchLog.mockResolvedValue({
      file_id: 1,
      relative_path: "",
      retention_days: 90,
      entries: [],
    });
    renderPage();
    const chips = screen.getByRole("group", { name: "Show" });
    expect(
      within(chips).getByRole("button", { name: /All\s*2/ }),
    ).toHaveAttribute("aria-pressed", "true");
    expect(
      within(chips).getByRole("button", { name: /Finished\s*1/ }),
    ).toBeInTheDocument();
    expect(
      within(chips).getByRole("button", { name: /Failed\s*1/ }),
    ).toBeInTheDocument();
  });

  it("shows what the open file kept and removed, and why", async () => {
    fetchLog.mockResolvedValue({
      file_id: 1,
      relative_path: "Northbound/Northbound.S08E09.mkv",
      retention_days: 90,
      entries: [
        {
          id: 9,
          recorded_at: "2026-08-19T04:00:00",
          outcome: "live_output_written",
          title: "",
          library_name: "TV",
          detail: {
            outcome: "live_output_written",
            audio_after: "English 5.1 E-AC-3",
            removed_audio: [
              "English 2.0 (commentary excluded — remove commentary enabled)",
            ],
            subs_after: "English",
            removed_subtitles: ["spa"],
            source_size_bytes: 4_000_000_000,
            output_size_bytes: 3_500_000_000,
          },
          story: [
            {
              heading: "Handed back",
              sentence: "Written for Sonarr.",
              tone: "good",
            },
          ],
        },
      ],
    });
    renderPage("/history?file=1");
    const detail = await screen.findByTestId("history-detail");
    const trackset = await within(detail).findByTestId("history-tracks");
    expect(within(trackset).getByText("2 kept")).toBeInTheDocument();
    expect(within(trackset).getByText("2 removed")).toBeInTheDocument();
    expect(within(trackset).getAllByText("Removed")).toHaveLength(2);
    expect(
      within(detail).getByText(
        "Commentary excluded — remove commentary enabled",
      ),
    ).toBeInTheDocument();
    expect(within(detail).getByText("Spanish")).toBeInTheDocument();
    expect(within(detail).getByText("477 MB")).toBeInTheDocument();
    expect(within(detail).getByText("Handed back")).toBeInTheDocument();
  });

  it("says a file's record could not load, through the shared load-error wording", async () => {
    fetchLog.mockRejectedValue(new Error("boom"));
    renderPage("/history?file=1");
    const detail = await screen.findByTestId("history-detail");

    expect(
      await within(detail).findByText(
        "Weir couldn't load this file's record. Reload the page to try again.",
      ),
    ).toBeInTheDocument();
  });

  it("offers Try again on a failed file and says what the server answered", async () => {
    fetchLog.mockResolvedValue({
      file_id: 2,
      relative_path: "",
      retention_days: 90,
      entries: [],
    });
    requeue.mockResolvedValue({ detail: "Queued again." });
    renderPage("/history?file=2");
    const detail = screen.getByTestId("history-detail");
    expect(
      within(detail).getByText("This attempt failed."),
    ).toBeInTheDocument();
    fireEvent.click(within(detail).getByRole("button", { name: "Try again" }));
    expect(
      await within(detail).findByText("Queued again."),
    ).toBeInTheDocument();
    expect(requeue).toHaveBeenCalledWith(2);
  });

  it("asks before passing a file through unchanged, and does nothing when told not now", async () => {
    fetchLog.mockResolvedValue({
      file_id: 2,
      relative_path: "",
      retention_days: 90,
      entries: [],
    });
    processNow.mockResolvedValue({});
    renderPage("/history?file=2");
    const detail = screen.getByTestId("history-detail");
    const passThrough = within(detail).getByRole("button", {
      name: "Pass through unchanged",
    });

    fireEvent.click(passThrough);
    fireEvent.click(screen.getByRole("button", { name: "Not now" }));
    expect(processNow).not.toHaveBeenCalled();

    fireEvent.click(passThrough);
    const dialog = screen.getByRole("dialog", {
      name: "Pass this file through unchanged?",
    });
    fireEvent.click(
      within(dialog).getByRole("button", { name: "Pass through unchanged" }),
    );

    expect(
      await within(detail).findByText(
        "Queued to pass through unchanged. Weir checks the copy before removing the original.",
      ),
    ).toBeInTheDocument();
    expect(processNow).toHaveBeenCalledWith(
      expect.objectContaining({ library_id: 1, pass_through_unchanged: true }),
    );
  });

  it("offers Process again on a file Weir is done with", async () => {
    files.files = [file({ id: 1, status: "cancelled" })];
    fetchLog.mockResolvedValue({
      file_id: 1,
      relative_path: "",
      retention_days: 90,
      entries: [],
    });
    requeue.mockResolvedValue({ detail: "Queued 1 file again." });
    renderPage("/history?file=1");
    const detail = screen.getByTestId("history-detail");

    fireEvent.click(
      within(detail).getByRole("button", { name: "Process again" }),
    );

    expect(
      await within(detail).findByText("Queued 1 file again."),
    ).toBeInTheDocument();
    expect(requeue).toHaveBeenCalledWith(1);
  });

  it("acts on exactly the failed files shown, not everything matching the filter", () => {
    files.files = [
      file({ id: 1, status: "processing_failed" }),
      file({ id: 2, status: "processing_failed" }),
      file({ id: 3, status: "skipped" }),
    ];
    renderPage("/history?show=failed");

    const retryAll = screen.getByRole("button", {
      name: "Try the 2 failed files again",
    });
    fireEvent.click(retryAll);

    expect(requeueFiles).toHaveBeenCalledWith({ file_ids: [1, 2] });
  });

  it("keeps a skip out of Failed and gives it its own neutral group", () => {
    files.files = [file({ id: 1, status: "skipped" })];
    renderPage();

    const chips = screen.getByRole("group", { name: "Show" });
    expect(
      within(chips).getByRole("button", { name: /Skipped\s*1/ }),
    ).toBeInTheDocument();
    expect(
      within(chips).getByRole("button", { name: /Failed\s*0/ }),
    ).toBeInTheDocument();
  });

  it("lists a library clean alongside downloads, as its own kind of entry", () => {
    files.files = [];
    cleans.cleans = [
      {
        kind: "library_clean",
        id: 5,
        library_id: 2,
        library_name: "Films",
        relative_path: "Heat (1995)/Heat.mkv",
        outcome: "cleaned",
        detail: "Cleaned Heat.mkv: removed 1 audio track.",
        trigger: null,
        recorded_at: "2026-08-19T04:00:00",
      },
    ];
    renderPage();

    expect(screen.getAllByText("Heat.mkv").length).toBeGreaterThan(0);
  });
});
