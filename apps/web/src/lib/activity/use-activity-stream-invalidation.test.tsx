import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

import { activityKeys } from "./query-keys";
import {
  useActivityStreamInvalidations,
  useLiveProgress,
} from "./use-activity-stream-invalidation";
import { processingKeys } from "../processing/query-keys";

class FakeEventSource {
  url: string;
  listeners = new Map<string, Set<(ev: MessageEvent<string>) => void>>();
  closed = false;

  constructor(url: string) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, cb: (ev: MessageEvent<string>) => void): void {
    const set = this.listeners.get(type) ?? new Set();
    set.add(cb);
    this.listeners.set(type, set);
  }

  removeEventListener(
    type: string,
    cb: (ev: MessageEvent<string>) => void,
  ): void {
    this.listeners.get(type)?.delete(cb);
  }

  close(): void {
    this.closed = true;
  }

  emit(type: string, data: string): void {
    const ev = { data } as MessageEvent<string>;
    this.listeners.get(type)?.forEach((cb) => cb(ev));
  }

  static instances: FakeEventSource[] = [];
}

function withQueryClient(qc: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  };
}

function setVisibility(state: DocumentVisibilityState): void {
  vi.spyOn(document, "visibilityState", "get").mockReturnValue(state);
}

const RECENT_KEYS = [activityKeys.recent] as const;
const CANCEL_REFETCH_FALSE = { cancelRefetch: false };

describe("useActivityStreamInvalidations", () => {
  afterEach(() => {
    FakeEventSource.instances = [];
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    document.dispatchEvent(new Event("visibilitychange"));
  });

  it("coalesces event bursts and invalidates only exact queries, without cancelling an in-flight fetch", () => {
    vi.useFakeTimers();
    vi.setSystemTime(10_000);
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");
    const keys = [processingKeys.overviewStats(), activityKeys.recent] as const;

    renderHook(
      () =>
        useActivityStreamInvalidations(keys, {
          exact: true,
          throttleMs: 1_000,
        }),
      { wrapper: withQueryClient(qc) },
    );

    const src = FakeEventSource.instances[0];
    src.emit("activity.latest", JSON.stringify({ latest_event_id: 1 }));
    src.emit("activity.latest", JSON.stringify({ latest_event_id: 2 }));
    src.emit("activity.latest", JSON.stringify({ latest_event_id: 3 }));

    expect(spy).toHaveBeenCalledTimes(2);
    expect(spy).toHaveBeenCalledWith(
      { queryKey: processingKeys.overviewStats(), exact: true },
      CANCEL_REFETCH_FALSE,
    );
    expect(spy).toHaveBeenCalledWith(
      { queryKey: activityKeys.recent, exact: true },
      CANCEL_REFETCH_FALSE,
    );

    vi.advanceTimersByTime(1_000);
    expect(spy).toHaveBeenCalledTimes(4);
  });

  it("invalidates activity recent query on activity.latest", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");

    renderHook(() => useActivityStreamInvalidations(RECENT_KEYS), {
      wrapper: withQueryClient(qc),
    });

    const src = FakeEventSource.instances[0];
    expect(src.url).toBe("/api/v1/activity/stream");
    src.emit("activity.latest", JSON.stringify({ latest_event_id: 12 }));

    expect(spy).toHaveBeenCalledWith(
      { queryKey: activityKeys.recent, exact: false },
      CANCEL_REFETCH_FALSE,
    );
  });

  it("invalidates when an existing activity row receives a newer revision, even at the same event id", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");

    renderHook(() => useActivityStreamInvalidations(RECENT_KEYS), {
      wrapper: withQueryClient(qc),
    });

    const src = FakeEventSource.instances[0];
    src.emit(
      "activity.latest",
      JSON.stringify({ latest_event_id: 12, activity_revision: 1 }),
    );
    src.emit(
      "activity.latest",
      JSON.stringify({ latest_event_id: 12, activity_revision: 2 }),
    );

    expect(spy).toHaveBeenCalledTimes(2);
  });

  it("ignores a message that repeats the same event id and revision already seen", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");

    renderHook(() => useActivityStreamInvalidations(RECENT_KEYS), {
      wrapper: withQueryClient(qc),
    });

    const src = FakeEventSource.instances[0];
    src.emit(
      "activity.latest",
      JSON.stringify({ latest_event_id: 12, activity_revision: 2 }),
    );
    src.emit(
      "activity.latest",
      JSON.stringify({ latest_event_id: 12, activity_revision: 2 }),
    );
    src.emit("activity.latest", JSON.stringify({ latest_event_id: 11 }));

    expect(spy).toHaveBeenCalledTimes(1);
  });

  it("ignores malformed stream messages instead of breaking live updates", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");

    renderHook(() => useActivityStreamInvalidations(RECENT_KEYS), {
      wrapper: withQueryClient(qc),
    });

    const src = FakeEventSource.instances[0];
    src.emit("activity.latest", "{");
    src.emit("activity.latest", JSON.stringify({ activity_revision: 1 }));
    src.emit(
      "activity.latest",
      JSON.stringify({ latest_event_id: 12, activity_revision: 2 }),
    );

    expect(spy).toHaveBeenCalledTimes(1);
  });

  it("invalidates the overview stats query on activity.latest", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");

    renderHook(
      () => useActivityStreamInvalidations([processingKeys.overviewStats()]),
      {
        wrapper: withQueryClient(qc),
      },
    );

    const src = FakeEventSource.instances[0];
    src.emit("activity.latest", JSON.stringify({ latest_event_id: 77 }));

    expect(spy).toHaveBeenCalledWith(
      { queryKey: processingKeys.overviewStats(), exact: false },
      CANCEL_REFETCH_FALSE,
    );
  });

  it("shares one EventSource across multiple query subscribers", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");

    const first = renderHook(
      () => useActivityStreamInvalidations(RECENT_KEYS),
      {
        wrapper: withQueryClient(qc),
      },
    );
    const second = renderHook(
      () => useActivityStreamInvalidations([processingKeys.overviewStats()]),
      {
        wrapper: withQueryClient(qc),
      },
    );

    expect(FakeEventSource.instances).toHaveLength(1);
    const src = FakeEventSource.instances[0];
    src.emit("activity.latest", JSON.stringify({ latest_event_id: 88 }));

    expect(spy).toHaveBeenCalledWith(
      { queryKey: activityKeys.recent, exact: false },
      CANCEL_REFETCH_FALSE,
    );
    expect(spy).toHaveBeenCalledWith(
      { queryKey: processingKeys.overviewStats(), exact: false },
      CANCEL_REFETCH_FALSE,
    );

    first.unmount();
    expect(src.closed).toBe(false);

    second.unmount();
    expect(src.closed).toBe(true);
  });

  it("opens a fresh EventSource after all subscribers unmount", async () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();

    const first = renderHook(
      () => useActivityStreamInvalidations(RECENT_KEYS),
      {
        wrapper: withQueryClient(qc),
      },
    );
    first.unmount();
    await waitFor(() => expect(FakeEventSource.instances[0].closed).toBe(true));

    renderHook(() => useActivityStreamInvalidations(RECENT_KEYS), {
      wrapper: withQueryClient(qc),
    });

    expect(FakeEventSource.instances).toHaveLength(2);
    expect(FakeEventSource.instances[1].closed).toBe(false);
  });

  it("closes the connection while the tab is hidden", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();

    renderHook(() => useActivityStreamInvalidations(RECENT_KEYS), {
      wrapper: withQueryClient(qc),
    });
    const src = FakeEventSource.instances[0];

    setVisibility("hidden");
    document.dispatchEvent(new Event("visibilitychange"));

    expect(src.closed).toBe(true);
  });

  it("reopens the connection once the tab becomes visible again", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();

    renderHook(() => useActivityStreamInvalidations(RECENT_KEYS), {
      wrapper: withQueryClient(qc),
    });

    setVisibility("hidden");
    document.dispatchEvent(new Event("visibilitychange"));
    setVisibility("visible");
    document.dispatchEvent(new Event("visibilitychange"));

    expect(FakeEventSource.instances).toHaveLength(2);
    expect(FakeEventSource.instances[1].closed).toBe(false);
  });
});

