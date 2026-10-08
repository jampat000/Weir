import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { systemKeys } from "./query-keys";
import { useSystemReadinessQuery } from "./readiness-queries";

vi.mock("./readiness-api", () => ({
  fetchSystemReadiness: vi.fn(async () => ({ ready: true, version: "1.0.0" })),
}));

describe("useSystemReadinessQuery", () => {
  it("is not read again on a timer: the server says on `readiness` when it changes", async () => {
    const client = new QueryClient();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useSystemReadinessQuery(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const query = client
      .getQueryCache()
      .find({ queryKey: systemKeys.readiness });

    expect(query?.observers[0].options.refetchInterval).toBeFalsy();
  });
});
