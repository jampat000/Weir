import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

import { SidebarUser, initialsOf } from "./sidebar-user";

describe("initialsOf", () => {
  it.each([
    ["admin", "A"],
    ["ann.lee", "AL"],
    ["ann lee smith", "AL"],
    ["j_smith", "JP"],
    ["émile", "É"],
    ["", "W"],
    ["---", "W"],
    [undefined, "W"],
  ])("reads %j as %s", (username, initials) => {
    expect(initialsOf(username)).toBe(initials);
  });
});

describe("SidebarUser", () => {
  it("says what the person is on this Weir, and falls back to plain Weir with no machine name", () => {
    render(
      <MemoryRouter>
        <SidebarUser
          username="admin"
          accountRole="admin"
          machineName=" "
          version="3.2.16"
          signingOut={false}
          onSignOut={vi.fn()}
          onNavigate={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(screen.getByTestId("user-menu")).toHaveTextContent("@admin · Weir");
    expect(screen.getByTestId("user-menu")).not.toHaveTextContent("Weir on");
  });
});