describe("useLiveProgress", () => {
  afterEach(() => {
    FakeEventSource.instances = [];
    vi.unstubAllGlobals();
    document.dispatchEvent(new Event("visibilitychange"));
  });

  it("starts empty and fills in from the stream's processing.progress frame", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const { result } = renderHook(() => useLiveProgress());
    expect(result.current).toEqual({});

    const src = FakeEventSource.instances[0];
    act(() => {
      src.emit(
        "processing.progress",
        JSON.stringify({
          files: [
            {
              relative_path: "Film/Film.mkv",
              status: "processing",
              percent: 42.5,
              eta_seconds: 12,
              message: "Weir is writing the cleaned-up file.",
              speed: "148x",
              elapsed_seconds: 9,
              removed_audio: ["fre aac 2ch: removed"],
              removed_subtitles: [],
            },
          ],
        }),
      );
    });

    expect(result.current["Film/Film.mkv"]).toEqual({
      relativePath: "Film/Film.mkv",
      status: "processing",
      percent: 42.5,
      etaSeconds: 12,
      message: "Weir is writing the cleaned-up file.",
      speed: "148x",
      elapsedSeconds: 9,
      removedAudio: ["fre aac 2ch: removed"],
      removedSubtitles: [],
    });
  });

  it("replaces the whole snapshot, so a file the stream no longer mentions disappears", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const { result } = renderHook(() => useLiveProgress());
    const src = FakeEventSource.instances[0];

    act(() => {
      src.emit(
        "processing.progress",
        JSON.stringify({
          files: [
            {
              relative_path: "Film/Film.mkv",
              status: "processing",
              percent: 10,
            },
          ],
        }),
      );
    });
    expect(result.current["Film/Film.mkv"]).toBeDefined();

    act(() => {
      src.emit("processing.progress", JSON.stringify({ files: [] }));
    });

    expect(result.current["Film/Film.mkv"]).toBeUndefined();
  });

  it("ignores a malformed frame instead of breaking live progress", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const { result } = renderHook(() => useLiveProgress());
    const src = FakeEventSource.instances[0];

    act(() => {
      src.emit("processing.progress", "not json");
    });

    expect(result.current).toEqual({});
  });

  it("shares its connection with an invalidation subscriber, and closes only once both are gone", () => {
    vi.stubGlobal(
      "EventSource",
      FakeEventSource as unknown as typeof EventSource,
    );
    const qc = new QueryClient();
    const progress = renderHook(() => useLiveProgress());
    const invalidation = renderHook(
      () => useActivityStreamInvalidations([activityKeys.recent]),
      { wrapper: withQueryClient(qc) },
    );

    expect(FakeEventSource.instances).toHaveLength(1);
    const src = FakeEventSource.instances[0];

    invalidation.unmount();
    expect(src.closed).toBe(false);

    progress.unmount();
    expect(src.closed).toBe(true);
  });
});
