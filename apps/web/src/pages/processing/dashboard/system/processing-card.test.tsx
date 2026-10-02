import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { ProcessingCard } from "./processing-card";
import { testStats } from "./test-stats";

type Query = { data?: unknown; isError?: boolean; error?: unknown };
let query: Query;
let recent: { done: number; savedBytes: number } | undefined;

vi.mock("../../../../lib/system/use-system-stats", () => ({
  useSystemStatsQuery: () => query,
}));
vi.mock("./use-recent-work", () => ({ useRecentWork: () => recent }));
vi.mock("../../../../components/charts/live-trace", () => ({
  LiveTrace: ({
    series,
    label,
  }: {
    series: { key: string }[];
    label: string;
  }) => (
    <i data-testid="trace">{`${label} | ${series.map((line) => line.key).join("+")}`}</i>
  ),
}));

beforeEach(() => {
  query = { data: testStats };
  recent = { done: 14, savedBytes: 3 * 1024 ** 3 };
});

describe("the Processing card", () => {
  it("shows the disk work, with what is read and what the last ten minutes saved beside it", () => {
    render(<ProcessingCard />);

    expect(screen.getByText("Disk work")).toBeInTheDocument();
    expect(screen.getByText("6.0")).toBeInTheDocument();
    expect(screen.getByText("reading 20 · 3.00 GB saved")).toBeInTheDocument();
  });

  it("shows the speed, how many passes run of the slots, and what the last ten minutes finished", () => {
    render(<ProcessingCard />);

    expect(screen.getByText("Speed")).toBeInTheDocument();
    expect(screen.getByText("148")).toBeInTheDocument();
    expect(screen.getByText("2 of 4 running · 14 done")).toBeInTheDocument();
  });

  it("draws reads and writes as two lines on one trace, and the speed as one", () => {
    render(<ProcessingCard />);

    expect(
      screen.getAllByTestId("trace").map((trace) => trace.textContent),
    ).toEqual([
      "Disk work, last 10 minutes | read+write",
      "Speed, last 10 minutes | speed",
    ]);
  });

  it("shows only the running passes while the finished work is still being read", () => {
    recent = undefined;
    render(<ProcessingCard />);

    expect(screen.getByText("2 of 4 running")).toBeInTheDocument();
    expect(screen.queryByText(/done/)).toBeNull();
  });

  it("says it is reading while the first reading is on its way", () => {
    query = {};
    render(<ProcessingCard />);

    expect(screen.getByText("Reading Weir's work…")).toBeInTheDocument();
  });
});
