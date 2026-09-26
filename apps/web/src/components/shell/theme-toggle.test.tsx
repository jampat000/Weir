import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as authQueries from "../../lib/auth/queries";
import { APP_THEME_STORAGE_KEY, persistAppTheme } from "../../lib/ui/app-theme";
import { ThemeToggle } from "./theme-toggle";

const mutate = vi.fn<(theme: "light" | "dark") => void>();

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

function setup(isError = false) {
  vi.spyOn(authQueries, "useSetThemeMutation").mockReturnValue({
    mutate,
    isError,
  } as unknown as ReturnType<typeof authQueries.useSetThemeMutation>);
}

afterEach(() => {
  vi.restoreAllMocks();
  mutate.mockReset();
  localStorage.removeItem(APP_THEME_STORAGE_KEY);
  document.documentElement.removeAttribute("data-mm-theme");
});

it("applies the new theme instantly and saves the choice to the account", () => {
  setup();
  persistAppTheme("dark");

  render(<ThemeToggle />, { wrapper });
  fireEvent.click(screen.getByTestId("theme-toggle"));

  expect(document.documentElement.getAttribute("data-mm-theme")).toBe("light");
  expect(localStorage.getItem(APP_THEME_STORAGE_KEY)).toBe("light");
  expect(mutate).toHaveBeenCalledWith("light");
});

it("says a failed save never touched the theme on other browsers, but keeps it here", () => {
  setup(true);
  persistAppTheme("dark");

  render(<ThemeToggle />, { wrapper });

  expect(screen.getByTestId("theme-toggle-alert")).toHaveTextContent(
    "couldn't save your theme for other browsers",
  );
  // The local flip and this attempt's failure are independent: the switch still shows dark's icon.
  expect(screen.getByTestId("theme-toggle")).toHaveAttribute(
    "aria-label",
    "Switch to light mode",
  );
});

it("shows no alert once a save has not failed", () => {
  setup(false);

  render(<ThemeToggle />, { wrapper });

  expect(screen.queryByTestId("theme-toggle-alert")).not.toBeInTheDocument();
});
