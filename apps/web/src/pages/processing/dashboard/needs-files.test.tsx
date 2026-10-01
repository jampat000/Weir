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

describe("the held, rejected and skipped files the Needs you panel reads", () => {
  it("reads those three kinds of file of the workflow it is narrowed to", async () => {
    const { result } = renderHook(() => useNeedsFiles(3, 2), { wrapper });

    await waitFor(() => expect(result.current).toEqual([{ id: 5 }]));
    expect(fetchProcessingFiles).toHaveBeenCalledWith({
      file_status: ["rejected", "on_hold", "skipped"],
      library_id: 3,
      limit: NEEDS_FILES_READ,
    });
  });

  it("reads again when the page's count changes", async () => {
    const { rerender } = renderHook(({ count }) => useNeedsFiles(null, count), {
      wrapper,
      initialProps: { count: 1 },
    });
    await waitFor(() => expect(fetchProcessingFiles).toHaveBeenCalledTimes(1));

    rerender({ count: 2 });

    await waitFor(() => expect(fetchProcessingFiles).toHaveBeenCalledTimes(2));
  });
});
