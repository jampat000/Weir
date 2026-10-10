import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { useLiveProgress } from "../activity/use-activity-stream-invalidation";
import { systemKeys } from "./query-keys";
import { fetchSystemOverview, fetchSystemStats } from "./system-stats-api";
import type {
  SystemNow,
  SystemOverview,
  SystemPoint,
  SystemStats,
} from "./system-stats-types";
import {
  useSystemOverviewQuery,
  useSystemStatsFrames,
  useSystemStatsQuery,
} from "./use-system-stats";

vi.mock("./system-stats-api", () => ({
  fetchSystemStats: vi.fn(),
  fetchSystemOverview: vi.fn(),
}));

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

const drive = (freeBytes: number) => ({
  name: "D:",
  path: "D:\\",
  total_bytes: 1000,
  free_bytes: freeBytes,
  weir_bytes: 0,
  keep_free_bytes: 0,
  full_in_days: null,
  read_bytes_per_sec: null,
  write_bytes_per_sec: null,
  busy_percent: null,
  workflows: [],
});

const frame = (
  reading: SystemNow,
  added: SystemPoint,
  drives: ReturnType<typeof drive>[] = [],
) => ({
  now: reading,
  point: added,
  machine: { os: "Windows 11", uptime_seconds: 60, reboot_pending: false },
  drives,
});

const seeded = (): SystemStats => ({
  interval_ms: 1000,
  window_s: 600,
  now: now(1),
  history: [point(1)],
  machine: { os: "", uptime_seconds: 0, reboot_pending: null },
  drives: [],
});

const overview = (jobsRun: number): SystemOverview => ({
  version: "1.0.0",
  update: { status: "up_to_date", latest_version: null },
  uptime_seconds: 60,
  started_at: "2026-10-02T11:59:00Z",
  runs_as: "app",
  address: "http://pc:8484",
  data_bytes: 1000,
  browsers_live: 1,
  requests: { median_ms: 10, p95_ms: 40, errors_today: 0 },
  jobs_today: { run: jobsRun, failed: 0 },
  restarts_this_week: 0,
  checks: { passing: 5, total: 5 },
  last_update_backup: null,
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

    FakeEventSource.instances[0].emit("system.stats", frame(now(77), point(2)));

    const stats = client.getQueryData<SystemStats>(systemKeys.stats);
    expect(stats?.history).toHaveLength(2);
    expect(stats?.now.cpu_percent).toBe(77);
  });

  it("takes the drives the frame carries, so they follow the stream without a request", () => {
    const { client, wrapper } = setup(seeded());
    renderHook(() => useSystemStatsFrames(), { wrapper });

    FakeEventSource.instances[0].emit(
      "system.stats",
      frame(now(77), point(2), [drive(400)]),
    );

    const stats = client.getQueryData<SystemStats>(systemKeys.stats);
    expect(stats?.drives.map((d) => d.free_bytes)).toEqual([400]);
  });

  it("leaves the cache empty until the history has been read, rather than starting one from a single point", () => {
    const { client, wrapper } = setup();
    renderHook(() => useSystemStatsFrames(), { wrapper });

    FakeEventSource.instances[0].emit("system.stats", frame(now(77), point(2)));

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

describe("following the system.overview frames", () => {
  it("reads the overview once, then takes each frame as the new overview", async () => {
    vi.mocked(fetchSystemOverview).mockResolvedValue(overview(1));
    const { client, wrapper } = setup();
    const { result } = renderHook(() => useSystemOverviewQuery(), { wrapper });
    await waitFor(() => expect(result.current.data?.jobs_today.run).toBe(1));

    act(() => {
      FakeEventSource.instances[0].emit("system.overview", overview(7));
    });

    expect(
      client.getQueryData<SystemOverview>(systemKeys.overview)?.jobs_today.run,
    ).toBe(7);
    expect(fetchSystemOverview).toHaveBeenCalledTimes(1);
  });

  it("ignores a frame that is not an overview", async () => {
    vi.mocked(fetchSystemOverview).mockResolvedValue(overview(1));
    const { result } = renderHook(() => useSystemOverviewQuery(), setup());
    await waitFor(() => expect(result.current.data).toBeDefined());

    act(() => {
      FakeEventSource.instances[0].emit("system.overview", { version: "x" });
    });

    expect(result.current.data?.jobs_today.run).toBe(1);
  });
});

describe("the System readings", () => {
  it("are not read again on a timer: the stream carries what changes", async () => {
    vi.mocked(fetchSystemOverview).mockResolvedValue(overview(1));
    vi.mocked(fetchSystemStats).mockResolvedValue(seeded());
    const { client, wrapper } = setup();
    const both = renderHook(
      () => [useSystemOverviewQuery(), useSystemStatsQuery()] as const,
      { wrapper },
    );
    await waitFor(() => expect(both.result.current[1].data).toBeDefined());

    const intervals = client
      .getQueryCache()
      .findAll()
      .map((query) => query.observers[0].options.refetchInterval);

    expect(intervals).toHaveLength(2);
    expect(intervals.every((interval) => !interval)).toBe(true);
  });
});
