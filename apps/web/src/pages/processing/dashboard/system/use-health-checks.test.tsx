import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { ConnectionEntry } from "../../../../lib/connections/connection-model";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import type { SystemDrive } from "../../../../lib/system/system-stats-types";
import type { Health } from "../use-health";
import type { HealthCheck } from "./health-checks";
import { useHealthChecks } from "./use-health-checks";

const NOW = Date.parse("2026-10-02T12:00:00Z");
const movies = { id: 2, name: "Movies" } as ProcessingLibrary;
const tv = { id: 3, name: "TV" } as ProcessingLibrary;

const recheckMovies = vi.fn(async () => undefined);
const health: Health = {
  workflows: [],
  managers: [],
  downloadClients: [],
  tools: null,
  recheckFolders: vi.fn(),
};
const testConnection = vi.fn(async () => undefined);
const refetch = () => vi.fn(async () => undefined);
const queries = {
  tools: { dataUpdatedAt: NOW, refetch: refetch() },
  settings: {
    data: undefined as Record<string, unknown> | undefined,
    dataUpdatedAt: NOW,
    refetch: refetch(),
  },
  backups: {
    data: undefined as { items: { created_at: string }[] } | undefined,
    refetch: refetch(),
  },
  readiness: {
    data: undefined as Record<string, unknown> | undefined,
    dataUpdatedAt: NOW,
    refetch: refetch(),
  },
  update: {
    data: undefined as Record<string, unknown> | undefined,
    refetch: refetch(),
  },
  overview: {
    data: undefined as
      { checks: { passing: number; total: number } } | undefined,
  },
};
let entries: ConnectionEntry[] = [];
let drives: SystemDrive[] = [];

vi.mock("../use-health", () => ({ useHealth: () => health }));
vi.mock("../../../../lib/connections/use-connections", () => ({
  useConnections: () => ({ entries, lights: new Map() }),
}));
vi.mock("../use-connection-testing", () => ({
  useConnectionTesting: () => ({ test: testConnection }),
}));
vi.mock("../../../../lib/system/media-tools", () => ({
  useMediaToolsQuery: () => queries.tools,
}));
vi.mock("../../../../lib/settings/queries", () => ({
  useAppSettingsQuery: () => queries.settings,
  useConfigurationBackupsQuery: () => queries.backups,
  useUpdateStatusQuery: () => queries.update,
}));
vi.mock("../../../../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => queries.readiness,
}));
vi.mock("../../../../lib/system/use-system-stats", () => ({
  useSystemOverviewQuery: () => queries.overview,
}));
vi.mock("../../../../lib/system/system-stats-api", () => ({
  fetchSystemStats: async () => ({ drives }),
}));

function wrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
  };
}

function drive(name: string, workflowIds: number[]): SystemDrive {
  return {
    name,
    path: `${name}\\`,
    total_bytes: 2_000_000_000_000,
    free_bytes: 1_000_000_000_000,
    weir_bytes: 0,
    keep_free_bytes: 0,
    full_in_days: null,
    read_bytes_per_sec: null,
    write_bytes_per_sec: null,
    busy_percent: null,
    workflows: workflowIds.map((id) => ({ id, name: `w${id}`, roles: [] })),
  };
}

function renderChecks(workflowId: number | null = null) {
  return renderHook(() => useHealthChecks([movies, tv], workflowId), {
    wrapper: wrapper(),
  });
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(NOW);
  health.workflows = [
    {
      workflow: movies,
      verdict: { words: "In sync", tone: "healthy" },
      why: null,
      chain: undefined,
      checkedAt: NOW,
      recheck: recheckMovies,
    },
  ];
  health.tools = null;
  entries = [];
  drives = [];
  queries.settings.data = undefined;
  queries.backups.data = undefined;
  queries.readiness.data = undefined;
  queries.update.data = undefined;
  queries.overview.data = undefined;
  for (const mock of [
    recheckMovies,
    testConnection,
    queries.tools.refetch,
    queries.settings.refetch,
    queries.backups.refetch,
    queries.readiness.refetch,
    queries.update.refetch,
  ]) {
    mock.mockClear();
  }
});

afterEach(() => {
  vi.useRealTimers();
});

