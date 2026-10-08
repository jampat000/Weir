import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { UpdateReadyNotice } from "./update-ready-notice";

type Signal = { type: "changed"; topic: "update" } | { type: "restarted" };

const mocks = vi.hoisted(() => ({
  useUpdateStateQuery: vi.fn(),
  useApplyUpdateMutation: vi.fn(),
  subscribeLiveSignals: vi.fn(),
}));

vi.mock("../../../../lib/settings/queries", () => ({
  useUpdateStateQuery: (...args: unknown[]) =>
    mocks.useUpdateStateQuery(...args),
  useApplyUpdateMutation: () => mocks.useApplyUpdateMutation(),
}));

vi.mock("../../../../lib/activity/use-activity-stream-invalidation", () => ({
  subscribeLiveSignals: (subscriber: (signal: Signal) => void) =>
    mocks.subscribeLiveSignals(subscriber),
}));

describe("UpdateReadyNotice", () => {
  const reset = vi.fn();
  let hear: (signal: Signal) => void;
  const unsubscribe = vi.fn();

  function restartSignalled() {
    mocks.useApplyUpdateMutation.mockReturnValue({
      isPending: false,
      isError: false,
      isSuccess: true,
      mutate: vi.fn(),
      reset,
    });
  }

  beforeEach(() => {
    mocks.useUpdateStateQuery.mockReturnValue({
      data: { downloaded: true, pending_version: "2.0.8" },
    });
    mocks.subscribeLiveSignals.mockImplementation(
      (subscriber: (signal: Signal) => void) => {
        hear = subscriber;
        return unsubscribe;
      },
    );
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.useRealTimers();
    mocks.subscribeLiveSignals.mockReset();
    unsubscribe.mockReset();
    reset.mockReset();
  });

  it("says the page will reload itself once the restart is signalled", () => {
    restartSignalled();

    render(<UpdateReadyNotice />);

    expect(
      screen.getByText(
        "Weir is restarting to finish the update. This page will reload by itself.",
      ),
    ).toBeInTheDocument();
  });

  it("asks the server nothing while it restarts: the stream tells the page when it is back", async () => {
    vi.useFakeTimers();
    restartSignalled();
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<UpdateReadyNotice />);
    await vi.advanceTimersByTimeAsync(30_000);

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("goes back to offering the restart when the server came back and the update is still waiting", () => {
    restartSignalled();
    render(<UpdateReadyNotice />);

    act(() => hear({ type: "restarted" }));

    expect(reset).toHaveBeenCalledTimes(1);
  });

  it("is not reset by a change that is not a restart", () => {
    restartSignalled();
    render(<UpdateReadyNotice />);

    act(() => hear({ type: "changed", topic: "update" }));

    expect(reset).not.toHaveBeenCalled();
  });

  it("stops listening when it goes away", () => {
    restartSignalled();
    const { unmount } = render(<UpdateReadyNotice />);

    unmount();

    expect(unsubscribe).toHaveBeenCalledTimes(1);
  });

  it("offers nothing until an update has been downloaded", () => {
    mocks.useUpdateStateQuery.mockReturnValue({ data: { downloaded: false } });
    mocks.useApplyUpdateMutation.mockReturnValue({
      isPending: false,
      isError: false,
      isSuccess: false,
      mutate: vi.fn(),
      reset,
    });

    render(<UpdateReadyNotice />);

    expect(screen.queryByText("Update ready to install")).toBeNull();
  });
});
