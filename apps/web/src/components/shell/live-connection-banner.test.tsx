import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import {
  reportLiveConnection,
  resetLiveConnection,
} from "../../lib/live/live-connection";
import { LiveConnectionBanner } from "./live-connection-banner";

afterEach(() => resetLiveConnection());

const banner = () => screen.queryByTestId("live-connection-banner");

describe("LiveConnectionBanner", () => {
  it("shows nothing while the page is still connecting or the connection is live", () => {
    render(<LiveConnectionBanner />);
    expect(banner()).toBeNull();

    act(() => void reportLiveConnection("opened"));
    expect(banner()).toBeNull();
  });

  it("says live updates are paused while the connection is lost, and clears when it is back", () => {
    render(<LiveConnectionBanner />);
    act(() => void reportLiveConnection("opened"));

    act(() => void reportLiveConnection("dropped"));
    expect(banner()).toHaveTextContent(
      "Live updates paused: Weir isn't answering. Reconnecting…",
    );
    expect(banner()).toHaveAttribute("data-status", "broken");

    act(() => void reportLiveConnection("opened"));
    expect(banner()).toBeNull();
  });

  it("keeps a live region on the page so the change is announced", () => {
    render(<LiveConnectionBanner />);

    expect(screen.getByRole("status")).toBeInTheDocument();
  });
});
