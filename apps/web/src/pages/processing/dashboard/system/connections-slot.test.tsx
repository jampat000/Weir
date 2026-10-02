import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { ConnectionsSlot } from "./connections-slot";

const entries = [{ key: "media_manager:1" }];
const lights = new Map([["media_manager:1", "ok"]]);
const testing = { testing: new Set<string>(), allBusy: false };
const useHealth = vi.fn(() => ({ managers: ["m"], downloadClients: ["d"] }));
const useConnections = vi.fn(() => ({ entries, lights }));

vi.mock("../use-health", () => ({
  useHealth: (...args: unknown[]) =>
    (useHealth as (...a: unknown[]) => unknown)(...args),
}));
vi.mock("../../../../lib/connections/use-connections", () => ({
  useConnections: (...args: unknown[]) =>
    (useConnections as (...a: unknown[]) => unknown)(...args),
}));
vi.mock("../use-connection-testing", () => ({
  useConnectionTesting: () => testing,
}));
vi.mock("../connections-card", () => ({
  ConnectionsCard: (props: {
    entries: unknown;
    lights: unknown;
    testing: unknown;
  }) => (
    <p data-testid="card">
      {String(props.entries === entries)}
      {String(props.lights === lights)}
      {String(props.testing === testing)}
    </p>
  ),
}));

describe("the Connections card's slot", () => {
  it("gives the card the connections in scope, how each answers and the tests", () => {
    render(<ConnectionsSlot workflows={[]} workflowId={null} />);

    expect(screen.getByTestId("card")).toHaveTextContent("truetruetrue");
  });

  it("asks for the connections of the picked workflow", () => {
    render(<ConnectionsSlot workflows={[]} workflowId={7} />);

    expect(useHealth).toHaveBeenLastCalledWith([], 7);
    expect(useConnections).toHaveBeenLastCalledWith(["m"], ["d"]);
  });
});
