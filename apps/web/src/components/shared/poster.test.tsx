import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it } from "vitest";

import { Poster } from "./poster";

const POSTER = "/api/v1/artwork/posters/movie-the-general-1926";

it("shows the poster, lazily, with the title as its description", () => {
  render(<Poster url={POSTER} title="The General (1926)" workflow="Movies" />);

  const image = screen.getByRole("img", { name: "The General (1926)" });
  expect(image).toHaveAttribute("src", POSTER);
  expect(image).toHaveAttribute("loading", "lazy");
  expect(image).toHaveAttribute("decoding", "async");
});

it("shows the title's initials where there is no poster", () => {
  render(
    <Poster url={null} title="The Quiet Harbour (2024)" workflow="Movies" />,
  );

  expect(screen.queryByRole("img")).toBeNull();
  expect(screen.getByText("QH")).toBeVisible();
});

it("falls back to the initials when the poster does not load", () => {
  render(<Poster url={POSTER} title="Lioness S03E08" workflow="TV" />);

  fireEvent.error(screen.getByRole("img"));

  expect(screen.queryByRole("img")).toBeNull();
  expect(screen.getByText("L")).toBeVisible();
});

it("tries the next poster when the file's poster address changes after a failure", () => {
  const { rerender } = render(
    <Poster url={POSTER} title="A Paper Lantern" workflow="Movies" />,
  );
  fireEvent.error(screen.getByRole("img"));

  rerender(
    <Poster url={`${POSTER}-2`} title="A Paper Lantern" workflow="Movies" />,
  );

  expect(screen.getByRole("img")).toHaveAttribute("src", `${POSTER}-2`);
});

it("takes the first letters of the first two words, leaving out small words and episode codes", () => {
  const { rerender } = render(
    <Poster url={null} title="A Paper Lantern" workflow="Movies" />,
  );
  expect(screen.getByText("PL")).toBeVisible();

  rerender(<Poster url={null} title="Lioness S03E08" workflow="Movies" />);
  expect(screen.getByText("L")).toBeVisible();
});

it("tints each workflow its own colour, the same every time", () => {
  const hueOf = (workflow: string) => {
    const { container, unmount } = render(
      <Poster url={null} title="Sintel" workflow={workflow} />,
    );
    const hue = (
      container.firstElementChild as HTMLElement
    ).style.getPropertyValue("--tile-hue");
    unmount();
    return hue;
  };

  expect(hueOf("Movies")).toBe(hueOf("Movies"));
  expect(hueOf("Movies")).not.toBe(hueOf("TV"));
});
