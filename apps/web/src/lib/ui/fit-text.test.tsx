import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { FitText, narrowing } from "./fit-text";

describe("words that narrow", () => {
  const parts = [
    { text: "16 cores", matters: 1 },
    { text: "Weir 3%", matters: 2 },
    { text: "ffmpeg 41%", matters: 3 },
  ];

  it("starts with every part and drops the least important first", () => {
    expect(narrowing(parts).slice(0, 3)).toEqual([
      "16 cores · Weir 3% · ffmpeg 41%",
      "Weir 3% · ffmpeg 41%",
      "ffmpeg 41%",
    ]);
  });

  it("ends with the other parts one at a time, the more important first", () => {
    expect(narrowing(parts).slice(3)).toEqual(["Weir 3%", "16 cores"]);
  });

  it("is the one part when there is only one", () => {
    expect(narrowing([{ text: "16 cores", matters: 1 }])).toEqual(["16 cores"]);
  });
});

/** A box `room` px wide in which every character is 10px wide, in an environment that lays out nothing. */
function laidOut(room: number) {
  vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(room);
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(
    function (this: HTMLElement) {
      const width = (this.textContent ?? "").length * 10;
      return { width } as DOMRect;
    },
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("a line of words that is never cut", () => {
  const words = ["Weir 3% · ffmpeg 41%", "ffmpeg 41%", "Weir 3%"];

  it("says the fullest words where the box has no width to measure", () => {
    render(<FitText words={words} />);

    expect(screen.getByText(words[0])).toBeInTheDocument();
  });

  it("says the fullest words that fit the box", () => {
    laidOut(120);
    render(<FitText words={words} />);

    expect(screen.getByText("ffmpeg 41%")).toBeInTheDocument();
  });

  it("says the shortest words where none fit", () => {
    laidOut(10);
    render(<FitText words={words} />);

    expect(screen.getByText("Weir 3%")).toBeInTheDocument();
  });

  it("gives the fullest words as its tooltip", () => {
    laidOut(120);
    render(<FitText words={words} />);

    expect(screen.getByText("ffmpeg 41%")).toHaveAttribute("title", words[0]);
  });

  it("measures again when the words change", () => {
    laidOut(120);
    const { rerender } = render(<FitText words={words} />);

    rerender(<FitText words={["x · y", "y"]} />);

    expect(screen.getByText("x · y")).toBeInTheDocument();
  });
});
