import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { LeavingCard } from "../leaving-cards";
import type { Lanes } from "../processing-model";
import type { Filter } from "../processing-toolbar";
import { PipelineBoard, type PipelineBoardProps } from "./pipeline-board";
import {
  NOW,
  aCleanJob,
  aFile,
  aWriting,
  anEndedCard,
  lanesOf,
} from "./pipeline-fixtures";

const NOTHING_ENDED: LeavingCard[] = [];

function setReducedMotion(reduce: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: reduce && query.includes("prefers-reduced-motion"),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
    onchange: null,
  })) as typeof window.matchMedia;
}

function board(
  lanes: Lanes,
  overrides: Partial<PipelineBoardProps> = {},
): React.ReactElement {
  return (
    <MemoryRouter>
      <PipelineBoard
        lanes={lanes}
        leaving={NOTHING_ENDED}
        filter="all"
        now={NOW}
        onOpen={vi.fn()}
        {...overrides}
      />
    </MemoryRouter>
  );
}

const cardNamed = (name: RegExp | string) =>
  screen.getByRole("button", { name });

beforeEach(() => setReducedMotion(false));
afterEach(() => vi.restoreAllMocks());

describe("the heading and the stations", () => {
  it("says nothing is in progress when nothing is", () => {
    render(board(lanesOf([])));

    expect(
      screen.getByRole("heading", {
        name: "Pipeline · nothing in progress right now",
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Nothing is being cleaned right now/),
    ).toBeInTheDocument();
  });

  it("says why nothing is happening while Weir is paused", () => {
    render(board(lanesOf([]), { paused: true }));

    expect(
      screen.getByText("Paused. Nothing new starts until you resume."),
    ).toBeInTheDocument();
  });

  it("counts what is in progress and gives a time only when every file in progress is writing", () => {
    render(board(lanesOf([aWriting(1, { progress_eta_seconds: 600 })])));

    expect(
      screen.getByRole("heading", {
        name: /^Pipeline · 1 in progress · all done by about \d{1,2}:\d{2} [ap]m$/,
      }),
    ).toBeInTheDocument();
  });

  it("leaves the time out while a file is waiting", () => {
    render(board(lanesOf([aWriting(1), aFile(2, "unprocessed")])));

    expect(
      screen.getByRole("heading", { name: "Pipeline · 2 in progress" }),
    ).toBeInTheDocument();
  });

  it("shows the five stations with how many files are at each", () => {
    render(
      board(
        lanesOf([
          aFile(1, "on_hold", { status_reason: "Still being copied." }),
          aFile(2, "unprocessed"),
          aFile(3, "unprocessed"),
          aFile(4, "processing", { progress_stage: "planning" }),
          aWriting(5),
        ]),
      ),
    );

    expect(
      screen.getByRole("group", { name: "Incoming: 1" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("group", { name: "Queued: 2" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("group", { name: "Analysing: 1" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("group", { name: "Processing: 1" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("group", { name: "Delivering: 0" }),
    ).toBeInTheDocument();
  });
});

describe("the cards", () => {
  it("puts each file's card at its station and says what it is doing", () => {
    render(board(lanesOf([aWriting(1), aFile(2, "unprocessed")])));

    const writing = cardNamed(/The Quiet Harbour S01E01: 42% · writing/);
    const waiting = cardNamed(/The Quiet Harbour S01E02: Waiting its turn/);

    expect(writing).toHaveAttribute("data-stage", "processing");
    expect(waiting).toHaveAttribute("data-stage", "queued");
    expect(within(writing).getByText("42%")).toBeInTheDocument();
  });

  it("opens the file's story when its card is clicked", () => {
    const onOpen = vi.fn();
    const files = [aWriting(1)];
    render(board(lanesOf(files), { onOpen }));

    fireEvent.click(cardNamed(/The Quiet Harbour S01E01/));

    expect(onOpen).toHaveBeenCalledWith(expect.objectContaining({ id: 1 }));
  });

  it("shows a library clean as a card with no file story to open", () => {
    render(board(lanesOf([], [aCleanJob(7, "leased")])));

    expect(screen.queryByRole("button")).toBeNull();
    expect(
      screen.getByRole("group", {
        name: /Paper Lanterns \(2023\): Cleaning in place/,
      }),
    ).toBeInTheDocument();
  });

  it("shows three rows at a station and ends with how many more there are", () => {
    const files = [1, 2, 3, 4, 5].map((id) => aFile(id, "unprocessed"));
    render(board(lanesOf(files)));

    const link = screen.getByRole("link", { name: "and 2 more →" });
    expect(link).toHaveAttribute(
      "title",
      expect.stringContaining("The Quiet Harbour S01E05"),
    );
    const hidden = document.querySelectorAll(".mm-pipe__card--hidden");
    expect(hidden).toHaveLength(2);
    for (const card of hidden)
      expect(card).toHaveAttribute("aria-hidden", "true");
  });

  it("says nothing about more when a station fits in its three rows", () => {
    render(board(lanesOf([1, 2, 3].map((id) => aFile(id, "unprocessed")))));

    expect(screen.queryByText(/more →/)).toBeNull();
  });
});

describe("the filter", () => {
  const lanes = lanesOf(
    [aWriting(1), aFile(2, "unprocessed")],
    [aCleanJob(7, "leased")],
  );
  const names = (filter: Filter) => {
    render(board(lanes, { filter }));
    return Array.from(document.querySelectorAll("[data-pipeline-card]")).map(
      (card) => card.getAttribute("aria-label"),
    );
  };

  it("shows every card for Everything", () => {
    expect(names("all")).toHaveLength(3);
  });

  it("shows only downloads for New downloads", () => {
    expect(names("download")).toEqual([
      expect.stringContaining("S01E01"),
      expect.stringContaining("S01E02"),
    ]);
  });

  it("shows only library cleans for Library cleaning", () => {
    expect(names("library")).toEqual([
      expect.stringContaining("Paper Lanterns"),
    ]);
  });

  it("counts only the cards it shows", () => {
    render(board(lanes, { filter: "library" }));

    expect(
      screen.getByRole("heading", { name: "Pipeline · 1 in progress" }),
    ).toBeInTheDocument();
  });
});

describe("a file that has just ended", () => {
  const delivered = [anEndedCard(1, { kind: "done" })];

  it("is shown full and green as Delivered for as long as it is held", () => {
    render(board(lanesOf([]), { leaving: delivered }));

    const card = cardNamed(/The Quiet Harbour S01E01: ✓ Delivered/);
    expect(card).toHaveClass("mm-pipe__card--delivered");
    expect(card).toHaveAttribute("data-stage", "delivering");
    expect(screen.getByTestId("pipeline-announcement")).toHaveTextContent(
      "The Quiet Harbour S01E01 was delivered. It moves to Just finished.",
    );
  });

  it("does not count towards what is in progress", () => {
    render(board(lanesOf([aWriting(2)]), { leaving: delivered }));

    expect(
      screen.getByRole("heading", { name: /^Pipeline · 1 in progress/ }),
    ).toBeInTheDocument();
  });

  it("shows why a file stopped, in the station of the step it stopped at", () => {
    render(
      board(lanesOf([]), {
        leaving: [
          anEndedCard(1, {
            kind: "failed",
            at: "write",
            reason: "ffmpeg stopped.",
          }),
        ],
      }),
    );

    const card = cardNamed(/Failed at Write/);
    expect(card).toHaveClass("mm-pipe__card--failed");
    expect(card).toHaveAttribute("data-stage", "processing");
    expect(within(card).getByText("ffmpeg stopped.")).toBeInTheDocument();
  });

  it("hands its tile over for the flight to the shelf when the hold ends, and not before", () => {
    const onDelivered = vi.fn();
    const { rerender } = render(
      board(lanesOf([]), { leaving: delivered, onDelivered }),
    );
    expect(onDelivered).not.toHaveBeenCalled();

    rerender(board(lanesOf([]), { leaving: NOTHING_ENDED, onDelivered }));

    expect(onDelivered).toHaveBeenCalledTimes(1);
    expect(onDelivered).toHaveBeenCalledWith(
      expect.objectContaining({
        path: delivered[0].path,
        title: "The Quiet Harbour S01E01",
        workflow: "TV",
      }),
    );
  });

  it("does not send a tile that was only hidden by the filter", () => {
    const onDelivered = vi.fn();
    const library = [anEndedCard(1, { kind: "done" }, { source: "library" })];
    const { rerender } = render(
      board(lanesOf([]), { leaving: library, filter: "library", onDelivered }),
    );

    rerender(
      board(lanesOf([]), { leaving: library, filter: "download", onDelivered }),
    );
    rerender(
      board(lanesOf([]), {
        leaving: NOTHING_ENDED,
        filter: "download",
        onDelivered,
      }),
    );

    expect(onDelivered).not.toHaveBeenCalled();
  });

  it("does not send a tile for a file that failed", () => {
    const onDelivered = vi.fn();
    const failed = [
      anEndedCard(1, {
        kind: "failed",
        at: "write",
        reason: "ffmpeg stopped.",
      }),
    ];
    const { rerender } = render(
      board(lanesOf([]), { leaving: failed, onDelivered }),
    );

    rerender(board(lanesOf([]), { leaving: NOTHING_ENDED, onDelivered }));

    expect(onDelivered).not.toHaveBeenCalled();
  });
});

describe("a card that changes station", () => {
  it("keeps its element, so it glides, and glows as it arrives", () => {
    const { rerender } = render(board(lanesOf([aFile(1, "unprocessed")])));
    const before = cardNamed(/S01E01/);
    expect(before).not.toHaveClass("mm-pipe__card--arrived");

    rerender(board(lanesOf([aWriting(1)])));

    const after = cardNamed(/S01E01/);
    expect(after).toBe(before);
    expect(after).toHaveAttribute("data-stage", "processing");
    expect(after).toHaveClass("mm-pipe__card--arrived");
  });

  it("does not glow when movement is reduced", () => {
    setReducedMotion(true);
    const { rerender } = render(board(lanesOf([aFile(1, "unprocessed")])));

    rerender(board(lanesOf([aWriting(1)])));

    expect(cardNamed(/S01E01/)).not.toHaveClass("mm-pipe__card--arrived");
  });
});
