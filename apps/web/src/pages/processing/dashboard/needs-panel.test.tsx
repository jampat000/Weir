import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ProcessingFile } from "../../../lib/processing/files-api";
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
const readiness = { worker_health: [] as unknown[] };
const failedJobs = { jobs: [] as unknown[] };

vi.mock("../../../lib/processing/files-queries", () => ({
  useRequeueProcessingFile: () => requeue,
}));
vi.mock("../../../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => ({ data: readiness }),
}));
vi.mock("../../../lib/processing/jobs-inspection/queries", () => ({
  useProcessingJobsInspectionQuery: () => ({ data: failedJobs }),
}));
vi.mock("../../history/history-rejected-again", () => ({
  ProcessRejectedAgain: () => <button type="button">Process all again</button>,
}));

const workflows = [
  { id: 1, enabled: true, watched_folder: "D:/tv" },
] as ProcessingLibrary[];

const stuckFile = {
  id: 9,
  relative_path: "Ember.and.Ash.S01E02.mkv",
  status: "processing_failed",
  status_reason: "The new file would not play. The original is safe.",
} as ProcessingFile;

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
}

beforeEach(() => {
  requeue.mutate = vi.fn();
  requeue.isPending = false;
  readiness.worker_health = [];
  failedJobs.jobs = [];
});

describe("the Needs you panel", () => {
  it("says nothing needs you when nothing does, and lists nothing", () => {
    renderPanel();

    const panel = screen.getByRole("region", { name: "Needs you" });
    expect(panel).toHaveTextContent("nothing right now");
    expect(
      within(panel).getByText("Nothing needs you right now."),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("live-needs")).toBeNull();
  });

  it("counts its rows and links to the files in History", () => {
    failedJobs.jobs = [{ id: 1 }];
    renderPanel({ stuck: [stuckFile] });

    const panel = screen.getByRole("region", { name: "Needs you" });
    expect(panel).toHaveTextContent("2 to look at");
    expect(
      within(panel).getByRole("link", { name: "History: Needs you" }),
    ).toHaveAttribute("href", "/history?show=failed");
  });

  it("gives a stuck file its name, its reason and where to read more", () => {
    renderPanel({ stuck: [stuckFile] });

    const row = screen.getByTestId("live-needs");
    expect(row).toHaveTextContent("Ember and Ash S01E02");
    expect(row).toHaveTextContent("The new file would not play.");
    expect(
      within(row).getByRole("link", { name: "Open in History →" }),
    ).toHaveAttribute("href", "/history?q=Ember.and.Ash.S01E02.mkv");
  });

  it("offers Process all again beside the rejected files", () => {
    renderPanel({ rejectedCount: 3 });

    const row = screen.getByTestId("live-needs");
    expect(row).toHaveTextContent("3 files were rejected");
    expect(
      within(row).getByRole("button", { name: "Process all again" }),
    ).toBeInTheDocument();
  });

  describe("trying a stuck file again", () => {
    it("queues that file", () => {
      renderPanel({ stuck: [stuckFile] });

      fireEvent.click(screen.getByRole("button", { name: "Try again" }));

      expect(requeue.mutate).toHaveBeenCalledWith(9, expect.any(Object));
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
});
