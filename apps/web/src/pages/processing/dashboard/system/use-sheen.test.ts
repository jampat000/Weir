import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { SHEEN_MS, useSheen } from "./use-sheen";

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("useSheen", () => {
  it("does not sweep for the first value it is given", () => {
    const { result } = renderHook(() => useSheen(100));
    expect(result.current).toBe(false);
  });

  it("sweeps for a moment each time the trigger changes", () => {
    const { result, rerender } = renderHook(({ at }) => useSheen(at), {
      initialProps: { at: 100 as number | null },
    });
    rerender({ at: 200 });
    expect(result.current).toBe(true);
    act(() => {
      vi.advanceTimersByTime(SHEEN_MS);
    });
    expect(result.current).toBe(false);
  });

  it("does not sweep when the trigger stays the same or has no value", () => {
    const { result, rerender } = renderHook(({ at }) => useSheen(at), {
      initialProps: { at: 100 as number | null },
    });
    rerender({ at: 100 });
    rerender({ at: null });
    expect(result.current).toBe(false);
  });
});
