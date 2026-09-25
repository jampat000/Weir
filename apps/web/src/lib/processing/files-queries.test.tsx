import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { WORKING_FILE_STATUS } from "../../pages/processing/processing-model";
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
    // ...and the two reports that read the same server rows filtered to files Weir still knows about: the
    // "Just finished" list and the failed-jobs alert. The overview's lifetime totals read those rows
    // unfiltered and do not change, so they are deliberately not invalidated here.
    expect(spy).toHaveBeenCalledWith({ queryKey: activityKeys.recent });
    expect(spy).toHaveBeenCalledWith({
      queryKey: processingKeys.jobsInspection,
    });
    expect(spy).not.toHaveBeenCalledWith({
      queryKey: processingKeys.overviewStats(),
    });
  });

  it("also invalidates the Working lane's own dedicated files query", async () => {
    const qc = new QueryClient();
    // The general page and the Working lane's own status-filtered fetch are both under processingKeys.files,
    // so the plain files-list invalidation above must reach this one too, without a key of its own to name.
    const workingFilesKey = processingKeys.fileList({
      file_status: WORKING_FILE_STATUS,
      limit: 1000,
    });
    qc.setQueryData(workingFilesKey, { files: [], returned: 0, limit: 1000 });
    const query = qc.getQueryCache().find({ queryKey: workingFilesKey });
    expect(query?.isStale()).toBe(false);

    const { result } = renderHook(() => useForgetProcessingFile(), {
      wrapper: withQueryClient(qc),
    });
    result.current.mutate(1);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(query?.isStale()).toBe(true);
  });
});
