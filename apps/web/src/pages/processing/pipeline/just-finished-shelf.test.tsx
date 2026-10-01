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
        onOpen={vi.fn()}
        {...props}
      />
    </MemoryRouter>
  );
}

describe("Just finished", () => {
  it("shows each file as a tile with its title, what it shrank by, what was removed and how long ago", () => {
    render(shelf([finished(1)]));

    const tile = screen.getByRole("button", {
      name: "The Quiet Harbour S01E01: 2 audio, 4 subtitles removed, 3 min ago",
    });
    expect(within(tile).getByText("−318 MB")).toBeInTheDocument();
    expect(
      within(tile).getByText("The Quiet Harbour S01E01"),
    ).toBeInTheDocument();
    expect(
      within(tile).getByText("2 audio, 4 subtitles removed · 3 min ago"),
    ).toBeInTheDocument();
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

    expect(screen.getAllByRole("button")).toHaveLength(1);
    expect(screen.getByRole("button", { name: /S01E02/ })).toBeInTheDocument();
  });

  it("says so when nothing has finished", () => {
    render(shelf([]));

    expect(
      screen.getByText("Nothing has finished recently."),
    ).toBeInTheDocument();
  });

  it("says what the panel counts beside its title", () => {
    render(
      shelf([finished(1)], { count: "38 cleaned today · 41.20 GB saved" }),
    );

    expect(
      screen.getByRole("region", { name: "Just finished" }),
    ).toHaveTextContent("38 cleaned today · 41.20 GB saved");
  });

  it("links on to History", () => {
    render(shelf([finished(1)]));

    expect(
      screen.getByRole("link", { name: "History: Just finished" }),
    ).toHaveAttribute("href", "/history");
  });

  it("marks each tile with the path of its file, so the board can fly a delivered tile to it", () => {
    render(shelf([finished(1)]));

    expect(document.querySelector("[data-shelf-path]")).toHaveAttribute(
      "data-shelf-path",
      "The.Quiet.Harbour.S01E01.1080p.WEB-DL.mkv",
    );
  });
});
