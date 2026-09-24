import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import {
  afterEach,
  beforeAll,
  beforeEach,
  describe,
  expect,
  it,
  vi,
} from "vitest";
import type { ActivityEventItem } from "../../../../lib/api/types";
import { ActivityLog } from "./activity-log";

const mocks = vi.hoisted(() => ({
  useActivityRecentQuery: vi.fn(),
  role: { current: "operator" as string },
  fetchActivityExport: vi.fn(),
  fetchOperationalHistoryPreview: vi.fn(),
  resetOperationalHistory: vi.fn(),
}));

vi.mock("../../../../lib/activity/queries", () => ({
  useActivityRecentQuery: (...args: unknown[]) =>
    mocks.useActivityRecentQuery(...args),
}));

vi.mock("../../../../lib/activity/use-activity-stream-invalidation", () => ({
  useActivityStreamInvalidations: vi.fn(),
}));

vi.mock("../../../../lib/ui/mm-format-date", () => ({
  useAppDateFormatter: () => (iso: string) => iso,
}));

vi.mock("../../../../lib/auth/queries", () => ({
  useMeQuery: () => ({
    data: { id: 1, username: "alice", role: mocks.role.current },
  }),
}));

vi.mock("../../../../lib/api/activity-api", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../../lib/api/activity-api")
  >()),
  fetchActivityExport: mocks.fetchActivityExport,
}));

vi.mock("../../../../lib/settings/settings-api", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../../lib/settings/settings-api")
  >()),
  fetchOperationalHistoryPreview: mocks.fetchOperationalHistoryPreview,
  resetOperationalHistory: mocks.resetOperationalHistory,
}));

beforeAll(() => {
  class EventSourceStub {
    addEventListener = vi.fn();
    removeEventListener = vi.fn();
    close = vi.fn();
  }
  vi.stubGlobal("EventSource", EventSourceStub);
});

function event(
  overrides: Partial<ActivityEventItem> & { id: number },
): ActivityEventItem {
  return {
    created_at: "2026-08-16T22:00:00Z",
    event_type: "processing.work_temp_stale_sweep_completed",
    module: "processing",
    title: "Temporary files cleanup finished",
    detail: null,
    trigger: null,
    result: null,
    library_id: null,
    relative_path: null,
    run_key: null,
    ...overrides,
  };
}

function recentResult(
  items: ActivityEventItem[],
  extra: Record<string, unknown> = {},
) {
  const data = {
    items,
    total: items.length,
    has_more: false,
    retention_days: 90,
    oldest_event_at: null,
    ...extra,
  };
  return {
    isPending: false,
    isError: false,
    data,
    refetch: vi.fn(async () => ({ data })),
  };
}

function renderLog() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const tree = () => (
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ActivityLog />
      </MemoryRouter>
    </QueryClientProvider>
  );
  const view = render(tree());
  return { ...view, rerenderLog: () => view.rerender(tree()) };
}

function lastQueryFilters(): Record<string, unknown> {
  return mocks.useActivityRecentQuery.mock.calls.at(-1)?.[0] as Record<
    string,
    unknown
  >;
}

const passEvent = event({
  id: 40,
  event_type: "processing.file_remux_pass_completed",
  title: "Movie.mkv was processed successfully",
  detail: JSON.stringify({ outcome: "ok", relative_media_path: "Movie.mkv" }),
  trigger: "webhook",
  result: "success",
});

