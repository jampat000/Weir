import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { useLiveProgress } from "../activity/use-activity-stream-invalidation";
import { systemKeys } from "./query-keys";
import type { SystemNow, SystemPoint, SystemStats } from "./system-stats-types";
import { useSystemStatsFrames } from "./use-system-stats";

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  listeners = new Map<string, Set<(ev: MessageEvent<string>) => void>>();
  closed = false;

  constructor() {
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, cb: (ev: MessageEvent<string>) => void) {
    const set = this.listeners.get(type) ?? new Set();
    set.add(cb);
    this.listeners.set(type, set);
  }

  removeEventListener() {}

  close() {
    this.closed = true;
  }

  emit(type: string, data: unknown) {
    const ev = { data: JSON.stringify(data) } as MessageEvent<string>;
    this.listeners.get(type)?.forEach((cb) => cb(ev));
  }
}

const now = (cpu: number): SystemNow => ({
  at: "2026-10-02T12:00:02Z",
  cpu_percent: cpu,
  cores: 4,
  memory_used_bytes: 1,
  memory_total_bytes: 2,
  disk_read_bytes_per_sec: null,
  disk_write_bytes_per_sec: null,
  disk_busy_percent: null,
  weir_cpu_percent: null,
  weir_memory_bytes: 5,
  tools_cpu_percent: null,
  processing_read_bytes_per_sec: 0,
  processing_write_bytes_per_sec: 0,
  processing_speed: 0,
  running: 0,
  slots: 2,
});

const point = (second: number): SystemPoint => ({
  at: `2026-10-02T12:00:0${second}Z`,
  cpu_percent: 50,
  memory_percent: 10,
  disk_read_bytes_per_sec: null,
  disk_write_bytes_per_sec: null,
  processing_read_bytes_per_sec: 0,
  processing_write_bytes_per_sec: 0,
  processing_speed: 0,
});

const seeded = (): SystemStats => ({
  interval_ms: 1000,
  window_s: 600,
  now: now(1),
  history: [point(1)],
  machine: { os: "", uptime_seconds: 0, reboot_pending: null },
  drives: [],
});

function setup(stats?: SystemStats) {
  vi.stubGlobal(
    "EventSource",
    FakeEventSource as unknown as typeof EventSource,
  );
  const client = new QueryClient();
  if (stats) client.setQueryData(systemKeys.stats, stats);
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

afterEach(() => {
  FakeEventSource.instances = [];
  vi.unstubAllGlobals();
});

describe("following the system.stats frames", () => {
  it("adds each frame's point to the history and takes its reading", () => {
    const { client, wrapper } = setup(seeded());
    renderHook(() => useSystemStatsFrames(), { wrapper });

    FakeEventSource.instances[0].emit("system.stats", {
      now: now(77),
      point: point(2),
    });

    const stats = client.getQueryData<SystemStats>(systemKeys.stats);
    expect(stats?.history).toHaveLength(2);
    expect(stats?.now.cpu_percent).toBe(77);
  });

  it("leaves the cache empty until the history has been read, rather than starting one from a single point", () => {
    const { client, wrapper } = setup();
    renderHook(() => useSystemStatsFrames(), { wrapper });

    FakeEventSource.instances[0].emit("system.stats", {
      now: now(77),
      point: point(2),
    });

    expect(client.getQueryData(systemKeys.stats)).toBeUndefined();
  });

  it("uses the one stream the other readers share, and closes it with the last", () => {
    const { wrapper } = setup(seeded());
    const progress = renderHook(() => useLiveProgress());
    const frames = renderHook(() => useSystemStatsFrames(), { wrapper });

    expect(FakeEventSource.instances).toHaveLength(1);
    frames.unmount();
    expect(FakeEventSource.instances[0].closed).toBe(false);
    progress.unmount();
    expect(FakeEventSource.instances[0].closed).toBe(true);
  });
});
