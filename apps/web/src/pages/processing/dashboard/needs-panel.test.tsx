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
  ProcessingFile,
  ProcessingFileRemoveOptions,
} from "../../../lib/processing/files-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { NeedsPanel } from "./needs-panel";

type RequeueHandlers = {
  onSuccess: (result: { detail: string }) => void;
  onError: (error: unknown) => void;
};

const requeue = {
  mutate: vi.fn(),
  isPending: false,
};
const forget = vi.fn();
const removeOptions = vi.fn<() => Promise<ProcessingFileRemoveOptions>>();
const readiness = { worker_health: [] as unknown[] };
const failedJobs = { jobs: [] as unknown[] };
const rejectedFiles = { files: [] as ProcessingFile[] };
const rejectedAgain = vi.fn();
const checkAgain = { mutate: vi.fn(), isPending: false };

vi.mock("../../../lib/processing/files-queries", () => ({
  useRequeueProcessingFile: () => requeue,
  useProcessingCheckLibraryAgain: () => checkAgain,
  useForgetProcessingFile: () => ({ mutateAsync: forget, isPending: false }),
  useProcessingFileRemoveOptions: () => ({
    mutateAsync: removeOptions,
    isPending: false,
  }),
}));
vi.mock("../../../lib/settings/queries", () => ({
  useAppSettingsQuery: () => ({ data: undefined }),
}));
vi.mock("../../../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => ({ data: readiness }),
}));
vi.mock("../../../lib/processing/jobs-inspection/queries", () => ({
  useProcessingJobsInspectionQuery: () => ({ data: failedJobs }),
}));
vi.mock("./needs-files", () => ({
  useNeedsFiles: (workflowId: number | null | undefined) =>
    rejectedFiles.files.filter(
      (file) => workflowId == null || file.library_id === workflowId,
    ),
}));
vi.mock("../../history/history-rejected-again", () => ({
  ProcessRejectedAgain: (props: { libraryId?: number }) => {
    rejectedAgain(props);
    return <button type="button">Process all again</button>;
  },
}));

const workflows = [
  { id: 1, name: "TV", enabled: true, watched_folder: "D:/tv" },
  { id: 2, name: "Movies", enabled: true, watched_folder: "D:/movies" },
] as ProcessingLibrary[];

function file(overrides: Partial<ProcessingFile>): ProcessingFile {
  return {
    id: 9,
    library_id: 1,
    library_name: "TV",
    relative_path: "Ember.and.Ash.S01E02.mkv",
    status: "processing_failed",
    status_reason: "The new file would not play. The original is safe.",
    failure_class: "execution",
    ...overrides,
  } as ProcessingFile;
}

const stuckFile = file({});
const rejected = file({
  id: 12,
  relative_path: "Old.Film.2019.mkv",
  status: "rejected",
  failure_class: "rules",
  status_reason: "Rejected: none of its audio tracks are in English.",
});

function renderPanel(
  props: Partial<React.ComponentProps<typeof NeedsPanel>> = {},
) {
  render(
    <MemoryRouter>
      <NeedsPanel
        workflows={workflows}
        stuck={[]}
        rejectedCount={0}
        {...props}
      />
    </MemoryRouter>,
  );
  return screen.getByRole("region", { name: "Needs you" });
}

beforeEach(() => {
  requeue.mutate = vi.fn();
  requeue.isPending = false;
  checkAgain.mutate = vi.fn();
  checkAgain.isPending = false;
  forget.mockReset().mockResolvedValue({ detail: "Removed." });
  removeOptions.mockReset();
  rejectedAgain.mockReset();
  readiness.worker_health = [];
  failedJobs.jobs = [];
  rejectedFiles.files = [];
});

describe("the Needs you panel when nothing needs a person", () => {
  it("says All clear in one line, with no count and no list", () => {
    const panel = renderPanel();

    expect(within(panel).getByText("All clear")).toBeInTheDocument();
    expect(panel).not.toHaveTextContent("to look at");
    expect(screen.queryByTestId("live-needs")).toBeNull();
  });
});

