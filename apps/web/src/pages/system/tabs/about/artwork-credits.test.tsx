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

  expect(
    screen.getByText(
      "TV information and images are provided by TheTVDB.com, but we are not endorsed or certified by TheTVDB.com or its affiliates.",
    ),
  ).toBeVisible();
  const link = screen.getByTestId("about-tvdb-link");
  expect(link).toHaveAttribute("href", "https://thetvdb.com");
  expect(link).toHaveAttribute("rel", "noreferrer");
});
