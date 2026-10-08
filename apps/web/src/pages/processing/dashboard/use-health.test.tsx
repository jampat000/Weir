import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { fetchLibraryFolderChain } from "../../../lib/processing/library-folder-chain-api";
import { processingKeys } from "../../../lib/processing/query-keys";
import { useHealth } from "./use-health";

vi.mock(
  "../../../lib/processing/library-folder-chain-api",
  async (original) => ({
    ...(await original<
      typeof import("../../../lib/processing/library-folder-chain-api")
    >()),
    fetchLibraryFolderChain: vi.fn(),
  }),
);
vi.mock("../../../lib/media-managers/queries", () => ({
  useMediaManagerConnectionsQuery: () => ({ data: [] }),
}));
vi.mock("../../../lib/download-clients/queries", () => ({
  useDownloadClientConnectionsQuery: () => ({ data: [] }),
}));
let looks: {
  foldersCheckedAt: number | null;
  readinessCheckedAt: number | null;
} = {
  foldersCheckedAt: null,
  readinessCheckedAt: null,
};
vi.mock("../../../lib/system/use-server-looks", () => ({
  useServerLooks: () => looks,
}));
vi.mock("../../../lib/system/media-tools", () => ({
  useMediaToolsQuery: () => ({ data: undefined }),
}));

const movies = {
  id: 2,
  name: "Movies",
  enabled: true,
  media_type: "movie",
  watched_folder: "D:\\Watched",
  work_folder: "",
  output_folder: "D:\\Out",
} as ProcessingLibrary;

const answer = {
  library_id: 2,
  ready: true,
  local: { ready: true, lines: [] },
  managers: [],
  download_clients: [],
};

function renderHealth() {
  const client = new QueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, ...renderHook(() => useHealth([movies]), { wrapper }) };
}

describe("useHealth", () => {
  it("says a workflow was checked when the server last looked, not when this page last read", async () => {
    vi.mocked(fetchLibraryFolderChain).mockResolvedValue(answer);
    looks = { foldersCheckedAt: Date.now() + 60_000, readinessCheckedAt: null };
    const { result } = renderHealth();
    await waitFor(() =>
      expect(result.current.workflows[0].chain).toBeDefined(),
    );

    expect(result.current.workflows[0].checkedAt).toBe(looks.foldersCheckedAt);
    expect(result.current.foldersReadAt).toBeLessThan(
      looks.foldersCheckedAt ?? 0,
    );
    looks = { foldersCheckedAt: null, readinessCheckedAt: null };
  });

  it("has no time for a workflow whose folders have not answered, whatever the server last looked", () => {
    vi.mocked(fetchLibraryFolderChain).mockReturnValue(new Promise(() => {}));
    looks = { foldersCheckedAt: Date.now(), readinessCheckedAt: null };
    const { result } = renderHealth();

    expect(result.current.workflows[0].checkedAt).toBeNull();
    looks = { foldersCheckedAt: null, readinessCheckedAt: null };
  });

  it("does not check a workflow's folders on a timer: the server says on `folder_checks` when an answer changes", async () => {
    vi.mocked(fetchLibraryFolderChain).mockResolvedValue({
      library_id: 2,
      ready: true,
      local: { ready: true, lines: [] },
      managers: [],
      download_clients: [],
    });
    const client = new QueryClient();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useHealth([movies]), { wrapper });
    await waitFor(() =>
      expect(result.current.workflows[0].chain).toBeDefined(),
    );

    const chains = client
      .getQueryCache()
      .findAll({ queryKey: processingKeys.libraryFolderChains });

    expect(chains).toHaveLength(1);
    expect(chains[0].observers[0].options.refetchInterval).toBeFalsy();
  });
});
