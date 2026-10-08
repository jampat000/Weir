import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { fetchLibraryOverview } from "./library-mode-api";
import { useLibraryOverviewQuery } from "./library-mode-queries";
import { processingKeys } from "./query-keys";

vi.mock("./library-mode-api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./library-mode-api")>()),
  fetchLibraryOverview: vi.fn(),
}));

describe("useLibraryOverviewQuery", () => {
  it("is not read again on a timer while a scan runs: the server says on `library_scan` when it changes", async () => {
    vi.mocked(fetchLibraryOverview).mockResolvedValue({
      scan: { running: true },
    } as Awaited<ReturnType<typeof fetchLibraryOverview>>);
    const client = new QueryClient();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useLibraryOverviewQuery(4), {
      wrapper,
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const query = client
      .getQueryCache()
      .find({ queryKey: processingKeys.libraryOverview(4) });

    expect(query?.observers[0].options.refetchInterval).toBeFalsy();
  });
});
