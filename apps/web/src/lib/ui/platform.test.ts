import { afterEach, expect, it, vi } from "vitest";

import { examplePath, isWindowsBrowser } from "./platform";

afterEach(() => {
  vi.unstubAllGlobals();
});

it("reads Windows from the user agent when the browser has no client hints", () => {
  vi.stubGlobal("navigator", {
    userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
  });

  expect(isWindowsBrowser()).toBe(true);
  expect(examplePath("X:\\Media", "/media")).toBe("X:\\Media");
});

it("prefers the client-hint platform when there is one", () => {
  vi.stubGlobal("navigator", {
    userAgent: "Mozilla/5.0 (Windows NT 10.0)",
    userAgentData: { platform: "Linux" },
  });

  expect(isWindowsBrowser()).toBe(false);
});
