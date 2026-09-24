import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { it, expect } from "vitest";

import { NotFoundPage } from "./not-found-page";

it("says the page doesn't exist and offers a way home, without a second main landmark", () => {
  render(
    <MemoryRouter>
      <NotFoundPage />
    </MemoryRouter>,
  );

  expect(
    screen.getByRole("heading", { name: "This page doesn't exist." }),
  ).toBeInTheDocument();
  expect(
    screen.getByRole("link", { name: "Go to Processing" }),
  ).toHaveAttribute("href", "/");
  expect(screen.queryByRole("main")).not.toBeInTheDocument();
});
