import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, expect, it, vi } from "vitest";

import * as chainApi from "../../../../lib/processing/library-folder-chain-api";
import * as librariesApi from "../../../../lib/processing/libraries-api";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import { ConnectionFolderChain } from "./connection-folder-chain";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return (
    <MemoryRouter>
      <QueryClientProvider client={qc}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

function library(over: Partial<ProcessingLibrary> = {}): ProcessingLibrary {
  return {
    id: 12,
    name: "TV",
    enabled: true,
    media_type: "tv",
    display_order: 0,
    watched_folder: "/media/in",
    work_folder: "",
    output_folder: "/media/out",
    media_extensions_csv: "",
    exclude_markers_csv: "",
    include_patterns_csv: "",
    exclude_patterns_csv: "",
    min_file_size_mb: 0,
    effective_min_file_size_mb: 0,
    max_file_size_mb: 0,
    rejected_file_action: "leave",
    min_file_age_seconds: 60,
    effective_min_file_age_seconds: 60,
    created_after: null,
    created_before: null,
    modified_after: null,
    modified_before: null,
    exclude_hidden: true,
    top_level_only: false,
    ...over,
  } as ProcessingLibrary;
}

afterEach(() => {
  vi.restoreAllMocks();
});

it("shows nothing when no library is linked", async () => {
  const fetchChain = vi
    .spyOn(chainApi, "fetchConnectionFolderChain")
    .mockResolvedValue([]);
  vi.spyOn(librariesApi, "fetchProcessingLibraries").mockResolvedValue([]);

  const { container } = render(<ConnectionFolderChain connectionId={3} />, {
    wrapper,
  });

  await waitFor(() => expect(fetchChain).toHaveBeenCalled());
  expect(
    container.querySelector('[data-testid="media-manager-folder-chain"]'),
  ).toBeNull();
});

it("lists each workflow it feeds with its readiness, and opens its folder chain instead of repeating the lines", async () => {
  vi.spyOn(chainApi, "fetchConnectionFolderChain").mockResolvedValue([
    {
      library_id: 12,
      local: {
        ready: true,
        lines: [
          { state: "ok", text: "Weir can read the watched folder /media/in." },
        ],
      },
      managers: [
        {
          connection_id: 3,
          kind: "sonarr",
          name: "Sonarr",
          label: "Sonarr",
          flow: "remote_path_mapping",
          ready: false,
          mapping: null,
          lines: [
            {
              state: "problem",
              text: "Sonarr has no enabled download client, so it has no downloads to import.",
            },
          ],
        },
      ],
      download_clients: [],
      ready: false,
    },
  ]);
  vi.spyOn(librariesApi, "fetchProcessingLibraries").mockResolvedValue([
    library(),
  ]);

  render(<ConnectionFolderChain connectionId={3} />, { wrapper });

  const row = (await screen.findByText("TV")).closest("p")!;
  expect(within(row).getByText("Needs attention")).toBeInTheDocument();
  expect(
    within(row).getByRole("link", { name: "See its folder chain" }),
  ).toHaveAttribute("href", "/settings?tab=libraries&edit=12");
  // The lines live once, in the workflow editor's Folder chain.
  expect(
    screen.queryByText(/Sonarr has no enabled download client/),
  ).not.toBeInTheDocument();
});

it("shows a workflow as not verified, never ready, when one of its lines is only a declaration", async () => {
  vi.spyOn(chainApi, "fetchConnectionFolderChain").mockResolvedValue([
    {
      library_id: 12,
      local: {
        ready: true,
        lines: [
          { state: "ok", text: "Weir can read the watched folder /media/in." },
        ],
      },
      managers: [
        {
          connection_id: 3,
          kind: "deluno",
          name: "Deluno",
          label: "Deluno",
          flow: "handoff",
          ready: true,
          mapping: null,
          lines: [
            {
              state: "unverified",
              text: "Deluno says its TV library downloads to /media/in (inside Weir's watched folder). Weir can't see where each download client really saves.",
            },
          ],
        },
      ],
      download_clients: [],
      ready: true,
    },
  ]);
  vi.spyOn(librariesApi, "fetchProcessingLibraries").mockResolvedValue([
    library(),
  ]);

  render(<ConnectionFolderChain connectionId={3} />, { wrapper });

  const row = (await screen.findByText("TV")).closest("p")!;
  expect(within(row).getByText("Not verified")).toBeInTheDocument();
  expect(within(row).queryByText("Ready")).not.toBeInTheDocument();
});
