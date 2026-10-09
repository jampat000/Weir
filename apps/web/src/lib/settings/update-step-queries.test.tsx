import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { settingsKeys } from "./query-keys";
import { useCheckUpdateMutation, useDownloadUpdateMutation } from "./queries";
import * as settingsApi from "./settings-api";
import type { UpdateStateOut } from "./types";

const STATE: UpdateStateOut = {
  downloaded: false,
  pending_version: null,
  state: "checking",
  failure: null,
  tray_running: true,
};

function setup() {
  const client = new QueryClient({
    defaultOptions: { mutations: { retry: false } },
  });
  const invalidate = vi.spyOn(client, "invalidateQueries");
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, invalidate, wrapper };
}

describe("the update step mutations", () => {
  it("show the step the server says is under way at once", async () => {
    vi.spyOn(settingsApi, "postDownloadUpdate").mockResolvedValue({
      ...STATE,
      state: "downloading",
    });
    const { client, wrapper } = setup();
    const { result } = renderHook(() => useDownloadUpdateMutation(), {
      wrapper,
    });

    await act(() => result.current.mutateAsync());

    expect(client.getQueryData(settingsKeys.updateState)).toMatchObject({
      state: "downloading",
    });
  });

  it("read the state again when the server refuses, because the page was behind", async () => {
    vi.spyOn(settingsApi, "postCheckUpdate").mockRejectedValue(
      new Error(
        "The Weir tray isn't running, so Weir can't update itself from here.",
      ),
    );
    const { invalidate, wrapper } = setup();
    const { result } = renderHook(() => useCheckUpdateMutation(), { wrapper });

    act(() => result.current.mutate());

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(invalidate).toHaveBeenCalledWith({
      queryKey: settingsKeys.updateState,
    });
  });
});
