import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { reportLiveConnection, resetLiveConnection } from "./live-connection";
import {
  FIRST_CONNECTION_GRACE_MS,
  LOST_CONNECTION_GRACE_MS,
  useLiveUpdatesPaused,
} from "./use-live-updates-paused";

beforeEach(() => vi.useFakeTimers());

afterEach(() => {
  resetLiveConnection();
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("useLiveUpdatesPaused", () => {
  it("is quiet while the page is making its first connection", () => {
    const { result } = renderHook(() => useLiveUpdatesPaused());

    expect(result.current).toBe(false);
  });

  it("is quiet while the connection is up", () => {
    const { result } = renderHook(() => useLiveUpdatesPaused());

    act(() => void reportLiveConnection("opened"));

    expect(result.current).toBe(false);
  });

  it("speaks once a connection that was up has stayed lost for a few seconds, and stops when it is back", () => {
    const { result } = renderHook(() => useLiveUpdatesPaused());
    act(() => void reportLiveConnection("opened"));

    act(() => void reportLiveConnection("dropped"));
    expect(result.current).toBe(false);

    act(() => void vi.advanceTimersByTime(LOST_CONNECTION_GRACE_MS - 1));
    expect(result.current).toBe(false);

    act(() => void vi.advanceTimersByTime(1));
    expect(result.current).toBe(true);

    act(() => void reportLiveConnection("opened"));
    expect(result.current).toBe(false);
  });

  it("says nothing about a blip that mends within the grace, and counts the next loss from its own start", () => {
    const { result } = renderHook(() => useLiveUpdatesPaused());
    act(() => void reportLiveConnection("opened"));

    act(() => void reportLiveConnection("dropped"));
    act(() => void vi.advanceTimersByTime(LOST_CONNECTION_GRACE_MS - 1));
    act(() => void reportLiveConnection("opened"));
    act(() => void vi.advanceTimersByTime(LOST_CONNECTION_GRACE_MS));
    expect(result.current).toBe(false);

    act(() => void reportLiveConnection("dropped"));
    act(() => void vi.advanceTimersByTime(LOST_CONNECTION_GRACE_MS - 1));
    expect(result.current).toBe(false);
  });

  it("speaks at once when the browser knows it is offline", () => {
    vi.spyOn(navigator, "onLine", "get").mockReturnValue(false);
    const { result } = renderHook(() => useLiveUpdatesPaused());
    act(() => void reportLiveConnection("opened"));

    act(() => void reportLiveConnection("dropped"));
    act(() => void vi.advanceTimersByTime(0));

    expect(result.current).toBe(true);
  });

  it("gives a first connection that fails a few seconds before it speaks", () => {
    const { result } = renderHook(() => useLiveUpdatesPaused());

    act(() => void reportLiveConnection("dropped"));
    expect(result.current).toBe(false);

    act(() => void vi.advanceTimersByTime(FIRST_CONNECTION_GRACE_MS - 1));
    expect(result.current).toBe(false);

    act(() => void vi.advanceTimersByTime(1));
    expect(result.current).toBe(true);

    act(() => void reportLiveConnection("opened"));
    expect(result.current).toBe(false);
  });
});
