import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, expect, it, vi } from "vitest";

import * as authQueries from "../lib/auth/queries";
import {
  APP_THEME_STORAGE_KEY,
  persistAppTheme,
  readStoredAppTheme,
} from "../lib/ui/app-theme";
import { RequireAuth } from "./require-auth";
import type { UserPublic } from "../lib/api/types";

function renderBehindAuth(user: UserPublic | null) {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    isPending: false,
    data: user,
  } as unknown as ReturnType<typeof authQueries.useMeQuery>);

  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route element={<RequireAuth />}>
            <Route path="/" element={<div>Protected</div>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.removeItem(APP_THEME_STORAGE_KEY);
  document.documentElement.removeAttribute("data-mm-theme");
});

it("overrides a stale local theme with the signed-in account's own once /auth/me answers", () => {
  persistAppTheme("dark");

  renderBehindAuth({
    id: 1,
    username: "alice",
    role: "admin",
    app_theme: "light",
  });

  expect(screen.getByText("Protected")).toBeInTheDocument();
  expect(readStoredAppTheme()).toBe("light");
  expect(document.documentElement.getAttribute("data-mm-theme")).toBe("light");
});

it("follows the system setting rather than a stale local choice when the account has none", () => {
  persistAppTheme("dark");

  renderBehindAuth({
    id: 1,
    username: "alice",
    role: "admin",
    app_theme: null,
  });

  expect(readStoredAppTheme()).toBeNull();
});