describe("the Needs you panel when something does", () => {
  it("shows an amber count in its header and links to the files in History", () => {
    failedJobs.jobs = [{ id: 1 }];
    const panel = renderPanel({ stuck: [stuckFile] });

    expect(within(panel).getByText("2 to look at")).toHaveClass(
      "mm-needs__count",
    );
    expect(
      within(panel).getByRole("link", { name: "History: Needs you" }),
    ).toHaveAttribute("href", "/history?show=failed");
  });

  it("groups files by what went wrong, each group titled in plain words", () => {
    rejectedFiles.files = [rejected];
    renderPanel({ stuck: [stuckFile], rejectedCount: 1 });

    expect(
      screen.getByRole("region", { name: "1 failed while writing" }),
    ).toHaveTextContent("Ember and Ash S01E02");
    expect(
      screen.getByRole("region", { name: "1 not in a language you keep" }),
    ).toHaveTextContent("Old Film (2019)");
  });

  it("gives a file its reason and its workflow", () => {
    renderPanel({ stuck: [stuckFile] });

    const row = screen.getByTestId("live-needs");
    expect(row).toHaveTextContent("The new file would not play.");
    expect(row).toHaveTextContent("TV");
  });

  it("offers Process all again beside the rejected files only, for the workflow the panel is narrowed to", () => {
    rejectedFiles.files = [rejected];
    renderPanel({ stuck: [stuckFile], rejectedCount: 1, workflowId: 1 });

    const failedGroup = screen.getByRole("region", {
      name: "1 failed while writing",
    });
    const rejectedGroup = screen.getByRole("region", {
      name: "1 not in a language you keep",
    });
    expect(
      within(failedGroup).queryByRole("button", { name: "Process all again" }),
    ).toBeNull();
    expect(
      within(rejectedGroup).getByRole("button", { name: "Process all again" }),
    ).toBeInTheDocument();
    expect(rejectedAgain).toHaveBeenCalledWith({
      libraryId: 1,
      libraryName: "TV",
    });
  });

  it("narrows to one workflow's files and leaves out what is wrong with Weir itself", () => {
    failedJobs.jobs = [{ id: 1 }];
    const movie = file({
      id: 20,
      library_id: 2,
      library_name: "Movies",
      relative_path: "Old.Film.2019.mkv",
    });
    const panel = renderPanel({ stuck: [stuckFile, movie], workflowId: 2 });

    expect(panel).toHaveTextContent("1 to look at");
    expect(panel).not.toHaveTextContent("1 job failed");
    expect(panel).toHaveTextContent("Old Film (2019)");
    expect(panel).not.toHaveTextContent("Ember and Ash");
  });

  it("opens the file's story from the row when the page can show it", () => {
    const onOpen = vi.fn();
    renderPanel({ stuck: [stuckFile], onOpen });

    fireEvent.click(screen.getByRole("button", { name: "Open →" }));

    expect(onOpen).toHaveBeenCalledWith(stuckFile);
  });

  it("opens the file in History when the page has no story to show", () => {
    renderPanel({ stuck: [stuckFile] });

    expect(
      screen.getByRole("link", { name: "Open in History →" }),
    ).toHaveAttribute("href", "/history?q=Ember.and.Ash.S01E02.mkv");
  });

  it("links a problem with Weir itself to where it is fixed", () => {
    failedJobs.jobs = [{ id: 1 }];
    renderPanel();

    expect(
      screen.getByRole("link", { name: "Review failed jobs →" }),
    ).toHaveAttribute("href", "/system?tab=history&show=jobs&status=failed");
  });
});

