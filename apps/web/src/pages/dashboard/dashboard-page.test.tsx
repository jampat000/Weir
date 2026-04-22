import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { activityRecentKey } from "../../lib/activity/queries";
import { dashboardStatusKey } from "../../lib/dashboard/queries";
import { DashboardPage } from "./dashboard-page";

beforeAll(() => {
  class EventSourceStub {
    addEventListener = vi.fn();
    removeEventListener = vi.fn();
    close = vi.fn();
  }
  vi.stubGlobal("EventSource", EventSourceStub);
});

function withProviders(ui: ReactNode, client: QueryClient) {
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

describe("DashboardPage", () => {
  let qc: QueryClient;

  beforeEach(() => {
    qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    qc.setQueryData(dashboardStatusKey, {
      scope_note: "Read-only overview.",
      system: { api_version: "0.0.0", environment: "test", healthy: true },
      activity_summary: { events_last_24h: 0, latest: null },
    });
    qc.setQueryData(activityRecentKey, { items: [] });
  });

  it("renders module cards", () => {
    render(withProviders(<DashboardPage />, qc));
    expect(screen.getByTestId("dashboard-page")).toBeInTheDocument();
    expect(screen.getByTestId("dashboard-module-cards")).toBeInTheDocument();
    expect(screen.getByText("Refiner")).toBeInTheDocument();
    expect(screen.getByText("Pruner")).toBeInTheDocument();
    expect(screen.getByText("Subber")).toBeInTheDocument();
  });
});
