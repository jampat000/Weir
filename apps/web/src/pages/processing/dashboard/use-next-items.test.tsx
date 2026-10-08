import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { fetchLibraryOverview } from "../../../lib/processing/library-mode-api";
import { processingKeys } from "../../../lib/processing/query-keys";
import { useNextItems } from "./use-next-items";

vi.mock("../../../lib/processing/library-mode-api", async (original) => ({
  ...(await original<
    typeof import("../../../lib/processing/library-mode-api")
  >()),
  fetchLibraryOverview: vi.fn(),
}));
vi.mock("../../../lib/processing/maintenance-queries", () => ({
  useProcessingMaintenanceQuery: () => ({ data: undefined }),
}));

const movies = {
  id: 2,
  name: "Movies",
  enabled: true,
  watched_folder: "D:\\Watched",
  next_look_at: null,
} as ProcessingLibrary;

describe("useNextItems", () => {
  it("does not read a library's clean time on a timer: the server says on `library_scan` when it moves", async () => {
    vi.mocked(fetchLibraryOverview).mockResolvedValue({
      schedule: { enabled: true, next_run_at: "2026-10-09T02:00:00Z" },
    } as Awaited<ReturnType<typeof fetchLibraryOverview>>);
    const client = new QueryClient();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    renderHook(() => useNextItems([movies]), { wrapper });
    await waitFor(() => expect(fetchLibraryOverview).toHaveBeenCalledTimes(1));

    const query = client
      .getQueryCache()
      .find({ queryKey: processingKeys.libraryOverview(2) });

    expect(query?.observers[0].options.refetchInterval).toBeFalsy();
  });
});
