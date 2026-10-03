import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { connectionEntry } from "../../../lib/connections/connection-fixtures";
import { useConnectionTesting } from "./use-connection-testing";

const testConnection = vi.fn();
const noteConnectionActivity = vi.fn();

vi.mock("../../../lib/connections/use-connection-test", () => ({
  useConnectionTest: () => testConnection,
}));
vi.mock("../../../lib/connections/connection-lights", () => ({
  noteConnectionActivity: (frame: unknown) => noteConnectionActivity(frame),
}));

const radarr = connectionEntry();
const qbittorrent = connectionEntry({
  key: "download_client:2",
  kind: "download_client",
  id: 2,
});
const switchedOff = connectionEntry({
  key: "media_manager:3",
  id: 3,
  enabled: false,
  testable: false,
});

/** A test that finishes when the test says so. */
function pendingTest() {
  let finish: (answered: boolean) => void = () => undefined;
  testConnection.mockReturnValueOnce(
    new Promise<boolean>((resolve) => {
      finish = resolve;
    }),
  );
  return (answered: boolean) => act(async () => finish(answered));
}

beforeEach(() => {
  testConnection.mockReset().mockResolvedValue(true);
  noteConnectionActivity.mockReset();
});

describe("testing one connection", () => {
  it("marks it as being tested until the test ends", async () => {
    const { result } = renderHook(() => useConnectionTesting());
    const finish = pendingTest();

    act(() => void result.current.test(radarr));
    expect(result.current.testing.has(radarr.key)).toBe(true);
    await finish(true);

    expect(result.current.testing.size).toBe(0);
    expect(testConnection).toHaveBeenCalledWith(radarr);
  });

  it("lights the row green when it answered", async () => {
    const { result } = renderHook(() => useConnectionTesting());

    await act(() => result.current.test(radarr));

    expect(noteConnectionActivity).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: "media_manager",
        id: 1,
        phase: "answered",
        direction: "outbound",
      }),
    );
  });

  it("lights the row red when it did not", async () => {
    testConnection.mockResolvedValue(false);
    const { result } = renderHook(() => useConnectionTesting());

    await act(() => result.current.test(radarr));

    expect(noteConnectionActivity).toHaveBeenCalledWith(
      expect.objectContaining({ phase: "failed" }),
    );
  });
});

describe("testing every connection", () => {
  it("tests each one that can be tested, and none that is switched off", async () => {
    const { result } = renderHook(() => useConnectionTesting());

    await act(() => result.current.testAll([radarr, qbittorrent, switchedOff]));

    expect(testConnection).toHaveBeenCalledTimes(2);
    expect(testConnection).not.toHaveBeenCalledWith(switchedOff);
  });

  it("is busy until the last test ends", async () => {
    const { result } = renderHook(() => useConnectionTesting());
    const finishFirst = pendingTest();
    const finishSecond = pendingTest();

    act(() => void result.current.testAll([radarr, qbittorrent]));
    expect(result.current.allBusy).toBe(true);
    expect(result.current.testing.size).toBe(2);
    await finishFirst(true);
    expect(result.current.allBusy).toBe(true);
    await finishSecond(false);

    expect(result.current.allBusy).toBe(false);
    expect(result.current.testing.size).toBe(0);
  });
});
