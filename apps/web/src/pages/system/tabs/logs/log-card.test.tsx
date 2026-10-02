import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import {
  afterEach,
  beforeAll,
  beforeEach,
  describe,
  expect,
  it,
  vi,
} from "vitest";

import { systemKeys } from "../../../../lib/system/query-keys";
import {
  EVENT_ROW,
  JOB_ROW,
  PENDING_JOB_ROW,
  SERVER_ROW,
  logPage,
  renderLog,
  stubEventSource,
} from "./log-test-support";

const mocks = vi.hoisted(() => ({
  fetchSystemLog: vi.fn(),
  fetchSystemLogExport: vi.fn(),
  fetchServerLogDownload: vi.fn(),
  fetchOperationalHistoryPreview: vi.fn(),
  resetOperationalHistory: vi.fn(),
  postCancel: vi.fn(),
  postRecover: vi.fn(),
  saveBlobAs: vi.fn(),
  role: { current: "operator" as string },
}));

vi.mock("../../../../lib/system/system-log-api", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../../lib/system/system-log-api")
  >()),
  fetchSystemLog: mocks.fetchSystemLog,
  fetchSystemLogExport: mocks.fetchSystemLogExport,
}));

vi.mock("../../../../lib/settings/queries", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../../lib/settings/queries")
  >()),
  useAppSettingsQuery: () => ({ data: { app_timezone: "UTC" } }),
}));

vi.mock("../../../../lib/settings/settings-api", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../../lib/settings/settings-api")
  >()),
  fetchServerLogDownload: mocks.fetchServerLogDownload,
  fetchOperationalHistoryPreview: mocks.fetchOperationalHistoryPreview,
  resetOperationalHistory: mocks.resetOperationalHistory,
}));

vi.mock(
  "../../../../lib/processing/jobs-inspection/api",
  async (importOriginal) => ({
    ...(await importOriginal<
      typeof import("../../../../lib/processing/jobs-inspection/api")
    >()),
    postProcessingJobCancelPending: mocks.postCancel,
    postProcessingJobRecoverFinalizeFailed: mocks.postRecover,
  }),
);

vi.mock("../../../../lib/ui/save-file", () => ({
  saveBlobAs: mocks.saveBlobAs,
}));

vi.mock("../../../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({ data: [{ id: 1, name: "Movies" }] }),
}));

vi.mock("../../../../lib/auth/queries", () => ({
  useMeQuery: () => ({
    data: { id: 1, username: "alice", role: mocks.role.current },
  }),
}));

vi.mock("../../../../lib/pause/pause-queries", () => ({
  usePauseQuery: () => ({ data: { paused: false } }),
}));

beforeAll(stubEventSource);

beforeEach(() => {
  mocks.role.current = "operator";
  mocks.fetchSystemLog.mockReset();
  mocks.fetchSystemLog.mockResolvedValue(
    logPage([EVENT_ROW, SERVER_ROW, JOB_ROW]),
  );
});

afterEach(() => {
  vi.clearAllMocks();
});

async function rendered() {
  const view = renderLog();
  await screen.findAllByTestId("log-row");
  return view;
}

/** Opens the row for the thing with this title. */
function open(title: string) {
  const row = screen.getByText(title).closest("li") as HTMLElement;
  fireEvent.click(within(row).getByRole("button", { expanded: false }));
  return row;
}

