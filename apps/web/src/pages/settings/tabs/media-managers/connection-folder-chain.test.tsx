import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as chainApi from "../../../../lib/processing/library-folder-chain-api";
import * as librariesApi from "../../../../lib/processing/libraries-api";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import { ConnectionFolderChain } from "./connection-folder-chain";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
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
    max_file_size_mb: 0,
    rejected_file_action: "leave",
    min_file_age_seconds: 60,
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

it("lists each linked library by name with its readiness, expandable to its lines", async () => {
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
      ready: false,
    },
  ]);
  vi.spyOn(librariesApi, "fetchProcessingLibraries").mockResolvedValue([
    library(),
  ]);

  render(<ConnectionFolderChain connectionId={3} />, { wrapper });

  const summary = await screen.findByText("TV");
  const details = summary.closest("details")!;
  expect(within(details).getByText("Needs attention")).toBeInTheDocument();

  fireEvent.click(summary);
  expect(
    within(details).getByText(
      "Sonarr has no enabled download client, so it has no downloads to import.",
    ),
  ).toBeInTheDocument();
});
