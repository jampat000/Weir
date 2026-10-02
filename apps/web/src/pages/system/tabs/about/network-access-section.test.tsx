import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { NetworkAccessHalf } from "./network-access-section";

const mocks = vi.hoisted(() => ({
  useNetworkAccessQuery: vi.fn(),
}));

vi.mock("../../../../lib/settings/queries", () => ({
  useNetworkAccessQuery: () => mocks.useNetworkAccessQuery(),
}));

function renderHalf() {
  return render(<NetworkAccessHalf labelId="network-label" />);
}

describe("NetworkAccessHalf", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("says other devices can reach Weir, and where to limit it, when they are allowed in", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "allowed",
        summary:
          "Other devices on your network can reach Weir. To limit Weir to this PC, use the Weir tray icon → Only allow this PC.",
      },
    });

    renderHalf();

    expect(screen.getByText("Your network")).toBeInTheDocument();
    expect(screen.getByText(/Only allow this PC/)).toBeInTheDocument();
    expect(screen.getByTestId("about-network-access-status")).toHaveAttribute(
      "title",
      expect.stringContaining("Other devices on your network can reach Weir."),
    );
  });

  it("says only this PC can reach Weir, and where to change that, before LAN access is allowed", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "this_pc_only",
        summary:
          "Only this PC can reach Weir. To let other devices on your network in, use the Weir tray icon → Allow other devices on your network.",
      },
    });

    renderHalf();

    expect(screen.getByText("This PC only")).toBeInTheDocument();
    expect(screen.getByText(/Allow other devices/)).toBeInTheDocument();
  });

  it("points at the tray menu when Windows Firewall is blocking other devices", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "blocked",
        summary:
          "Windows Firewall is blocking other devices. Use the Weir tray icon → Allow other devices on your network to fix it, or Only allow this PC.",
      },
    });

    renderHalf();

    expect(screen.getByText("Blocked")).toBeInTheDocument();
    expect(
      screen.getByText(/Windows Firewall is blocking it/),
    ).toBeInTheDocument();
  });

  it("renders nothing on a build that does not manage its own firewall (Docker, source)", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: { state: "not_applicable", summary: "" },
    });

    const { container } = renderHalf();

    expect(container).toBeEmptyDOMElement();
  });

  it("shows a loading state before the check returns", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: true,
      data: undefined,
    });

    renderHalf();

    expect(screen.getByText("Checking…")).toBeInTheDocument();
  });
});