describe("an open row", () => {
  it("says what an event was, why it happened and how it went", async () => {
    await rendered();

    const row = open("Sign-in finished");

    expect(within(row).getByText("You started this")).toBeInTheDocument();
    expect(within(row).getByText("Finished")).toBeInTheDocument();
    expect(within(row).getByText("auth.login_succeeded")).toBeInTheDocument();
    expect(
      within(row).getByRole("button", { expanded: true }),
    ).toBeInTheDocument();
  });

  it("says what a job was, how often it was tried, what it was given and why it stopped", async () => {
    await rendered();

    const row = open("Couldn't finish this job for heat.mkv");

    expect(
      within(row).getByText("Couldn't finish this job for heat.mkv."),
    ).toBeInTheDocument();
    expect(
      within(row).getByText(/See Files or Jobs for why/),
    ).toBeInTheDocument();
    expect(within(row).getByText("#7")).toBeInTheDocument();
    expect(within(row).getByText("3 of 3")).toBeInTheDocument();
    expect(within(row).getByText("remux:1:heat")).toBeInTheDocument();
    expect(
      within(row).getByText("ffmpeg stopped unexpectedly"),
    ).toBeInTheDocument();
    expect(
      within(row).getByText(/"relative_media_path": "Heat \(1995\)\/heat.mkv"/),
    ).toBeInTheDocument();
  });

  it("links a job's file to Activity", async () => {
    await rendered();

    const row = open("Couldn't finish this job for heat.mkv");

    expect(
      within(row).getByRole("link", { name: "Open the file in Activity →" }),
    ).toHaveAttribute("href", "/activity?q=heat.mkv&within=all&library=1");
  });

  it("says where a server line came from, and shows its exception", async () => {
    await rendered();

    const row = open("The backup folder is nearly full");

    expect(
      within(row).getByText("weir.platform.suite_settings.backups"),
    ).toBeInTheDocument();
    expect(within(row).getByText("req-1")).toBeInTheDocument();
    expect(
      within(row).getByText(/Not enough space\s+at Backup.Write\(\)/),
    ).toBeInTheDocument();
  });

  it("opens one row at a time", async () => {
    await rendered();

    const first = open("Sign-in finished");
    open("The backup folder is nearly full");

    expect(
      within(first).getByRole("button", { expanded: false }),
    ).toBeInTheDocument();
  });

  it("narrows the log to one job from the job, the server line that names it, and back", async () => {
    await rendered();
    open("Couldn't finish this job for heat.mkv");

    fireEvent.click(
      screen.getByRole("button", { name: "Everything about this job →" }),
    );

    await waitFor(() =>
      expect(mocks.fetchSystemLog).toHaveBeenLastCalledWith(
        expect.objectContaining({ job: 7 }),
      ),
    );
    expect(screen.getByTestId("logs-job-filter")).toHaveTextContent(
      "Everything about job #7",
    );
    fireEvent.click(screen.getByRole("button", { name: "Show all" }));
    await waitFor(() =>
      expect(screen.queryByTestId("logs-job-filter")).not.toBeInTheDocument(),
    );
  });

  it("offers the job a server line names", async () => {
    await rendered();

    const row = open("The backup folder is nearly full");

    expect(
      within(row).getByRole("button", { name: "Everything about this job →" }),
    ).toBeInTheDocument();
  });
});

describe("a job's actions", () => {
  beforeEach(() => {
    mocks.fetchSystemLog.mockResolvedValue(
      logPage([
        PENDING_JOB_ROW,
        {
          ...JOB_ROW,
          id: "job:9",
          level: "warning",
          title:
            "The work finished for heat.mkv, but its result could not be saved",
          job: { ...JOB_ROW.job!, id: 9, status: "handler_ok_finalize_failed" },
        },
        JOB_ROW,
      ]),
    );
  });

  it("can take a queued job back, and reads the log again afterwards", async () => {
    mocks.postCancel.mockResolvedValue({
      ok: true,
      job_id: 8,
      status: "cancelled",
    });
    await rendered();
    const row = open("Queued for alien.mkv");

    fireEvent.click(
      within(row).getByRole("button", { name: "Cancel pending" }),
    );

    await waitFor(() => expect(mocks.postCancel).toHaveBeenCalledWith(8));
    await waitFor(() =>
      expect(mocks.fetchSystemLog.mock.calls.length).toBeGreaterThan(1),
    );
  });

  it("can save the result of a job that finished its work but could not record it", async () => {
    mocks.postRecover.mockResolvedValue({
      ok: true,
      job_id: 9,
      status: "completed",
    });
    await rendered();
    const row = open(
      "The work finished for heat.mkv, but its result could not be saved",
    );

    fireEvent.click(
      within(row).getByRole("button", { name: "Recover result" }),
    );

    await waitFor(() => expect(mocks.postRecover).toHaveBeenCalledWith(9));
  });

  it("offers neither on a job that failed", async () => {
    await rendered();

    const failed = open("Couldn't finish this job for heat.mkv");

    expect(
      within(failed).queryByRole("button", { name: /Cancel|Recover/ }),
    ).not.toBeInTheDocument();
  });

  it("is not offered to a viewer", async () => {
    mocks.role.current = "viewer";
    await rendered();

    const row = open("Queued for alien.mkv");

    expect(
      within(row).queryByRole("button", { name: "Cancel pending" }),
    ).not.toBeInTheDocument();
  });

  it("says why a cancel did not work", async () => {
    mocks.postCancel.mockRejectedValue(
      new Error("Only pending jobs can be cancelled."),
    );
    await rendered();
    const row = open("Queued for alien.mkv");

    fireEvent.click(
      within(row).getByRole("button", { name: "Cancel pending" }),
    );

    expect(await within(row).findByRole("alert")).toHaveTextContent(/cancel/i);
  });
});