describe("ActivityLog", () => {
  beforeEach(() => {
    mocks.role.current = "operator";
    Object.values(mocks).forEach((value) => {
      if (typeof value === "function" && "mockReset" in value)
        value.mockReset();
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    class EventSourceStub {
      addEventListener = vi.fn();
      removeEventListener = vi.fn();
      close = vi.fn();
    }
    vi.stubGlobal("EventSource", EventSourceStub);
  });

  it("renders one summary line and the filters", () => {
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([event({ id: 1, detail: '{"removed":0}' })]),
    );

    renderLog();

    expect(screen.getByTestId("activity-summary")).toHaveTextContent(
      "Showing 1 of 1 event · live",
    );
    expect(screen.getByDisplayValue("All events")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Any reason")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Any result")).toBeInTheDocument();
    expect(
      screen.getAllByText("Temporary files cleanup finished").length,
    ).toBeGreaterThan(0);
  });

  it("asks only for Weir's own events, with no library or file filter", () => {
    mocks.useActivityRecentQuery.mockReturnValue(recentResult([]));

    renderLog();

    expect(lastQueryFilters()).toMatchObject({ about: "weir" });
    expect(screen.queryByDisplayValue("All libraries")).not.toBeInTheDocument();
    expect(
      screen.queryByPlaceholderText("Part of a file path"),
    ).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue("All modules")).not.toBeInTheDocument();
    expect(lastQueryFilters()).not.toHaveProperty("module");
  });

  it("shows a proper empty state when no events match", () => {
    mocks.useActivityRecentQuery.mockReturnValue(recentResult([]));

    renderLog();

    fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));
    expect(
      screen.getByText("No activity matched the current filters."),
    ).toBeInTheDocument();
  });

  it("folds identical consecutive entries into one row with a count", () => {
    const signIn = (id: number) =>
      event({
        id,
        event_type: "auth.login_succeeded",
        module: "auth",
        title: "Sign-in finished",
        detail: "admin",
        trigger: "manual",
      });
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([signIn(9), signIn(8), signIn(7), event({ id: 6 })]),
    );

    renderLog();

    const rows = screen.getAllByTestId("activity-row");
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent("Sign-in finished");
    expect(
      within(rows[0]).getByTestId("activity-repeat-count"),
    ).toHaveTextContent("×3");
    expect(screen.queryByText("System event")).not.toBeInTheDocument();
  });

  it("renders explicit labels for system repair activity", () => {
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([
        event({
          id: 2,
          event_type: "system.reconciliation.repair",
          module: "system",
          title: "System repair action completed",
          detail: "remove_processing_temp_artifact: Removed the temp artifact.",
        }),
      ]),
    );

    renderLog();

    expect(
      screen.getByRole("heading", { name: "System repair finished" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "remove_processing_temp_artifact: Removed the temp artifact.",
      ),
    ).toBeInTheDocument();
  });

  it("shows a connection check's JSON detail as plain fields, never as raw JSON", () => {
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([
        event({
          id: 4,
          event_type: "arr_library.connection_test_failed",
          module: "arr_library",
          title: "Connection check failed",
          detail: JSON.stringify({
            manager_name: "Sonarr",
            reason: "Timed out",
          }),
        }),
      ]),
    );

    renderLog();

    expect(screen.queryByText(/"manager_name"/)).not.toBeInTheDocument();
    expect(screen.queryByText(/\{.*"reason".*\}/)).not.toBeInTheDocument();
    expect(screen.getByText("Manager name")).toBeInTheDocument();
    expect(screen.getByText("Sonarr")).toBeInTheDocument();
    expect(screen.getByText("Reason")).toBeInTheDocument();
    expect(screen.getByText("Timed out")).toBeInTheDocument();
  });

  it("shows a long file name in full, wrapped rather than cut mid-title", () => {
    const fileName =
      "An.Example.Feature.With.A.Very.Long.Release.Name.2024.UHD.BluRay.2160p.TrueHD.Atmos.EXAMPLE.mkv";
    const longTitle = `${fileName} was processed successfully`;
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([
        event({
          id: 3,
          event_type: "processing.file_remux_pass_completed",
          title: longTitle,
          detail: JSON.stringify({
            outcome: "ok",
            relative_media_path: fileName,
          }),
        }),
      ]),
    );

    renderLog();

    const heading = screen.getByTitle(longTitle);
    expect(heading.tagName).toBe("H2");
    expect(heading).toHaveClass("mm-activity-item__title");
    // The full name is present, not cut down to a head-and-tail summary.
    expect(heading.textContent).toBe(longTitle);
  });

  it("sends trigger and result filters to the server", () => {
    mocks.useActivityRecentQuery.mockReturnValue(recentResult([]));
    renderLog();

    fireEvent.change(screen.getByDisplayValue("Any reason"), {
      target: { value: "scheduled" },
    });
    fireEvent.change(screen.getByDisplayValue("Any result"), {
      target: { value: "failed" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    expect(lastQueryFilters()).toMatchObject({
      trigger: "scheduled",
      result: "failed",
      about: "weir",
    });
  });

  it("applies last night as yesterday 18:00 to today 08:00 in one click", () => {
    mocks.useActivityRecentQuery.mockReturnValue(recentResult([]));
    renderLog();

    fireEvent.click(screen.getByRole("button", { name: "Last night →" }));

    const filters = lastQueryFilters();
    const from = new Date(String(filters.date_from));
    const to = new Date(String(filters.date_to));
    expect(from.getHours()).toBe(18);
    expect(to.getHours()).toBe(8);
    expect(to.getTime() - from.getTime()).toBe(14 * 60 * 60 * 1000);
  });

  it("explains the trigger in plain words and shows nothing when it is not known", () => {
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([
        passEvent,
        event({ id: 41, trigger: null }),
        event({ id: 42, trigger: "folder_change" }),
      ]),
    );
    renderLog();

    const chips = screen.getAllByTestId("activity-trigger-chip");
    expect(chips.map((chip) => chip.textContent)).toEqual([
      "New file in watched folder",
      "From your media manager",
    ]);
  });

  it("collapses a run under one summary and expands to its entries", () => {
    const run = (
      id: number,
      path: string,
      detail: Record<string, unknown>,
      eventType = "processing.file_remux_pass_completed",
    ) =>
      event({
        id,
        event_type: eventType,
        title: `${path} finished`,
        detail: JSON.stringify(detail),
        relative_path: path,
        run_key: "run:7",
        trigger: "scheduled",
      });
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([
        run(20, "a.mkv", { outcome: "ok" }),
        run(19, "b.mkv", { outcome: "live_skipped_not_required" }),
        run(18, "c.mkv", {}, "processing.file_passed_through"),
        run(17, "d.mkv", { outcome: "ok" }),
        event({
          id: 10,
          event_type: "auth.login_failed",
          module: "auth",
          title: "Sign-in failed",
        }),
        event({
          id: 9,
          event_type: "auth.login_failed",
          module: "auth",
          title: "Sign-in failed",
        }),
      ]),
    );
    renderLog();

    const runGroup = screen.getByTestId("activity-run") as HTMLDetailsElement;
    expect(runGroup).toHaveTextContent(
      "Scheduled run · 4 files: 2 processed, 1 passed through, 1 no changes needed",
    );
    expect(runGroup.open).toBe(false);
    expect(within(runGroup).getAllByTestId("activity-row")).toHaveLength(4);
    fireEvent.click(runGroup.querySelector("summary")!);
    expect(runGroup.open).toBe(true);

    const failures = screen.getByTestId("activity-cluster");
    expect(failures).toHaveTextContent("2 repeated failures");
  });

  it("states how far back history goes, with the oldest entry", () => {
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([], {
        retention_days: 90,
        oldest_event_at: "2026-06-19T00:00:00Z",
      }),
    );
    renderLog();

    expect(screen.getByTestId("activity-retention")).toHaveTextContent(
      "History goes back 90 days (oldest entry 2026-06-19T00:00:00Z).",
    );
    expect(
      screen.getByRole("link", { name: "Change how long history is kept" }),
    ).toHaveAttribute("href", "/system?tab=history#activity-retention");
  });

  it("says history is kept until cleared when retention is 0", () => {
    mocks.useActivityRecentQuery.mockReturnValue(
      recentResult([], { retention_days: 0 }),
    );
    renderLog();

    expect(screen.getByTestId("activity-retention")).toHaveTextContent(
      "History is kept until you clear it.",
    );
  });

  it("exports the applied filters", async () => {
    mocks.useActivityRecentQuery.mockReturnValue(recentResult([]));
    mocks.fetchActivityExport.mockResolvedValue({
      blob: new Blob(["id\n"]),
      filename: "weir-activity.csv",
    });
    const createObjectURL = vi.fn(() => "blob:activity");
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL }));
    URL.revokeObjectURL = vi.fn();
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(() => undefined);
    renderLog();

    fireEvent.change(screen.getByDisplayValue("Any reason"), {
      target: { value: "scheduled" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));
    fireEvent.click(screen.getByRole("button", { name: "Export CSV →" }));

    await waitFor(() => expect(click).toHaveBeenCalled());
    expect(mocks.fetchActivityExport).toHaveBeenCalledWith(
      "csv",
      expect.objectContaining({ trigger: "scheduled" }),
    );
    expect(mocks.fetchActivityExport.mock.calls[0][1]).not.toHaveProperty(
      "limit",
    );

    fireEvent.click(screen.getByRole("button", { name: "Export JSON →" }));
    await waitFor(() =>
      expect(mocks.fetchActivityExport).toHaveBeenCalledWith(
        "json",
        expect.objectContaining({ trigger: "scheduled" }),
      ),
    );
    click.mockRestore();
  });

  it("clears all history only after RESET is typed, listing the counts", async () => {
    mocks.useActivityRecentQuery.mockReturnValue(recentResult([passEvent]));
    mocks.fetchOperationalHistoryPreview.mockResolvedValue({
      status: "preview",
      activity_events_deleted: 12,
      jobs_deleted: 4,
      total_deleted: 16,
    });
    mocks.resetOperationalHistory.mockResolvedValue({
      status: "reset",
      activity_events_deleted: 12,
      jobs_deleted: 4,
      total_deleted: 16,
    });
    renderLog();

    fireEvent.click(
      screen.getByRole("button", { name: "Clear all history →" }),
    );
    const dialog = await screen.findByTestId(
      "activity-clear-all-history-dialog",
    );
    expect(dialog).toHaveTextContent("12 Activity events");
    expect(dialog).toHaveTextContent("4 finished jobs");
    expect(dialog).toHaveTextContent(
      "Queued and running work is kept, and so are all settings. No media file is touched.",
    );
    const confirm = within(dialog).getByRole("button", {
      name: "Clear all history",
    });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByRole("textbox"), {
      target: { value: "reset please" },
    });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByRole("textbox"), {
      target: { value: "RESET" },
    });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);

    await waitFor(() =>
      expect(mocks.resetOperationalHistory).toHaveBeenCalledWith("RESET"),
    );
    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(
        "History cleared. Removed 12 Activity events and 4 finished jobs.",
      ),
    );
  });

  it("closes the clear-history dialog on Escape", async () => {
    mocks.useActivityRecentQuery.mockReturnValue(recentResult([passEvent]));
    mocks.fetchOperationalHistoryPreview.mockResolvedValue({
      status: "preview",
      activity_events_deleted: 1,
      jobs_deleted: 0,
      total_deleted: 1,
    });
    renderLog();

    fireEvent.click(
      screen.getByRole("button", { name: "Clear all history →" }),
    );
    const dialog = await screen.findByTestId(
      "activity-clear-all-history-dialog",
    );
    // The dialog starts listening for Escape in the same effect that moves focus into it, which can run
    // just after the dialog first appears, so wait for the focus before pressing the key.
    await waitFor(() =>
      expect(dialog).toContainElement(document.activeElement as HTMLElement),
    );
    fireEvent.keyDown(document, { key: "Escape" });
    expect(
      screen.queryByTestId("activity-clear-all-history-dialog"),
    ).not.toBeInTheDocument();
  });

  it("does not offer clearing to viewers", () => {
    mocks.role.current = "viewer";
    mocks.useActivityRecentQuery.mockReturnValue(recentResult([passEvent]));
    renderLog();

    expect(
      screen.queryByRole("button", { name: "Clear all history →" }),
    ).not.toBeInTheDocument();
  });

  describe("live updates", () => {
    const older = event({ id: 5, title: "Older entry" });
    const newer = event({
      id: 6,
      event_type: "system.reconciliation.repair",
      module: "system",
      title: "Newest entry",
      detail: "Newest entry detail",
    });

    it("inserts new entries straight away when the reader is at the top", () => {
      mocks.useActivityRecentQuery.mockReturnValue(recentResult([older]));
      const view = renderLog();

      mocks.useActivityRecentQuery.mockReturnValue(
        recentResult([newer, older]),
      );
      view.rerenderLog();

      expect(screen.getByText("Newest entry detail")).toBeInTheDocument();
      expect(
        screen.queryByRole("button", { name: /new entr/ }),
      ).not.toBeInTheDocument();
    });

    it("holds new entries behind a button when the reader has scrolled away", () => {
      mocks.useActivityRecentQuery.mockReturnValue(recentResult([older]));
      const view = renderLog();
      const feed = screen.getByTestId("activity-feed");
      feed.getBoundingClientRect = () => ({ top: -240 }) as DOMRect;
      fireEvent.scroll(window);

      mocks.useActivityRecentQuery.mockReturnValue(
        recentResult([newer, older]),
      );
      view.rerenderLog();

      expect(screen.queryByText("Newest entry detail")).not.toBeInTheDocument();
      expect(screen.getAllByTestId("activity-row")).toHaveLength(1);
      fireEvent.click(
        screen.getByRole("button", { name: "1 new entry — show" }),
      );
      expect(screen.getByText("Newest entry detail")).toBeInTheDocument();
      expect(
        screen.queryByRole("button", { name: /new entr/ }),
      ).not.toBeInTheDocument();
    });

    it("holds new entries while an entry is expanded", () => {
      const detailed = event({
        id: 5,
        event_type: "processing.custom_event",
        title: "Older entry",
        detail: "x".repeat(200),
      });
      mocks.useActivityRecentQuery.mockReturnValue(recentResult([detailed]));
      const view = renderLog();
      const details = screen
        .getByTestId("activity-feed")
        .querySelector("details")!;
      details.open = true;
      fireEvent(details, new Event("toggle"));

      mocks.useActivityRecentQuery.mockReturnValue(
        recentResult([newer, detailed]),
      );
      view.rerenderLog();

      expect(
        screen.getByRole("button", { name: "1 new entry — show" }),
      ).toBeInTheDocument();
      expect(screen.queryByText("Newest entry detail")).not.toBeInTheDocument();
    });
  });
});
