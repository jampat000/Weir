import { renderHook } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import {
  APP_THEME_STORAGE_KEY,
  persistAppTheme,
  readStoredAppTheme,
} from "./app-theme";
import { useAccountThemeSync } from "./use-account-theme-sync";

afterEach(() => {
  localStorage.removeItem(APP_THEME_STORAGE_KEY);
  document.documentElement.removeAttribute("data-mm-theme");
});

it("overrides a stale local choice with the signed-in account's theme", () => {
  persistAppTheme("dark");

  renderHook(({ theme }) => useAccountThemeSync(theme), {
    initialProps: { theme: "light" as const },
  });

  expect(readStoredAppTheme()).toBe("light");
  expect(document.documentElement.getAttribute("data-mm-theme")).toBe("light");
});

it("leaves the first-paint cache alone while the account is still unknown", () => {
  persistAppTheme("dark");

  renderHook(({ theme }) => useAccountThemeSync(theme), {
    initialProps: { theme: undefined },
  });

  expect(readStoredAppTheme()).toBe("dark");
});

it("applies the new theme again when the account's preference changes", () => {
  const { rerender } = renderHook(({ theme }) => useAccountThemeSync(theme), {
    initialProps: { theme: "dark" as "dark" | "light" | null | undefined },
  });
  expect(readStoredAppTheme()).toBe("dark");

  rerender({ theme: "light" });

  expect(readStoredAppTheme()).toBe("light");
});
