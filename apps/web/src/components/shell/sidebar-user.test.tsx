import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

import { SidebarUser, initialsOf } from "./sidebar-user";

describe("initialsOf", () => {
  it.each([
    ["admin", "A"],
    ["ann.lee", "AL"],
    ["ann lee smith", "AL"],
    ["j_smith", "JS"],
    ["émile", "É"],
    ["", "W"],
    ["---", "W"],
    [undefined, "W"],
  ])("reads %j as %s", (username, initials) => {
    expect(initialsOf(username)).toBe(initials);
  });
});

describe("SidebarUser", () => {
  it("shows only the name of whoever is signed in", () => {
    render(
      <MemoryRouter>
        <SidebarUser
          username="admin"
          version="3.2.16"
          signingOut={false}
          onSignOut={vi.fn()}
          onNavigate={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(screen.getByTestId("user-menu")).toHaveTextContent(/^Aadmin$/);
  });
});
