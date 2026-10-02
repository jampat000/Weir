import { act, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import { useMediaQuery } from "./use-media-query";

function Probe() {
  return <p>{useMediaQuery("(min-width: 1024px)") ? "wide" : "narrow"}</p>;
}

afterEach(() => vi.unstubAllGlobals());

it("says whether the window matches, and follows it as the window changes", () => {
  let matches = false;
  let notify: () => void = () => undefined;
  vi.stubGlobal("matchMedia", () => ({
    get matches() {
      return matches;
    },
    addEventListener: (_event: string, listener: () => void) => {
      notify = listener;
    },
    removeEventListener: () => undefined,
  }));
  render(<Probe />);
  expect(screen.getByText("narrow")).toBeInTheDocument();

  matches = true;
  act(() => notify());

  expect(screen.getByText("wide")).toBeInTheDocument();
});

it("says no where the window cannot answer", () => {
  vi.stubGlobal("matchMedia", undefined);

  render(<Probe />);

  expect(screen.getByText("narrow")).toBeInTheDocument();
});
