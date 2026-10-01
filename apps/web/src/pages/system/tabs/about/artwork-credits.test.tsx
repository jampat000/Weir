import { render, screen } from "@testing-library/react";
import { expect, it } from "vitest";

import { ArtworkCredits } from "./artwork-credits";

it("credits TMDb in the words it asks for, with a link to its site", () => {
  render(<ArtworkCredits />);

  expect(
    screen.getByText(
      "This product uses the TMDB API but is not endorsed or certified by TMDB.",
    ),
  ).toBeVisible();
  expect(screen.getByTestId("about-tmdb-link")).toHaveAttribute(
    "href",
    "https://www.themoviedb.org",
  );
});

it("credits TheTVDB beside it, with a link, whatever the library holds", () => {
  render(<ArtworkCredits />);

  expect(screen.getByTestId("about-tvdb-credit")).toHaveTextContent(
    "TV metadata provided by TheTVDB",
  );
  const link = screen.getByTestId("about-tvdb-link");
  expect(link).toHaveTextContent("TheTVDB");
  expect(link).toHaveAttribute("href", "https://thetvdb.com");
  expect(link).toHaveAttribute("rel", "noreferrer noopener");
});

it("shows TMDb's logo as a small image that links to its site", () => {
  render(<ArtworkCredits />);

  const logo = screen.getByRole("img", { name: "TMDB" });
  expect(logo).toBeVisible();
  expect(logo).toHaveClass("h-3.5", "w-auto");
  expect(logo.closest("a")).toHaveAttribute(
    "href",
    "https://www.themoviedb.org",
  );
});
