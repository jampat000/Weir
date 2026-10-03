import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { UnifiedLog } from "./system/tabs/logs/unified-log";

vi.mock("../lib/system/system-log-api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../lib/system/system-log-api")>()),
  fetchSystemLog: vi.fn(async () => ({
    items: [
      {
        id: "job:1",
        source: "job",
        at: "2026-04-20T00:00:00Z",
        level: "success",
        category: "processing",
        workflow: null,
        title: "Finished",
        detail: "Process a media file",
        event: null,
        job: null,
        server: null,
      },
    ],
    next_cursor: null,
    total: 1,
    counts: {
      source: { event: 0, job: 1, server: 0 },
      level: { error: 0, warning: 0, info: 0, success: 1 },
      category: {
        processing: 1,
        scans: 0,
        cleanup: 0,
        library: 0,
        connections: 0,
        backups: 0,
        sign_in: 0,
        updates: 0,
        weir: 0,
      },
    },
  })),
}));

vi.mock("../lib/auth/queries", async (importOriginal) => {
  const original = (await importOriginal()) as Record<string, unknown>;
  return {
    ...original,
    useMeQuery: vi.fn(() => ({
      isPending: false,
      data: { id: 1, username: "admin", role: "admin" },
    })),
  };
});

vi.mock("../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: vi.fn(() => ({ data: [] })),
}));

vi.mock("../lib/settings/queries", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../lib/settings/queries")>()),
  useAppSettingsQuery: vi.fn(() => ({ data: undefined })),
}));

function withProviders(ui: ReactNode) {
  const client = new QueryClient();
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

function setViewport(width: number) {
  Object.defineProperty(window, "innerWidth", {
    configurable: true,
    writable: true,
    value: width,
  });
  window.dispatchEvent(new Event("resize"));
}

const VIEWPORTS = [320, 375, 768, 1024, 1440];

describe("responsive smoke", () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it.each(VIEWPORTS)("renders the log at %ipx", async (width) => {
    setViewport(width);
    render(withProviders(<UnifiedLog onOpenSettings={() => undefined} />));
    expect(await screen.findByTestId("log-feed")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Log" })).toBeInTheDocument();
    expect(screen.getByText("Finished")).toBeInTheDocument();
  });
});
