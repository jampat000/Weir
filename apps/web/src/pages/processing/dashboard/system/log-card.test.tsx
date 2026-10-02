import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { LogLine } from "./log-card-model";
import { LogCard } from "./log-card";
import type { SystemLog } from "./use-system-log";

const NOW = Date.parse("2026-10-02T12:00:00Z");

const log: SystemLog = {
  lines: [],
  counts: { errors: 0, warnings: 0 },
  loading: false,
  failed: false,
};
let fits = Number.MAX_SAFE_INTEGER;

vi.mock("../fit-rows", () => ({
  useFittingRows: () => [{ current: null }, fits],
}));
vi.mock("./use-system-log", () => ({ useSystemLog: () => log }));
vi.mock("../../../../lib/ui/mm-format-date", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../../lib/ui/mm-format-date")
  >()),
  useAppClockSecondsFormatter: () => (ms: number) => `t${ms - NOW}`,
}));

function line(
  level: LogLine["level"],
  message: string,
  secondsAgo: number,
  arrivedAt: number | null = null,
): LogLine {
  const at = NOW - secondsAgo * 1000;
  return { key: `${at}|${level}|${message}`, at, level, message, arrivedAt };
}

function renderCard() {
  render(
    <MemoryRouter>
      <LogCard />
    </MemoryRouter>,
  );
  return screen.getByRole("region", { name: "Log" });
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(NOW);
  log.lines = [];
  log.counts = { errors: 0, warnings: 0 };
  log.loading = false;
  log.failed = false;
  fits = Number.MAX_SAFE_INTEGER;
});

afterEach(() => {
  vi.useRealTimers();
});

describe("the Log card", () => {
  it("lists each line with its time, its level and its message, and counts today's errors and warnings", () => {
    log.lines = [
      line("error", "Disk is full.", 10),
      line("warning", "Radarr was slow.", 60),
      line("info", "Scan finished.", 120),
    ];
    log.counts = { errors: 1, warnings: 1 };
    const card = renderCard();

    fireEvent.click(within(card).getByRole("button", { name: "All" }));
    const rows = within(card).getAllByTestId("system-log-line");
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent("Error");
    expect(rows[0]).toHaveTextContent("Disk is full.");
    expect(rows[0]).toHaveTextContent("t-10000");
    expect(card).toHaveTextContent("1 error · 1 warning today");
  });

  it("starts on the problems when there have been any today, so the information does not bury them", () => {
    log.lines = [
      line("info", "Scan finished.", 5),
      line("error", "Disk is full.", 10),
      line("warning", "Radarr was slow.", 60),
    ];
    log.counts = { errors: 1, warnings: 1 };
    const card = renderCard();

    expect(
      within(card).getByRole("button", { name: "Problems" }),
    ).toHaveAttribute("aria-pressed", "true");
    expect(within(card).getAllByTestId("system-log-line")).toHaveLength(2);
    expect(card).not.toHaveTextContent("Scan finished.");
  });

  it("starts on everything when today has had no errors or warnings", () => {
    log.lines = [line("info", "Scan finished.", 5)];
    const card = renderCard();

    expect(within(card).getByRole("button", { name: "All" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(card).toHaveTextContent("Scan finished.");
  });

  it("keeps a choice the person made when the counts change", () => {
    log.lines = [line("info", "Scan finished.", 5)];
    const card = renderCard();
    fireEvent.click(within(card).getByRole("button", { name: "Errors" }));

    expect(
      within(card).getByRole("button", { name: "Errors" }),
    ).toHaveAttribute("aria-pressed", "true");
  });

  it("marks each line with its level, so a warning and an error stand out from information", () => {
    log.lines = [
      line("error", "Disk is full.", 10),
      line("warning", "Radarr was slow.", 60),
      line("info", "Scan finished.", 120),
    ];
    const card = renderCard();

    const [error, warning, info] =
      within(card).getAllByTestId("system-log-line");
    expect(error).toHaveClass("mm-sy-log--error");
    expect(warning).toHaveClass("mm-sy-log--warning");
    expect(info).toHaveClass("mm-sy-log--info");
  });

  it("narrows the list to errors, or to warnings, with the switch", () => {
    log.lines = [
      line("error", "Disk is full.", 10),
      line("warning", "Radarr was slow.", 60),
      line("info", "Scan finished.", 120),
    ];
    const card = renderCard();

    fireEvent.click(within(card).getByRole("button", { name: "Errors" }));
    expect(within(card).getAllByTestId("system-log-line")).toHaveLength(1);
    expect(card).toHaveTextContent("Disk is full.");

    fireEvent.click(within(card).getByRole("button", { name: "Warnings" }));
    expect(card).toHaveTextContent("Radarr was slow.");
    expect(card).not.toHaveTextContent("Disk is full.");

    fireEvent.click(within(card).getByRole("button", { name: "All" }));
    expect(within(card).getAllByTestId("system-log-line")).toHaveLength(3);
  });

  it("pulses a line that has just arrived, and not one read from the log", () => {
    log.lines = [
      line("error", "Just now.", 0, NOW),
      line("error", "Earlier.", 600),
    ];
    const card = renderCard();

    const [fresh, old] = within(card).getAllByTestId("system-log-line");
    expect(fresh).toHaveClass("mm-sy-log--new");
    expect(old).not.toHaveClass("mm-sy-log--new");
  });

  it("links the header to the full server log", () => {
    const card = renderCard();

    expect(
      within(card).getByRole("link", { name: "Full log: Log" }),
    ).toHaveAttribute("href", "/system?tab=logs&show=log");
  });

  it("says how many lines the card's height left out", () => {
    log.lines = [
      line("info", "a", 1),
      line("info", "b", 2),
      line("info", "c", 3),
    ];
    fits = 1;
    const card = renderCard();

    expect(within(card).getByTestId("system-more")).toHaveTextContent("2 more");
  });

  it("says what an empty list means for the switch's choice", () => {
    const card = renderCard();
    expect(card).toHaveTextContent("Nothing has been logged yet.");

    fireEvent.click(within(card).getByRole("button", { name: "Errors" }));
    expect(card).toHaveTextContent("No errors in the log.");
  });

  it("says Weir could not read the log when every read failed", () => {
    log.failed = true;
    const card = renderCard();

    expect(card).toHaveTextContent("could not read its log");
  });

  it("shows no count while the log is being read", () => {
    log.loading = true;
    const card = renderCard();

    expect(card).not.toHaveTextContent("today");
  });
});
