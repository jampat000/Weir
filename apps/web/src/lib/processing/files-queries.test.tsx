import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { activityKeys } from "../activity/query-keys";
import { useForgetProcessingFile } from "./files-queries";
import { processingKeys } from "./query-keys";

vi.mock("./files-api", () => ({
  forgetProcessingFile: vi.fn().mockResolvedValue(undefined),
}));

function withQueryClient(qc: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  };
}

describe("useForgetProcessingFile", () => {
  it("invalidates every Processing query that can still report the file as finished or failed", async () => {
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");
    const { result } = renderHook(() => useForgetProcessingFile(), {
      wrapper: withQueryClient(qc),
    });

    result.current.mutate(1);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // The Files list itself (what the removal reads from)...
    expect(spy).toHaveBeenCalledWith({ queryKey: processingKeys.files });
    // ...and the three reports that read the server's own state instead, which forgetting a file
    // does not otherwise touch (#780): the "Just finished" list, the failed-jobs alert, and the
    // overview counters.
    expect(spy).toHaveBeenCalledWith({ queryKey: activityKeys.recent });
    expect(spy).toHaveBeenCalledWith({
      queryKey: processingKeys.jobsInspection,
    });
    expect(spy).toHaveBeenCalledWith({
      queryKey: processingKeys.overviewStats(),
    });
  });
});
