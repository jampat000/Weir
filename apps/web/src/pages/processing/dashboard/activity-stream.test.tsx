import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { ActivityEventItem } from "../../../lib/api/types";
import { ActivityStream } from "./activity-stream";

const NOW = Date.parse("2026-08-18T10:00:00Z");
const feed: { items: ActivityEventItem[] } = { items: [] };

vi.mock("../../../lib/activity/queries", () => ({
  useActivityRecentQuery: () => ({ data: feed }),
}));

function passEvent(id: number, path: string): ActivityEventItem {
  return {
    id,
    created_at: "2026-08-18T09:58:00Z",
    event_type: "processing.file_remux_pass_completed",
    module: "processing",
    title: "x",
    relative_path: path,
    detail: JSON.stringify({
      outcome: "live_output_written",
      ok: true,
      relative_media_path: path,
    }),
  } as ActivityEventItem;
}

function routineEvent(id: number): ActivityEventItem {
  return {
    id,
    created_at: "2026-08-18T09:50:00Z",
    event_type: "processing.work_temp_stale_sweep_completed",
    module: "processing",
    title: "x",
  } as ActivityEventItem;
}

function renderStream() {
  return render(
    <MemoryRouter>
      <ActivityStream now={NOW} />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  feed.items = [];
});

afterEach(() => vi.restoreAllMocks());

/** Gives the panel's list room for `rows` whole lines of 40px: each line ends where its number says. */
function roomForLines(rows: number) {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(
    function (this: HTMLElement) {
      if (this.classList.contains("mm-stream__fit"))
        return { bottom: rows * 40 + 20 } as DOMRect;
      if (this.hasAttribute("data-fit")) {
        const index = Array.from(this.parentElement?.children ?? []).indexOf(
          this,
        );
        return { bottom: (index + 1) * 40 } as DOMRect;
      }
      return { bottom: 0 } as DOMRect;
    },
  );
}

describe("the activity stream panel", () => {
  it("says nothing has happened yet when there is nothing to list", () => {
    renderStream();

    expect(
      screen.getByText("Nothing yet. What Weir does appears here."),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("live-stream")).toBeNull();
  });

  it("lists each line with its file's name in bold and when it happened, linked to its story", () => {
    feed.items = [passEvent(2, "Heat.1995.mkv")];
    renderStream();

    const list = screen.getByTestId("live-stream");
    const link = within(list).getByRole("link");
    expect(link).toHaveAttribute("href", "/history?q=Heat.1995.mkv");
    expect(link).toHaveTextContent("Heat (1995) cleaned");
    expect(within(link).getByText("Heat (1995)").tagName).toBe("B");
    expect(link).toHaveTextContent("2 min ago");
  });

  it("says how many routine entries it left out, beside its title", () => {
    feed.items = [
      passEvent(3, "Heat.1995.mkv"),
      routineEvent(2),
      routineEvent(1),
    ];
    renderStream();

    expect(screen.getByRole("region", { name: "Activity" })).toHaveTextContent(
      "2 routine left out",
    );
  });

  it("says it is live when nothing was left out, and links to the whole log", () => {
    feed.items = [passEvent(3, "Heat.1995.mkv")];
    renderStream();

    const panel = screen.getByRole("region", { name: "Activity" });
    expect(panel).toHaveTextContent("as it happens");
    expect(
      within(panel).getByRole("link", { name: "All activity: Activity" }),
    ).toHaveAttribute("href", "/system?tab=logs");
  });

  it("lets a line that arrives while the page is open glow once, but not the ones already there", () => {
    feed.items = [passEvent(2, "Heat.1995.mkv")];
    const { rerender } = renderStream();
    const lines = () =>
      within(screen.getByTestId("live-stream")).getAllByRole("listitem");
    expect(lines()[0]).not.toHaveClass("mm-stream__item--new");

    feed.items = [
      passEvent(3, "Sintel.2010.mkv"),
      passEvent(2, "Heat.1995.mkv"),
    ];
    rerender(
      <MemoryRouter>
        <ActivityStream now={NOW} />
      </MemoryRouter>,
    );

    expect(lines()[0]).toHaveClass("mm-stream__item--new");
    expect(lines()[1]).not.toHaveClass("mm-stream__item--new");
  });

  it("shows only the whole lines that fit and says how many more there are", () => {
    feed.items = [5, 4, 3, 2, 1].map((id) => passEvent(id, `Film.${id}.mkv`));
    roomForLines(2);
    renderStream();

    const lines = within(screen.getByTestId("live-stream")).getAllByRole(
      "listitem",
      { hidden: true },
    );
    expect(lines.map((line) => line.style.visibility)).toEqual([
      "",
      "",
      "hidden",
      "hidden",
      "hidden",
    ]);
    const more = screen.getByRole("link", {
      name: "3 more in the activity log",
    });
    expect(more).toHaveTextContent("3 more");
    expect(more).toHaveAttribute("href", "/system?tab=logs");
  });

  it("says nothing about more when every line fits", () => {
    feed.items = [passEvent(2, "Heat.1995.mkv"), passEvent(1, "Sintel.mkv")];
    roomForLines(2);
    renderStream();

    expect(screen.queryByText(/more$/)).toBeNull();
  });

  it("fits the lines that arrive after the panel has drawn its empty message", () => {
    roomForLines(1);
    const { rerender } = renderStream();

    feed.items = [3, 2, 1].map((id) => passEvent(id, `Film.${id}.mkv`));
    rerender(
      <MemoryRouter>
        <ActivityStream now={NOW} />
      </MemoryRouter>,
    );

    expect(
      screen.getByRole("link", { name: "2 more in the activity log" }),
    ).toBeInTheDocument();
  });
});
