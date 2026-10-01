import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { handedBack } from "../handed-back-model";
import type { HandedBackSummary } from "../use-handed-back";
import { NEEDS_PANEL_ID } from "./show-needs-panel";
import { TodayTile } from "./today-tile";

const NOW = Date.parse("2026-08-18T10:00:00Z");
const stats = { files_processed: 38, net_space_saved_bytes: 44_236_078_284 };
const needsYou = { count: 0 };
/** The workflow the tile asked the figures and the count for, each time. */
const figuresAsked: (number | null)[] = [];
const summary: { value: HandedBackSummary } = {
  value: { handed: handedBack([], NOW), total: 0, partial: false },
};

vi.mock("./use-today-figures", () => ({
  useTodayFigures: (workflowId: number | null) => {
    figuresAsked.push(workflowId);
    return {
      cleaned: stats.files_processed,
      savedBytes: stats.net_space_saved_bytes,
    };
  },
}));
vi.mock("./use-needs-you", () => ({
  useNeedsYou: (workflowId: number | null) => {
    figuresAsked.push(workflowId);
    return { groups: [], count: needsYou.count };
  },
}));
vi.mock("../use-handed-back", () => ({
  useHandedBack: () => summary.value,
}));

function finishedAt(minutesAgo: number) {
  return {
    id: minutesAgo,
    source: "download" as const,
    kind: "cleaned" as const,
    relativePath: "Heat.mkv",
    libraryId: 1,
    savedBytes: null,
    removedAudio: 0,
    removedSubtitles: 0,
    sentence: null,
    finishedAt: new Date(NOW - minutesAgo * 60_000).toISOString(),
  };
}

function renderTile(workflowId?: number | null) {
  render(
    <MemoryRouter>
      <TodayTile filter="all" now={NOW} workflowId={workflowId} />
    </MemoryRouter>,
  );
  return screen.getByRole("region", { name: "Today" });
}

afterEach(() => {
  document.getElementById(NEEDS_PANEL_ID)?.remove();
});

beforeEach(() => {
  needsYou.count = 0;
  figuresAsked.length = 0;
  summary.value = { handed: handedBack([], NOW), total: 0, partial: false };
});

describe("the Today tile", () => {
  it("asks for every workflow's figures unless it is given one", () => {
    renderTile();

    expect(new Set(figuresAsked)).toEqual(new Set([null]));
  });

  it("asks for one workflow's figures and need-a-look count when narrowed to it", () => {
    renderTile(3);

    expect(new Set(figuresAsked)).toEqual(new Set([3]));
  });

  it("says how many were cleaned today and how much space that saved", () => {
    const tile = renderTile();

    expect(within(tile).getByTestId("live-done-today")).toHaveTextContent("38");
    expect(tile).toHaveTextContent("cleaned");
    expect(tile).toHaveTextContent("41.20 GB saved");
  });

  it("links to the files that need a look when there are any, and says nothing when there are none", () => {
    expect(renderTile()).not.toHaveTextContent("need a look");
  });

  it("names the number that need a look, and takes the person to the Needs you panel", () => {
    needsYou.count = 12;
    const panel = document.createElement("section");
    panel.id = NEEDS_PANEL_ID;
    panel.tabIndex = -1;
    const scrollIntoView = vi.fn();
    panel.scrollIntoView = scrollIntoView;
    document.body.append(panel);
    const tile = renderTile();

    fireEvent.click(
      within(tile).getByRole("button", { name: "12 need a look →" }),
    );

    expect(scrollIntoView).toHaveBeenCalled();
    expect(panel).toHaveFocus();
  });

  it("says nothing finished in the last 2 hours rather than drawing a chart of nothing", () => {
    const tile = renderTile();

    expect(within(tile).getByTestId("live-handed-back-sum")).toHaveTextContent(
      "Nothing finished in the last 2 hours.",
    );
  });

  it("describes the two hours in words, for a screen reader", () => {
    const handed = handedBack([finishedAt(3), finishedAt(8)], NOW);
    summary.value = { handed, total: handed.totals.all, partial: false };
    const tile = renderTile();

    expect(within(tile).getByTestId("live-handed-back-sum")).toHaveTextContent(
      "2 files finished in the last 2 hours: 2 cleaned.",
    );
  });

  it("lets the keyboard walk the chart and reads each five minutes of it", () => {
    const handed = handedBack(
      [finishedAt(3), finishedAt(3), finishedAt(8)],
      NOW,
    );
    summary.value = { handed, total: handed.totals.all, partial: false };
    const tile = renderTile();
    const chart = within(tile).getByRole("slider", {
      name: "Files finished, five minutes to a point",
    });

    chart.focus();
    expect(chart).toHaveAttribute(
      "aria-valuetext",
      expect.stringContaining("nothing"),
    );

    fireEvent.keyDown(chart, { key: "ArrowLeft" });

    expect(chart).toHaveAttribute(
      "aria-valuetext",
      expect.stringContaining("2 cleaned"),
    );
  });
});
