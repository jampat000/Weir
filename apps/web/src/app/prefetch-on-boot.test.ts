import { waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import { authKeys } from "../lib/auth/query-keys";
import { settingsKeys } from "../lib/settings/query-keys";
import { queryClient } from "./query-client";

vi.mock("../lib/api/auth-api", () => ({
  fetchMe: vi.fn().mockResolvedValue(null),
}));
vi.mock("../lib/settings/settings-api", () => ({
  fetchAppSettings: vi.fn().mockResolvedValue({}),
}));

afterEach(() => {
  queryClient.clear();
  vi.restoreAllMocks();
});

it("starts who-is-signed-in and the app's settings under the same keys the app queries later", async () => {
  const spy = vi.spyOn(queryClient, "prefetchQuery");
  const { prefetchOnBoot } = await import("./prefetch-on-boot");

  prefetchOnBoot();

  const queriedKeys = spy.mock.calls.map(([options]) => options.queryKey);
  expect(queriedKeys).toContainEqual(authKeys.me);
  expect(queriedKeys).toContainEqual(settingsKeys.app);
});

it("puts the answers where useMeQuery and useAppSettingsQuery will find them", async () => {
  const { prefetchOnBoot } = await import("./prefetch-on-boot");

  prefetchOnBoot();
  await waitFor(() => {
    expect(queryClient.getQueryState(authKeys.me)?.status).toBe("success");
  });

  expect(queryClient.getQueryData(settingsKeys.app)).toEqual({});
});
