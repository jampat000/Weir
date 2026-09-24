import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as chainApi from "../../../../lib/processing/library-folder-chain-api";
import type { LibraryFolderChain as LibraryFolderChainData } from "../../../../lib/processing/library-folder-chain-api";
import { LibraryFolderChain } from "./library-folder-chain";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

function chain(over: Partial<LibraryFolderChainData> = {}): LibraryFolderChainData {
  return {
    library_id: 12,
    local: {
      ready: true,
      lines: [
        { state: "ok", text: "Weir can read the watched folder /media/in." },
      ],
    },
    managers: [],
    ready: true,
    ...over,
  };
}

afterEach(() => {
  vi.restoreAllMocks();
});

it("asks the library to save first when it has no id yet", () => {
  render(
    <LibraryFolderChain
      libraryId={undefined}
      watchedFolder="/media/in"
      workFolder=""
      outputFolder="/media/out"
      mediaType="movie"
    />,
    { wrapper },
  );

  expect(
    screen.getByText("Save this library first to check its folder chain."),
  ).toBeInTheDocument();
});

it("shows Weir's own folder lines are ready, with no manager section and no absence warning, when none is connected", async () => {
  vi.spyOn(chainApi, "fetchLibraryFolderChain").mockResolvedValue(chain());

  render(
    <LibraryFolderChain
      libraryId={12}
      watchedFolder="/media/in"
      workFolder=""
      outputFolder="/media/out"
      mediaType="movie"
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", {
    name: "Weir's own folders",
  });
  expect(
    within(block).getByText("Weir can read the watched folder /media/in."),
  ).toBeInTheDocument();
  expect(within(block).getByText("Ready")).toBeInTheDocument();
  expect(screen.queryByText(/no.*connection covers/i)).not.toBeInTheDocument();
});

it("surfaces a connected manager's own lines and readiness", async () => {
  vi.spyOn(chainApi, "fetchLibraryFolderChain").mockResolvedValue(
    chain({
      ready: false,
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
    }),
  );

  render(
    <LibraryFolderChain
      libraryId={12}
      watchedFolder="/media/in"
      workFolder=""
      outputFolder="/media/out"
      mediaType="tv"
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", { name: "Sonarr" });
  expect(within(block).getByText("Needs attention")).toBeInTheDocument();
  expect(
    within(block).getByText(
      "Sonarr has no enabled download client, so it has no downloads to import.",
    ),
  ).toBeInTheDocument();
});

it("says when it could not check just now", async () => {
  vi.spyOn(chainApi, "fetchLibraryFolderChain").mockRejectedValue(
    new Error("Could not reach the server."),
  );

  render(
    <LibraryFolderChain
      libraryId={12}
      watchedFolder="/media/in"
      workFolder=""
      outputFolder="/media/out"
      mediaType="movie"
    />,
    { wrapper },
  );

  expect(
    await screen.findByText(
      /Weir could not check this library's folder chain just now/,
    ),
  ).toBeInTheDocument();
});
