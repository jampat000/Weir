import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

import type { FinishedFile } from "../../../lib/activity/processing-outcome";
import { JustFinishedShelf } from "./just-finished-shelf";
import { NOW } from "./pipeline-fixtures";

function finished(
  id: number,
  overrides: Partial<FinishedFile> = {},
): FinishedFile {
  return {
    id,
    source: "download",
    kind: "cleaned",
    relativePath: `The.Quiet.Harbour.S01E0${id}.1080p.WEB-DL.mkv`,
    libraryId: 2,
    savedBytes: 318 * 1024 ** 2,
    removedAudio: 2,
    removedSubtitles: 4,
    sentence: null,
    finishedAt: new Date(NOW - 3 * 60_000).toISOString(),
    ...overrides,
  };
}

const WORKFLOW_NAMES = new Map([
  [1, "Movies"],
  [2, "TV"],
  [3, "Kids"],
]);

function shelf(
  items: FinishedFile[],
  props: Partial<React.ComponentProps<typeof JustFinishedShelf>> = {},
) {
  return (
    <MemoryRouter>
      <JustFinishedShelf
        items={items}
        filter="all"
        now={NOW}
        workflowNames={WORKFLOW_NAMES}
        onOpen={vi.fn()}
        {...props}
      />
    </MemoryRouter>
  );
}

const tiles = () =>
  screen
    .getAllByRole("listitem")
    .map((item) => within(item).getByRole("button").getAttribute("aria-label"));

describe("Just finished", () => {
  it("shows each file as a tile with its title, what it shrank by, what was removed and how long ago", () => {
    render(shelf([finished(1)]));

    const tile = screen.getByRole("button", {
      name: "The Quiet Harbour S01E01 (TV): 2 audio, 4 subtitles removed, 3 min ago",
    });
    expect(within(tile).getByText("−318 MB")).toBeInTheDocument();
    expect(
      within(tile).getByText("The Quiet Harbour S01E01"),
    ).toBeInTheDocument();
    expect(
      within(tile).getByText("2 audio, 4 subtitles removed · 3 min ago"),
    ).toBeInTheDocument();
  });

  it("tags each tile with its workflow, and the tag goes to that workflow in the library", () => {
    render(shelf([finished(1, { libraryId: 2 })]));

    const list = screen.getByRole("list");
    expect(within(list).getByRole("link", { name: "TV" })).toHaveAttribute(
      "href",
      "/library?library=2",
    );
  });

  it("opens the file's story when a tile is clicked", () => {
    const onOpen = vi.fn();
    const item = finished(1);
    render(shelf([item], { onOpen }));

    fireEvent.click(screen.getByRole("button", { name: /S01E01/ }));

    expect(onOpen).toHaveBeenCalledWith(item);
  });

  it("puts the ring on a file that has only just finished", () => {
    render(
      shelf([
        finished(1, { finishedAt: new Date(NOW - 5_000).toISOString() }),
        finished(2),
      ]),
    );

    const [fresh, older] = screen.getAllByRole("listitem");
    expect(fresh).toHaveClass("mm-shelf__item--fresh");
    expect(older).not.toHaveClass("mm-shelf__item--fresh");
  });

  it("honours the filter", () => {
    render(
      shelf([finished(1), finished(2, { source: "library", libraryId: 1 })], {
        filter: "library",
      }),
    );

    expect(tiles()).toEqual([expect.stringContaining("S01E02")]);
  });

  it("marks each tile with the path of its file, so the board can fly a delivered tile to it", () => {
    render(shelf([finished(1)]));

    expect(document.querySelector("[data-shelf-path]")).toHaveAttribute(
      "data-shelf-path",
      "The.Quiet.Harbour.S01E01.1080p.WEB-DL.mkv",
    );
  });

  it("links on to History", () => {
    render(shelf([finished(1)]));

    expect(
      screen.getByRole("link", { name: "History: Just finished" }),
    ).toHaveAttribute("href", "/history");
  });
});

describe("Just finished's workflow chips", () => {
  it("offers All and a chip for each workflow, with All pressed", () => {
    render(shelf([finished(1)]));

    const group = screen.getByRole("group", { name: "Show workflow" });
    expect(
      within(group)
        .getAllByRole("button")
        .map((chip) => [chip.textContent, chip.getAttribute("aria-pressed")]),
    ).toEqual([
      ["All", "true"],
      ["Movies", "false"],
      ["TV", "false"],
      ["Kids", "false"],
    ]);
  });

  it("offers a chip only to the workflows that are on, when told which", () => {
    render(shelf([finished(1)], { enabledWorkflowIds: new Set([1, 2]) }));

    const group = screen.getByRole("group", { name: "Show workflow" });
    expect(within(group).queryByRole("button", { name: "Kids" })).toBeNull();
  });

  it("narrows the shelf and its counts to the chip pressed, and back again with All", () => {
    render(
      shelf([
        finished(1, { libraryId: 2 }),
        finished(2, { libraryId: 1, kind: "failed" }),
      ]),
    );

    fireEvent.click(screen.getByRole("button", { name: "Movies" }));

    expect(tiles()).toEqual([expect.stringContaining("S01E02")]);
    expect(screen.getByRole("button", { name: "Movies" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(
      screen.getByText("1 today · 318 MB saved · 1 needs a look"),
    ).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "All" }));

    expect(tiles()).toHaveLength(2);
  });

  it("leaves only the dashboard's workflow, pressed, when the page narrows to one", () => {
    render(
      shelf([finished(1, { libraryId: 2 }), finished(2, { libraryId: 1 })], {
        workflowId: 1,
      }),
    );

    const group = screen.getByRole("group", { name: "Show workflow" });
    const chips = within(group).getAllByRole("button");
    expect(chips.map((chip) => chip.textContent)).toEqual(["Movies"]);
    expect(chips[0]).toHaveAttribute("aria-pressed", "true");
    expect(tiles()).toEqual([expect.stringContaining("S01E02")]);
  });

  it("says what the page counts for the day across every workflow, and what needs a look", () => {
    render(
      shelf([finished(1, { kind: "failed" })], {
        count: "38 cleaned today · 41.20 GB saved",
      }),
    );

    expect(
      screen.getByText("38 cleaned today · 41.20 GB saved · 1 needs a look"),
    ).toBeVisible();
  });

  it("says nothing finished yet today, and shows the latest from before", () => {
    render(
      shelf(
        [
          finished(1, {
            finishedAt: new Date(NOW - 30 * 3_600_000).toISOString(),
          }),
        ],
        { count: "0 cleaned today · 0 B saved" },
      ),
    );

    expect(
      screen.getByText("Nothing yet today · latest arrivals"),
    ).toBeVisible();
    expect(tiles()).toHaveLength(1);
  });
});
