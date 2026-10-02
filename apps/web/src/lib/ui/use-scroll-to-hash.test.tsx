import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import { useScrollToHash } from "./use-scroll-to-hash";

const scrollIntoView = vi.fn();

function Page() {
  useScrollToHash();
  return <p id="target">Target</p>;
}

function renderAt(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Page />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  Element.prototype.scrollIntoView = scrollIntoView;
});

afterEach(() => {
  scrollIntoView.mockReset();
  vi.unstubAllGlobals();
});

it("brings the element the fragment names into view", () => {
  renderAt("/page#target");

  expect(scrollIntoView).toHaveBeenCalledWith({ block: "center" });
});

it("does nothing when the address has no fragment", () => {
  renderAt("/page");

  expect(scrollIntoView).not.toHaveBeenCalled();
});

it("does nothing when the fragment names no element", () => {
  renderAt("/page#elsewhere");

  expect(scrollIntoView).not.toHaveBeenCalled();
});

it("brings it into view again as the page grows, until the person scrolls", () => {
  let grew: () => void = () => undefined;
  vi.stubGlobal(
    "ResizeObserver",
    class {
      constructor(callback: () => void) {
        grew = callback;
      }
      observe() {}
      disconnect() {}
    },
  );
  renderAt("/page#target");
  scrollIntoView.mockClear();

  grew();

  expect(scrollIntoView).toHaveBeenCalledOnce();
});