describe("older entries", () => {
  it("are read a page at a time from where the last page ended, and added under it", async () => {
    mocks.fetchSystemLog
      .mockResolvedValueOnce(
        logPage([EVENT_ROW], { next_cursor: "page-two", total: 2 }),
      )
      .mockResolvedValueOnce(logPage([JOB_ROW]));
    renderLog();
    await screen.findByText("Sign-in finished");

    fireEvent.click(
      screen.getByRole("button", { name: "Load older entries →" }),
    );

    expect(
      await screen.findByText("Couldn't finish this job for heat.mkv"),
    ).toBeInTheDocument();
    expect(mocks.fetchSystemLog).toHaveBeenLastCalledWith(
      expect.objectContaining({ cursor: "page-two", limit: 50 }),
    );
    expect(
      screen.queryByRole("button", { name: "Load older entries →" }),
    ).not.toBeInTheDocument();
    expect(screen.getAllByTestId("log-row")).toHaveLength(2);
  });

  it("say what went wrong when a page cannot be read, and can be tried again", async () => {
    mocks.fetchSystemLog
      .mockResolvedValueOnce(
        logPage([EVENT_ROW], { next_cursor: "page-two", total: 2 }),
      )
      .mockRejectedValueOnce(new Error("down"))
      .mockResolvedValueOnce(logPage([JOB_ROW]));
    renderLog();
    await screen.findByText("Sign-in finished");

    fireEvent.click(
      screen.getByRole("button", { name: "Load older entries →" }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(
      /older entries/i,
    );

    fireEvent.click(
      screen.getByRole("button", { name: "Load older entries →" }),
    );
    expect(
      await screen.findByText("Couldn't finish this job for heat.mkv"),
    ).toBeInTheDocument();
  });
});

describe("new entries", () => {
  it("wait behind a count while a row is open, and join the list when asked for", async () => {
    const { client } = await rendered();
    open("Sign-in finished");
    const arrived = {
      ...SERVER_ROW,
      id: "server:999",
      at: "2026-10-02T11:59:00Z",
      title: "A new warning",
      server: { ...SERVER_ROW.server!, message: "A new warning" },
    };
    mocks.fetchSystemLog.mockResolvedValue(
      logPage([arrived, EVENT_ROW, SERVER_ROW, JOB_ROW]),
    );

    await act(async () => {
      await client.invalidateQueries({ queryKey: systemKeys.logEntriesAll });
    });

    expect(
      await screen.findByRole("button", { name: "1 new entry — show" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("A new warning")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "1 new entry — show" }));
    expect(await screen.findByText("A new warning")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /new entr/ }),
    ).not.toBeInTheDocument();
  });

  it("join the list at once when nothing is open", async () => {
    const { client } = await rendered();
    const arrived = {
      ...SERVER_ROW,
      id: "server:999",
      title: "A new warning",
      server: SERVER_ROW.server,
    };
    mocks.fetchSystemLog.mockResolvedValue(
      logPage([arrived, EVENT_ROW, SERVER_ROW, JOB_ROW]),
    );

    await act(async () => {
      await client.invalidateQueries({ queryKey: systemKeys.logEntriesAll });
    });

    expect(await screen.findByText("A new warning")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /new entr/ }),
    ).not.toBeInTheDocument();
  });
});

