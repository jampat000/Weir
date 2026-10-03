import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  RESIZING_CLASS,
  useStillWhileResizing,
} from "./use-still-while-resizing";

const SETTLE_MS = 350;

type FakeAnimation = {
  animationName?: string;
  effect: { getComputedTiming: () => { iterations: number } };
  finish: ReturnType<typeof vi.fn>;
};

function animation(
  iterations: number,
  named: boolean,
  seenWhileResizing: boolean[],
): FakeAnimation {
  const fake: FakeAnimation = {
    effect: { getComputedTiming: () => ({ iterations }) },
    finish: vi.fn(() => {
      seenWhileResizing.push(document.body.classList.contains(RESIZING_CLASS));
    }),
  };
  if (named) fake.animationName = "mm-shelf-arrive";
  return fake;
}

function resizeWindow() {
  act(() => {
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      value: window.innerWidth + 40,
    });
    window.dispatchEvent(new Event("resize"));
  });
}

afterEach(() => {
  vi.useRealTimers();
  Reflect.deleteProperty(document, "getAnimations");
});

describe("what the page's animations do when a resize ends", () => {
  it("finishes the animations that end, once the class is off, so none of them plays again", () => {
    vi.useFakeTimers();
    const classOnWhenFinished: boolean[] = [];
    const arrival = animation(1, true, classOnWhenFinished);
    const sheen = animation(Infinity, true, classOnWhenFinished);
    const glide = animation(1, false, classOnWhenFinished);
    document.getAnimations = () =>
      [arrival, sheen, glide] as unknown as Animation[];
    renderHook(() => useStillWhileResizing());

    resizeWindow();
    act(() => {
      vi.advanceTimersByTime(SETTLE_MS);
    });

    expect(arrival.finish).toHaveBeenCalledTimes(1);
    expect(classOnWhenFinished).toEqual([false]);
    // A sheen that runs forever cannot be finished, and a transition is not an animation of the page's own.
    expect(sheen.finish).not.toHaveBeenCalled();
    expect(glide.finish).not.toHaveBeenCalled();
  });

  it("holds the class through the settling time and takes it off after", () => {
    vi.useFakeTimers();
    renderHook(() => useStillWhileResizing());

    resizeWindow();
    act(() => {
      vi.advanceTimersByTime(SETTLE_MS - 1);
    });
    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(true);
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(false);
  });

  it("copes with a browser that cannot list animations", () => {
    vi.useFakeTimers();
    renderHook(() => useStillWhileResizing());

    resizeWindow();
    act(() => {
      vi.advanceTimersByTime(SETTLE_MS);
    });

    expect(document.body.classList.contains(RESIZING_CLASS)).toBe(false);
  });
});
