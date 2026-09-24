import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { UpdateReadyNotice } from "./update-ready-notice";

const mocks = vi.hoisted(() => ({
  useUpdateStateQuery: vi.fn(),
  useApplyUpdateMutation: vi.fn(),
}));

vi.mock("../../../../lib/settings/queries", () => ({
  useUpdateStateQuery: (...args: unknown[]) =>
    mocks.useUpdateStateQuery(...args),
  useApplyUpdateMutation: () => mocks.useApplyUpdateMutation(),
}));

describe("UpdateReadyNotice", () => {
  beforeEach(() => {
    mocks.useUpdateStateQuery.mockReturnValue({
      data: { downloaded: true, pending_version: "2.0.8" },
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("says the page will reload itself once the restart is signalled", () => {
    mocks.useApplyUpdateMutation.mockReturnValue({
      isPending: false,
      isError: false,
      isSuccess: true,
      mutate: vi.fn(),
    });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ json: () => Promise.resolve({}) }),
    );

    render(<UpdateReadyNotice />);

    expect(
      screen.getByText(
        "Weir is restarting to finish the update. This page will reload by itself.",
      ),
    ).toBeInTheDocument();
  });

  it("reloads the page once /ready answers after a restart is signalled", async () => {
    vi.useFakeTimers();
    mocks.useApplyUpdateMutation.mockReturnValue({
      isPending: false,
      isError: false,
      isSuccess: true,
      mutate: vi.fn(),
    });
    const fetchMock = vi
      .fn()
      .mockResolvedValue({ json: () => Promise.resolve({ ready: true }) });
    vi.stubGlobal("fetch", fetchMock);
    const reloadMock = vi.fn();
    vi.stubGlobal("location", { ...window.location, reload: reloadMock });

    render(<UpdateReadyNotice />);

    await vi.advanceTimersByTimeAsync(3000);

    expect(fetchMock).toHaveBeenCalledWith("/ready", { cache: "no-store" });
    expect(reloadMock).toHaveBeenCalledTimes(1);
  });

  it("keeps polling while the server is still restarting, then reloads once it answers", async () => {
    vi.useFakeTimers();
    mocks.useApplyUpdateMutation.mockReturnValue({
      isPending: false,
      isError: false,
      isSuccess: true,
      mutate: vi.fn(),
    });
    let attempt = 0;
    const fetchMock = vi.fn().mockImplementation(() => {
      attempt += 1;
      if (attempt < 3) {
        return Promise.reject(new Error("connection refused"));
      }
      return Promise.resolve({ json: () => Promise.resolve({ ready: true }) });
    });
    vi.stubGlobal("fetch", fetchMock);
    const reloadMock = vi.fn();
    vi.stubGlobal("location", { ...window.location, reload: reloadMock });

    render(<UpdateReadyNotice />);

    await vi.advanceTimersByTimeAsync(2000 + 1000 + 1000 + 1000);

    expect(fetchMock.mock.calls.length).toBeGreaterThanOrEqual(3);
    expect(reloadMock).toHaveBeenCalledTimes(1);
  });

  it("does not poll before a restart has been signalled", async () => {
    vi.useFakeTimers();
    mocks.useApplyUpdateMutation.mockReturnValue({
      isPending: false,
      isError: false,
      isSuccess: false,
      mutate: vi.fn(),
    });
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<UpdateReadyNotice />);
    await vi.advanceTimersByTimeAsync(5000);

    expect(fetchMock).not.toHaveBeenCalled();
  });
});
