import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { systemKeys } from "./query-keys";
import { useSystemTasksQuery } from "./system-tasks";
import type { SystemTask } from "./system-tasks-frame";

const REPLY = [
  {
    key: "scan-1",
    label: "Scan Movies",
    running: false,
    last_run_at: null,
    last_ok: true,
    last_error: null,
    next_run_at: null,
    interval_seconds: null,
  },
];
let deliver: ((tasks: SystemTask[]) => void) | null = null;
const stop = vi.fn();

vi.mock("../api/client", () => ({
  apiFetch: vi.fn(async () => ({ ok: true })),
  requireOk: vi.fn(async () => undefined),
  readJson: vi.fn(async () => REPLY),
}));
vi.mock("../activity/use-activity-stream-invalidation", () => ({
  subscribeSystemTasks: (subscriber: (tasks: SystemTask[]) => void) => {
    deliver = subscriber;
    return stop;
  },
}));

function renderTasks() {
  const client = new QueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, ...renderHook(() => useSystemTasksQuery(), { wrapper }) };
}

afterEach(() => {
  deliver = null;
  stop.mockClear();
});

describe("useSystemTasksQuery", () => {
  it("reads the task list once", async () => {
    const { result } = renderTasks();

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([
      expect.objectContaining({
        key: "scan-1",
        label: "Scan Movies",
        last_ok: true,
      }),
    ]);
  });

  it("replaces the list with the one a system.tasks frame carries", async () => {
    const { client, result } = renderTasks();
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    act(() => {
      deliver?.([
        {
          key: "scan-1",
          label: "Scan Movies",
          running: true,
          last_run_at: null,
          last_ok: null,
          last_error: null,
          next_run_at: null,
          interval_seconds: null,
        },
      ]);
    });

    expect(
      client.getQueryData<SystemTask[]>(systemKeys.tasks)?.[0].running,
    ).toBe(true);
  });

  it("is not read again on a timer: the stream carries what changes", async () => {
    const { client, result } = renderTasks();
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const query = client.getQueryCache().find({ queryKey: systemKeys.tasks });

    expect(query?.observers[0].options.refetchInterval).toBeFalsy();
  });

  it("stops listening to the stream when it unmounts", () => {
    const { unmount } = renderTasks();

    unmount();

    expect(stop).toHaveBeenCalledTimes(1);
  });
});
