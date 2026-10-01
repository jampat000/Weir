import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { NEEDS_FILES_READ } from "./needs-model";
import { useNeedsFiles } from "./needs-files";

const fetchProcessingFiles = vi.fn();

vi.mock("../../../lib/activity/use-activity-stream-invalidation", () => ({
  useActivityStreamInvalidations: vi.fn(),
}));
vi.mock("../../../lib/processing/files-api", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../lib/processing/files-api")
  >()),
  fetchProcessingFiles: (query: unknown) => fetchProcessingFiles(query),
}));

let client: QueryClient;

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  fetchProcessingFiles.mockReset().mockResolvedValue({
    files: [{ id: 5 }],
    status_counts: {},
    returned: 1,
    limit: NEEDS_FILES_READ,
  });
});

describe("the failed, rejected, held and skipped files the Needs you panel reads", () => {
  it("reads those four kinds of file of the workflow it is narrowed to", async () => {
    const { result } = renderHook(() => useNeedsFiles(3), { wrapper });

    await waitFor(() => expect(result.current).toEqual([{ id: 5 }]));
    expect(fetchProcessingFiles).toHaveBeenCalledWith({
      file_status: ["processing_failed", "rejected", "on_hold", "skipped"],
      library_id: 3,
      limit: NEEDS_FILES_READ,
    });
  });

  it("reads every workflow's files when it is not narrowed", async () => {
    const { result } = renderHook(() => useNeedsFiles(null), { wrapper });

    await waitFor(() => expect(result.current).toEqual([{ id: 5 }]));
    expect(fetchProcessingFiles).toHaveBeenCalledWith(
      expect.objectContaining({ library_id: undefined }),
    );
  });

  it("shares one read between every part of the page that asks for the same workflows", async () => {
    const { result } = renderHook(
      () => {
        useNeedsFiles(null);
        return useNeedsFiles(null);
      },
      { wrapper },
    );

    await waitFor(() => expect(result.current).toEqual([{ id: 5 }]));
    expect(fetchProcessingFiles).toHaveBeenCalledTimes(1);
  });
});
