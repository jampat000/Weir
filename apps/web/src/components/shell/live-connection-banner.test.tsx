import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  reportLiveConnection,
  resetLiveConnection,
} from "../../lib/live/live-connection";
import { LOST_CONNECTION_GRACE_MS } from "../../lib/live/use-live-updates-paused";
import { LiveConnectionBanner } from "./live-connection-banner";

afterEach(() => {
  resetLiveConnection();
  vi.useRealTimers();
});

const banner = () => screen.queryByTestId("live-connection-banner");

describe("LiveConnectionBanner", () => {
  it("shows nothing while the page is still connecting or the connection is live", () => {
    render(<LiveConnectionBanner />);
    expect(banner()).toBeNull();

    act(() => void reportLiveConnection("opened"));
    expect(banner()).toBeNull();
  });

  it("says live updates are paused once the connection has stayed lost for a few seconds, and clears when it is back", () => {
    vi.useFakeTimers();
    render(<LiveConnectionBanner />);
    act(() => void reportLiveConnection("opened"));

    act(() => void reportLiveConnection("dropped"));
    act(() => void vi.advanceTimersByTime(LOST_CONNECTION_GRACE_MS));
    expect(banner()).toHaveTextContent(
      "Live updates paused: can't reach Weir. Reconnecting…",
    );
    expect(banner()).toHaveAttribute("data-status", "broken");

    act(() => void reportLiveConnection("opened"));
    expect(banner()).toBeNull();
  });

  it("shows nothing for a drop that mends before the grace is over", () => {
    vi.useFakeTimers();
    render(<LiveConnectionBanner />);
    act(() => void reportLiveConnection("opened"));

    act(() => void reportLiveConnection("dropped"));
    act(() => void vi.advanceTimersByTime(LOST_CONNECTION_GRACE_MS - 1));
    act(() => void reportLiveConnection("opened"));
    act(() => void vi.advanceTimersByTime(LOST_CONNECTION_GRACE_MS));

    expect(banner()).toBeNull();
  });

  it("keeps a live region on the page so the change is announced", () => {
    render(<LiveConnectionBanner />);

    expect(screen.getByRole("status")).toBeInTheDocument();
  });
});
