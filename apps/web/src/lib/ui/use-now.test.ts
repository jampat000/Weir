import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useNow } from "./use-now";

function setHidden(hidden: boolean) {
  Object.defineProperty(document, "hidden", {
    configurable: true,
    get: () => hidden,
  });
  document.dispatchEvent(new Event("visibilitychange"));
}

describe("useNow", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    setHidden(false);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("moves forward on its own while the tab is visible", () => {
    const { result } = renderHook(() => useNow(1000));
    const first = result.current;

    act(() => {
      vi.advanceTimersByTime(3000);
    });

    expect(result.current).toBeGreaterThan(first);
  });

  it("stops ticking while the tab is hidden, and catches up as soon as it is shown again", () => {
    const { result } = renderHook(() => useNow(1000));
    setHidden(true);
    const whenHidden = result.current;

    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    expect(result.current).toBe(whenHidden);

    act(() => {
      vi.advanceTimersByTime(5_000);
      setHidden(false);
    });
    expect(result.current).toBeGreaterThan(whenHidden);
  });
});
