import { render, screen } from "@testing-library/react";
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
    percent: 42,
    etaSeconds: 30,
    speed: "148x",
    removedAudio: 0,
    removedSubtitles: 0,
    file: null,
    ...overrides,
  };
}

/**
 * The bar's fill moves by changing `--mm-live-fill`, which drives a CSS transition on `transform`
 * (weir-processing-cards.css) rather than by re-laying out the track — cheap to animate, and left to CSS
 * so the existing `prefers-reduced-motion` rule (weir-processing-trend.css) can turn the transition off
 * without any change here (#750).
 */
describe("WorkingCard's progress bar", () => {
  it("sets the fill's animation target from the item's percent", () => {
    render(<WorkingCard item={item({ percent: 17 })} onOpen={vi.fn()} />);

    const fill = document.querySelector(".mm-live-bar__fill");
    expect(fill).not.toBeNull();
    expect(fill?.getAttribute("style")).toContain("--mm-live-fill: 17");
  });

  it("moves the fill's target when the live percent changes, for the same file", () => {
    const { rerender } = render(
      <WorkingCard item={item({ percent: 17 })} onOpen={vi.fn()} />,
    );

    rerender(<WorkingCard item={item({ percent: 63 })} onOpen={vi.fn()} />);

    const fill = document.querySelector(".mm-live-bar__fill");
    expect(fill?.getAttribute("style")).toContain("--mm-live-fill: 63");
  });

  it("never sets its own transition or animation-duration inline, leaving motion entirely to CSS", () => {
    render(<WorkingCard item={item({ percent: 55 })} onOpen={vi.fn()} />);

    const fill = document.querySelector(".mm-live-bar__fill");
    const style = fill?.getAttribute("style") ?? "";
    expect(style).not.toMatch(/transition|animation/);
  });

  it("shows the live percent as a whole number", () => {
    render(<WorkingCard item={item({ percent: 63.7 })} onOpen={vi.fn()} />);

    expect(screen.getByText("63%")).toBeInTheDocument();
  });
});
