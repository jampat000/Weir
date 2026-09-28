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

  it("says other devices can reach Weir when the rule is allowed", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "allowed",
        summary: "Other devices on your network can reach Weir.",
      },
    });

    render(<NetworkAccessSection />);

    expect(
      screen.getByText("Other devices on your network can reach Weir."),
    ).toBeInTheDocument();
    expect(screen.getByText("Reachable")).toBeInTheDocument();
  });

  it("tells the operator to use the tray menu when Windows Firewall is blocking it", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "blocked",
        summary:
          "Windows Firewall is blocking other devices. Use the Weir tray icon → Allow other devices… to fix it.",
      },
    });

    render(<NetworkAccessSection />);

    expect(screen.getByText("Blocked")).toBeInTheDocument();
    expect(screen.getByText(/Use the Weir tray icon/)).toBeInTheDocument();
  });

  it("says not set up when nobody has answered the prompt yet", () => {
    mocks.useNetworkAccessQuery.mockReturnValue({
      isLoading: false,
      data: {
        state: "not_configured",
        summary:
          "Not set up. Use the Weir tray icon to let other devices on your network reach Weir.",
      },
    });

    render(<NetworkAccessSection />);

    expect(screen.getByText("Not set up")).toBeInTheDocument();
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