describe("useHealthChecks", () => {
  it("makes a check of each switched-on workflow", () => {
    const { result } = renderChecks();

    expect(result.current.checks.map((check) => check.id)).toEqual([
      "workflow:2",
    ]);
  });

  it("says it is checking while a folder check has not answered", () => {
    health.workflows = [
      {
        ...health.workflows[0],
        verdict: { words: "Checking…", tone: "neutral" },
      },
    ];
    queries.overview.data = { checks: { passing: 9, total: 10 } };
    const { result } = renderChecks();

    expect(result.current.checking).toBe(true);
    expect(result.current.overviewChecks).toEqual({ passing: 9, total: 10 });
  });

  it("judges the backup from the schedule and the newest backup, made by hand or by itself", () => {
    queries.settings.data = {
      configuration_backup_enabled: true,
      configuration_backup_interval_hours: 24,
      configuration_backup_last_run_at: "2026-09-20T03:00:00Z",
    };
    queries.backups.data = { items: [{ created_at: "2026-10-02T03:00:00Z" }] };
    const { result } = renderChecks();

    const backup = result.current.checks.find(
      (check) => check.area === "backups",
    );
    expect(backup?.tone).toBe("ok");
  });

  it("reads Weir's workers and a waiting update from readiness and the update status", () => {
    queries.readiness.data = {
      worker_health: [
        { stopped_workers: 1, stale_workers: 0, detail: "Cleanup stopped." },
        { stopped_workers: 0, stale_workers: 0, detail: "Fine." },
      ],
    };
    queries.update.data = {
      status: "update_available",
      latest_version: "3.3.0",
    };
    const { result } = renderChecks();

    const weir = result.current.checks.filter((check) => check.area === "weir");
    expect(weir.map((check) => [check.id, check.tone])).toEqual([
      ["weir:workers", "bad"],
      ["weir:update", "note"],
    ]);
  });

  it("checks the drives, narrowed to the workflow when one is chosen", async () => {
    drives = [drive("D:", [2]), drive("E:", [3])];
    const { result } = renderChecks(2);

    await waitFor(() =>
      expect(
        result.current.checks.filter((check) => check.area === "storage"),
      ).toHaveLength(1),
    );
    expect(
      result.current.checks.find((check) => check.area === "storage")?.title,
    ).toBe("D:");
  });

  it("looks at a workflow again by reading its folder chain again", async () => {
    const { result } = renderChecks();
    const target = result.current.checks[0];

    await act(async () => {
      await result.current.again(target);
    });

    expect(recheckMovies).toHaveBeenCalledTimes(1);
  });

  it("looks at a connection again by testing it", async () => {
    const entry = {
      key: "media_manager:1",
      enabled: true,
      state: "ok",
    } as ConnectionEntry;
    entries = [entry];
    const { result } = renderChecks();
    const target = result.current.checks.find(
      (check) => check.area === "connections",
    ) as HealthCheck;

    await act(async () => {
      await result.current.again(target);
    });

    expect(testConnection).toHaveBeenCalledWith(entry);
  });

  it("looks at the backups and at Weir again by reading them again", async () => {
    const { result } = renderChecks();
    const again = (area: HealthCheck["again"]["area"]) =>
      act(async () => {
        await result.current.again({
          again: { area, key: null },
          id: area,
        } as HealthCheck);
      });

    await again("backups");
    await again("weir");
    await again("tools");

    expect(queries.settings.refetch).toHaveBeenCalled();
    expect(queries.backups.refetch).toHaveBeenCalled();
    expect(queries.readiness.refetch).toHaveBeenCalled();
    expect(queries.update.refetch).toHaveBeenCalled();
    expect(queries.tools.refetch).toHaveBeenCalled();
  });

  it("marks a check as being looked at while it is, and clears it after", async () => {
    let finish: () => void = () => undefined;
    recheckMovies.mockImplementationOnce(
      () =>
        new Promise<undefined>((resolve) => {
          finish = () => resolve(undefined);
        }),
    );
    const { result } = renderChecks();
    const target = result.current.checks[0];

    let pending: Promise<void> = Promise.resolve();
    act(() => {
      pending = result.current.again(target);
    });
    expect(result.current.busy.has("workflow:2")).toBe(true);

    await act(async () => {
      finish();
      await pending;
    });
    expect(result.current.busy.size).toBe(0);
  });
});
