import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import type { NextItem } from "./next-model";
import { TimersPanel } from "./timers-panel";

const NOW = Date.parse("2026-10-02T10:00:00Z");

const items: NextItem[] = [
  {
    key: "scan-1",
    label: "Look for new downloads in TV",
    to: "/settings?tab=libraries&edit=1",
    at: NOW + 30_000,
    intervalSeconds: 60,
  },
  {
    key: "clean-2",
    label: "Clean the Movies library",
    to: "/library",
    at: NOW + 20 * 60_000,
    intervalSeconds: null,
  },
];

function show(props: Partial<Parameters<typeof TimersPanel>[0]> = {}) {
  render(
    <MemoryRouter>
      <TimersPanel items={items} now={NOW} paused={false} {...props} />
    </MemoryRouter>,
  );
}

describe("TimersPanel", () => {
  it("lists each timer with the time until it runs, soonest first", () => {
    show();
    const rows = screen.getAllByRole("listitem");
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent("Look for new downloads in TV");
    expect(rows[0]).toHaveTextContent("30 s");
    expect(rows[1]).toHaveTextContent("20 min");
    expect(
      screen.getByRole("link", { name: "Clean the Movies library" }),
    ).toHaveAttribute("href", "/library");
  });

  it("says so when nothing is on a timer", () => {
    show({ items: [] });
    expect(screen.getByText(/Nothing is waiting on a timer/)).toBeVisible();
    expect(screen.queryByRole("listitem")).toBeNull();
  });

  it("says that nothing new starts while paused, and still lists the timers", () => {
    show({ paused: true });
    expect(screen.getByText(/Processing is paused/)).toBeVisible();
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
  });
});