describe("export", () => {
  it("hands over the rows the filters show as a spreadsheet or as JSON", async () => {
    mocks.fetchSystemLogExport.mockResolvedValue({
      blob: new Blob(["time\n"]),
      filename: "weir-log.csv",
    });
    renderLog("/system?tab=logs&level=error&source=job");
    await screen.findAllByTestId("log-row");

    fireEvent.click(screen.getByTestId("logs-export"));
    fireEvent.click(screen.getByRole("menuitem", { name: /Spreadsheet/ }));

    await waitFor(() =>
      expect(mocks.fetchSystemLogExport).toHaveBeenCalledWith(
        "csv",
        expect.objectContaining({ level: ["error"], source: ["job"] }),
      ),
    );
    expect(mocks.fetchSystemLogExport.mock.calls[0][1]).not.toHaveProperty(
      "limit",
    );
    expect(mocks.saveBlobAs).toHaveBeenCalledWith(
      expect.any(Blob),
      "weir-log.csv",
    );

    fireEvent.click(screen.getByTestId("logs-export"));
    fireEvent.click(screen.getByRole("menuitem", { name: /JSON/ }));
    await waitFor(() =>
      expect(mocks.fetchSystemLogExport).toHaveBeenCalledWith(
        "json",
        expect.anything(),
      ),
    );
  });

  it("hands over the whole server log as the file it is", async () => {
    mocks.fetchServerLogDownload.mockResolvedValue(new Blob(["log"]));
    await rendered();

    fireEvent.click(screen.getByTestId("logs-export"));
    fireEvent.click(screen.getByRole("menuitem", { name: /Whole server log/ }));

    await waitFor(() =>
      expect(mocks.fetchServerLogDownload).toHaveBeenCalled(),
    );
    expect(mocks.saveBlobAs).toHaveBeenCalledWith(
      expect.any(Blob),
      expect.stringMatching(/^weir-log-.*\.log$/),
    );
  });

  it("says what went wrong when it cannot", async () => {
    mocks.fetchServerLogDownload.mockRejectedValue(new Error("no file"));
    await rendered();

    fireEvent.click(screen.getByTestId("logs-export"));
    fireEvent.click(screen.getByRole("menuitem", { name: /Whole server log/ }));

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });
});

describe("Clear events", () => {
  const counts = {
    status: "preview",
    activity_events_deleted: 12,
    jobs_deleted: 4,
    total_deleted: 16,
  };

  it("lists what it would remove and works only once RESET is typed", async () => {
    mocks.fetchOperationalHistoryPreview.mockResolvedValue(counts);
    mocks.resetOperationalHistory.mockResolvedValue({
      ...counts,
      status: "reset",
    });
    await rendered();

    fireEvent.click(screen.getByRole("button", { name: "Clear events…" }));
    const dialog = await screen.findByTestId("logs-clear-dialog");
    expect(dialog).toHaveTextContent("12 Activity events");
    expect(dialog).toHaveTextContent("4 finished jobs");
    expect(dialog).toHaveTextContent("Queued and running work is kept");
    const confirm = within(dialog).getByRole("button", {
      name: "Clear events",
    });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByRole("textbox"), {
      target: { value: "reset please" },
    });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByRole("textbox"), {
      target: { value: "RESET" },
    });
    fireEvent.click(confirm);

    await waitFor(() =>
      expect(mocks.resetOperationalHistory).toHaveBeenCalledWith("RESET"),
    );
    expect(
      await screen.findByText(
        "Events cleared. Removed 12 Activity events and 4 finished jobs. No media file was touched.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("logs-clear-dialog")).not.toBeInTheDocument();
    await waitFor(() =>
      expect(mocks.fetchSystemLog.mock.calls.length).toBeGreaterThan(1),
    );
  });

  it("closes on Escape, removing nothing", async () => {
    mocks.fetchOperationalHistoryPreview.mockResolvedValue(counts);
    await rendered();

    fireEvent.click(screen.getByRole("button", { name: "Clear events…" }));
    const dialog = await screen.findByTestId("logs-clear-dialog");
    await waitFor(() =>
      expect(dialog).toContainElement(document.activeElement as HTMLElement),
    );
    fireEvent.keyDown(document, { key: "Escape" });

    expect(screen.queryByTestId("logs-clear-dialog")).not.toBeInTheDocument();
    expect(mocks.resetOperationalHistory).not.toHaveBeenCalled();
  });

  it("is not offered to a viewer", async () => {
    mocks.role.current = "viewer";
    await rendered();

    expect(
      screen.queryByRole("button", { name: "Clear events…" }),
    ).not.toBeInTheDocument();
  });
});

describe("when the log cannot be read", () => {
  it("says so rather than showing an empty list", async () => {
    mocks.fetchSystemLog.mockRejectedValue(new Error("boom"));
    renderLog();

    expect(await screen.findByRole("alert")).toHaveTextContent(/log/i);
    expect(
      screen.queryByText("Nothing has been logged yet."),
    ).not.toBeInTheDocument();
  });
});
