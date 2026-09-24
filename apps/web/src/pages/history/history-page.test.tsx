import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
  ProcessingFile,
  ProcessingFileLog,
} from "../../lib/processing/files-api";
import { HistoryPage } from "./history-page";

const files: {
  files: ProcessingFile[];
  status_counts: Record<string, number>;
} = { files: [], status_counts: {} };
const requeue = vi.fn();
const processNow = vi.fn();
const fetchLog = vi.fn<(id: number) => Promise<ProcessingFileLog>>();

vi.mock("../../lib/processing/files-queries", async (importOriginal) => {
  const mutation = (fn = vi.fn()) => ({
    mutateAsync: fn,
    isPending: false,
  });
  return {
    ...(await importOriginal<
      typeof import("../../lib/processing/files-queries")
    >()),
    useProcessingFilesQuery: () => ({
      data: files,
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
    useRequeueProcessingFiles: () => mutation(),
    useRequeueProcessingFile: () => mutation(requeue),
    useForgetProcessingFile: () => mutation(),
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
    created_at: "2026-09-23T04:00:00",
    updated_at: "2026-09-23T04:00:00",
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
    processNow.mockReset();
    fetchLog.mockReset();
    files.files = [
      file({ id: 1 }),
      file({
        id: 2,
        relative_path: "Glass Orchard/Glass.Orchard.S03E03.mkv",
        status: "processing_failed",
        status_reason: "The download ended early.",
        updated_at: "2026-09-23T03:00:00",
      }),
    ];
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
          recorded_at: "2026-09-23T04:00:00",
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
});
