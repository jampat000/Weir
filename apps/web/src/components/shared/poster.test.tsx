import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import {
  UNKNOWN_WORKFLOW_HUE,
  WORKFLOW_HUES,
} from "../../lib/processing/workflow-hues";
import {
  MOVIES_AND_TV,
  WithWorkflows,
  workflow,
} from "../../test/with-workflows";
import { Poster } from "./poster";

const POSTER = "/api/v1/artwork/posters/movie-the-general-1926";

it("shows the poster, lazily, with the title as its description", () => {
  render(<Poster url={POSTER} title="The General (1926)" workflow="Movies" />, {
    wrapper: WithWorkflows,
  });

  const image = screen.getByRole("img", { name: "The General (1926)" });
  expect(image).toHaveAttribute("src", POSTER);
  expect(image).toHaveAttribute("loading", "lazy");
  expect(image).toHaveAttribute("decoding", "async");
});

it("shows the title's initials where there is no poster", () => {
  render(
    <Poster url={null} title="The Quiet Harbour (2024)" workflow="Movies" />,
    { wrapper: WithWorkflows },
  );

  expect(screen.queryByRole("img")).toBeNull();
  expect(screen.getByText("QH")).toBeVisible();
});

it("falls back to the initials when the poster does not load", () => {
  render(<Poster url={POSTER} title="Lioness S03E08" workflow="TV" />, {
    wrapper: WithWorkflows,
  });

  fireEvent.error(screen.getByRole("img"));

  expect(screen.queryByRole("img")).toBeNull();
  expect(screen.getByText("L")).toBeVisible();
});

it("tries the next poster when the file's poster address changes after a failure", () => {
  const { rerender } = render(
    <Poster url={POSTER} title="A Paper Lantern" workflow="Movies" />,
    { wrapper: WithWorkflows },
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
    { wrapper: WithWorkflows },
  );
  expect(screen.getByText("PL")).toBeVisible();

  rerender(<Poster url={null} title="Lioness S03E08" workflow="Movies" />);
  expect(screen.getByText("L")).toBeVisible();
});

describe("the colour under the initials", () => {
  const hueOf = (name: string, workflows: ProcessingLibrary[]) => {
    const { container, unmount } = render(
      <Poster url={null} title="Sintel" workflow={name} />,
      {
        wrapper: ({ children }) => (
          <WithWorkflows workflows={workflows}>{children}</WithWorkflows>
        ),
      },
    );
    const hue = (
      container.firstElementChild as HTMLElement
    ).style.getPropertyValue("--tile-hue");
    unmount();
    return Number(hue);
  };

  it("is the palette's hue for the workflow's place in Settings, the same every time", () => {
    expect(hueOf("Movies", MOVIES_AND_TV)).toBe(WORKFLOW_HUES[0]);
    expect(hueOf("TV", MOVIES_AND_TV)).toBe(WORKFLOW_HUES[1]);
    expect(hueOf("TV", MOVIES_AND_TV)).toBe(hueOf("TV", MOVIES_AND_TV));
  });

  it("follows the workflow when Settings puts it elsewhere", () => {
    const reordered = [workflow(1, "Movies", 1), workflow(2, "TV", 0)];

    expect(hueOf("TV", reordered)).toBe(WORKFLOW_HUES[0]);
    expect(hueOf("Movies", reordered)).toBe(WORKFLOW_HUES[1]);
  });

  it("is the neutral hue for a workflow that is not in the list", () => {
    expect(hueOf("Deleted workflow", MOVIES_AND_TV)).toBe(UNKNOWN_WORKFLOW_HUE);
  });
});
