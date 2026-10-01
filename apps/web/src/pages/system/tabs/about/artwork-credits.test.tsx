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
