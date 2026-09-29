import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { WorkingItem } from "./processing-model";
import { WorkingCard } from "./working-card";

function item(overrides: Partial<WorkingItem>): WorkingItem {
  return {
    key: "file-1",
    source: "download",
    name: "Film (2024)",
    path: "Film (2024)/Film.mkv",
    facts: "1080p · H264 · 2.27 GB",
    libraryName: "Movies",
    step: "write",
    percent: 42,
    etaSeconds: 30,
    speed: "148x",
    removedAudio: 0,
    removedSubtitles: 0,
    file: null,
    ...overrides,
  };
}

describe("WorkingCard", () => {
  it("shows a download's stages as one flow, moving along Write with the live percent", () => {
    render(<WorkingCard item={item({ percent: 17 })} onOpen={vi.fn()} />);

    const flow = screen.getByTestId("live-stage-flow");
    expect(within(flow).getByText("Hand back")).toBeInTheDocument();
    expect(
      within(flow)
        .getByRole("progressbar", { name: "Write progress" })
        .querySelector(".mm-flow__fill")
        ?.getAttribute("style"),
    ).toContain("--mm-live-fill: 17");
  });

  it("is on Write, with Checking and Plan done, once the pass reports a percent", () => {
    render(<WorkingCard item={item({ percent: 42 })} onOpen={vi.fn()} />);

    const current = screen
      .getAllByRole("listitem")
      .find((step) => step.getAttribute("aria-current") === "step");
    expect(current).toHaveTextContent("Write");
    const steps = within(
      screen.getByRole("list", { name: "Stages" }),
    ).getAllByRole("listitem");
    expect(steps[0]).toHaveTextContent("Checking (done)");
    expect(steps[1]).toHaveTextContent("Plan (done)");
  });

  it("is on Checking, with no progress line, before the pass reports a percent", () => {
    render(
      <WorkingCard
        item={item({ percent: null, step: "checking" })}
        onOpen={vi.fn()}
      />,
    );

    const current = screen
      .getAllByRole("listitem")
      .find((step) => step.getAttribute("aria-current") === "step");
    expect(current).toHaveTextContent("Checking");
    expect(
      screen.queryByRole("progressbar", { name: "Write progress" }),
    ).toBeNull();
  });

  it("is on the step the server names, even before a percent, with the line into it sweeping", () => {
    render(
      <WorkingCard
        item={item({ percent: null, step: "plan" })}
        onOpen={vi.fn()}
      />,
    );

    const current = screen
      .getAllByRole("listitem")
      .find((step) => step.getAttribute("aria-current") === "step");
    expect(current).toHaveTextContent("Plan");
    expect(current?.querySelector(".mm-flow__link--busy")).not.toBeNull();
  });

  it("has no separate progress bar beside the flow for a download", () => {
    render(<WorkingCard item={item({ percent: 55 })} onOpen={vi.fn()} />);

    expect(document.querySelector(".mm-live-bar")).toBeNull();
  });

  it("shows a library clean as a bar that only says it is running, with no stages", () => {
    render(
      <WorkingCard
        item={item({ source: "library", percent: null })}
        onOpen={vi.fn()}
      />,
    );

    expect(screen.queryByTestId("live-stage-flow")).toBeNull();
    expect(
      screen.getByRole("progressbar", { name: "Progress for Film (2024)" }),
    ).not.toHaveAttribute("aria-valuenow");
  });

  it("shows the live percent as a whole number", () => {
    render(<WorkingCard item={item({ percent: 63.7 })} onOpen={vi.fn()} />);

    expect(screen.getByText("63%")).toBeInTheDocument();
  });
});
