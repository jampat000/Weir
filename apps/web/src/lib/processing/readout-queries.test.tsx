import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { useProcessingLibrariesQuery } from "./libraries-queries";
import { useProcessingMaintenanceQuery } from "./maintenance-queries";
import { useProcessingFilesAtOnceQuery } from "./queries";
import { processingKeys } from "./query-keys";

vi.mock("./files-at-once-api", () => ({
  fetchProcessingFilesAtOnce: vi.fn().mockResolvedValue({}),
}));
vi.mock("./maintenance-api", () => ({
  fetchProcessingMaintenance: vi.fn().mockResolvedValue({ families: [] }),
}));
vi.mock("./libraries-api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./libraries-api")>()),
  fetchProcessingLibraries: vi.fn().mockResolvedValue([]),
}));

function withQueryClient(qc: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  };
}

describe("the Dashboard's read-outs", () => {
  it.each([
    [
      "what files are waiting for",
      processingKeys.filesAtOnce,
      useProcessingFilesAtOnceQuery,
    ],
    [
      "the maintenance sweeps",
      processingKeys.maintenance,
      useProcessingMaintenanceQuery,
    ],
    [
      "the workflows and their next look",
      processingKeys.libraries,
      () => useProcessingLibrariesQuery(),
    ],
  ])(
    "read %s when the server says it changed, not on a timer",
    (_name, queryKey, useReadout) => {
      const qc = new QueryClient();

      renderHook(() => useReadout(), { wrapper: withQueryClient(qc) });

      const query = qc.getQueryCache().find({ queryKey });
      expect(query?.observers).toHaveLength(1);
      expect(query?.observers[0].options.refetchInterval).toBeUndefined();
    },
  );
});