describe("trying a file again", () => {
  it("queues that file", () => {
    renderPanel({ stuck: [stuckFile] });

    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(requeue.mutate).toHaveBeenCalledWith(9, expect.any(Object));
  });

  it("calls it Process again for a rejected file", () => {
    rejectedFiles.files = [rejected];
    renderPanel({ rejectedCount: 1 });

    fireEvent.click(screen.getByRole("button", { name: "Process again" }));

    expect(requeue.mutate).toHaveBeenCalledWith(12, expect.any(Object));
  });

  it("shows that it is working while it queues", () => {
    requeue.isPending = true;
    renderPanel({ stuck: [stuckFile] });

    expect(screen.getByRole("button", { name: "Queueing…" })).toBeDisabled();
  });

  it("says what the server answered when it is queued", () => {
    requeue.mutate = vi.fn((_id: number, handlers: RequeueHandlers) =>
      handlers.onSuccess({ detail: "Queued this file again." }),
    );
    renderPanel({ stuck: [stuckFile] });

    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(screen.getByRole("status")).toHaveTextContent(
      "Queued this file again.",
    );
  });

  it("says why it could not be queued, as an alert", () => {
    requeue.mutate = vi.fn((_id: number, handlers: RequeueHandlers) =>
      handlers.onError(new TypeError("boom")),
    );
    renderPanel({ stuck: [stuckFile] });

    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(screen.getByRole("alert")).toHaveTextContent(
      "That file could not be queued again.",
    );
  });
});

describe("asking a workflow to look again at a held or skipped file", () => {
  const held = file({
    id: 30,
    library_id: 2,
    status: "on_hold",
    failure_class: null,
    hold_until: null,
    status_reason: "Weir could not open this file for reading.",
  });

  it("offers Check again for a stuck file, and checks its workflow", () => {
    rejectedFiles.files = [held];
    renderPanel();

    fireEvent.click(screen.getByRole("button", { name: "Check again" }));

    expect(checkAgain.mutate).toHaveBeenCalledWith(
      { media_scope: "movie", library_id: 2 },
      expect.any(Object),
    );
    expect(requeue.mutate).not.toHaveBeenCalled();
  });

  it("says that the workflow is being checked again", () => {
    rejectedFiles.files = [held];
    checkAgain.mutate = vi.fn((_vars: unknown, handlers: RequeueHandlers) =>
      handlers.onSuccess({ detail: "" }),
    );
    renderPanel();

    fireEvent.click(screen.getByRole("button", { name: "Check again" }));

    expect(screen.getByRole("status")).toHaveTextContent(
      "Weir is checking this workflow again",
    );
  });
});

describe("removing a file", () => {
  const choice = (partial: Partial<ProcessingFileRemoveOptions> = {}) =>
    ({
      requires_choice: true,
      manager_label: null,
      delete_handled_by_manager: false,
      keep_notifies_manager: false,
      fingerprint_recorded: true,
      unconfirmed_size_bytes: null,
      unconfirmed_modified_at: null,
      ...partial,
    }) as ProcessingFileRemoveOptions;

  it("opens History's remove dialog when the file is still in the watched folder", async () => {
    removeOptions.mockResolvedValue(choice());
    renderPanel({ stuck: [stuckFile] });

    fireEvent.click(screen.getByRole("button", { name: "Remove…" }));

    expect(await screen.findByTestId("history-remove-dialog")).toBeVisible();
    expect(forget).not.toHaveBeenCalled();
  });

  it("removes what the person chose in that dialog", async () => {
    removeOptions.mockResolvedValue(choice());
    renderPanel({ stuck: [stuckFile] });
    fireEvent.click(screen.getByRole("button", { name: "Remove…" }));

    fireEvent.click(
      await screen.findByTestId("history-remove-dialog-choice-keep"),
    );
    fireEvent.click(screen.getByTestId("history-remove-dialog-confirm"));

    await waitFor(() =>
      expect(forget).toHaveBeenCalledWith({
        id: 9,
        resolution: "keep",
        confirm: undefined,
      }),
    );
    expect(await screen.findByRole("status")).toHaveTextContent("Removed.");
  });

  it("drops a file that needs no choice from the list straight away", async () => {
    removeOptions.mockResolvedValue(choice({ requires_choice: false }));
    renderPanel({ stuck: [stuckFile] });

    fireEvent.click(screen.getByRole("button", { name: "Remove…" }));

    await waitFor(() => expect(forget).toHaveBeenCalledWith({ id: 9 }));
    expect(screen.queryByTestId("history-remove-dialog")).toBeNull();
  });

  it("says when Weir could not tell what removing would do", async () => {
    removeOptions.mockRejectedValue(new TypeError("boom"));
    renderPanel({ stuck: [stuckFile] });

    fireEvent.click(screen.getByRole("button", { name: "Remove…" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Weir could not check what removing this file would do.",
    );
  });
});
