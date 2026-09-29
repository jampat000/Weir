import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { NetworkAccessSection } from "./network-access-section";

const mocks = vi.hoisted(() => ({
  useNetworkAccessQuery: vi.fn(),
}));

vi.mock("../../../../lib/settings/queries", () => ({
  useNetworkAccessQuery: () => mocks.useNetworkAccessQuery(),
}));

describe("NetworkAccessSection", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("says other devices can reach Weir, and how to limit it, when they are allowed in", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "allowed",
        summary:
          "Other devices on your network can reach Weir. To limit Weir to this PC, use the Weir tray icon → Only allow this PC.",
      },
    });

    render(<NetworkAccessSection />);

    expect(screen.getByText("Reachable")).toBeInTheDocument();
    expect(
      screen.getByText(/Other devices on your network can reach Weir./),
    ).toBeInTheDocument();
    expect(screen.getByText(/Only allow this PC/)).toBeInTheDocument();
  });

  it("says only this PC can reach Weir, and how to change that, before LAN access is allowed", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "this_pc_only",
        summary:
          "Only this PC can reach Weir. To let other devices on your network in, use the Weir tray icon → Allow other devices on your network.",
      },
    });

    render(<NetworkAccessSection />);

    expect(screen.getByText("This PC only")).toBeInTheDocument();
    expect(
      screen.getByText(/Only this PC can reach Weir./),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Allow other devices on your network/),
    ).toBeInTheDocument();
  });

  it("tells the operator to use the tray menu when Windows Firewall is blocking other devices", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "blocked",
        summary:
          "Windows Firewall is blocking other devices. Use the Weir tray icon → Allow other devices on your network to fix it, or Only allow this PC.",
      },
    });

    render(<NetworkAccessSection />);

    expect(screen.getByText("Blocked")).toBeInTheDocument();
    expect(
      screen.getByText(/Windows Firewall is blocking other devices./),
    ).toBeInTheDocument();
    expect(screen.getByText(/Use the Weir tray icon/)).toBeInTheDocument();
  });

  it("renders nothing on a build that does not manage its own firewall (Docker, source)", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: { state: "not_applicable", summary: "" },
    });

    const { container } = render(<NetworkAccessSection />);

    expect(container).toBeEmptyDOMElement();
  });

  it("shows a loading state before the check returns", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: true,
      data: undefined,
    });

    render(<NetworkAccessSection />);

    expect(screen.getByText("Checking…")).toBeInTheDocument();
  });
});
