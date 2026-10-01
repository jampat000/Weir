import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { StatSide, StatTile, StatUnit } from "./stat-tile";

describe("StatTile", () => {
  it("shows the label, the aside, the figure with its words, and the body", () => {
    render(
      <MemoryRouter>
        <StatTile
          label="Today"
          aside="3 need a look"
          figure={
            <>
              12
              <StatUnit>cleaned</StatUnit>
              <StatSide>4.2 GB saved</StatSide>
            </>
          }
        >
          <p>Chart</p>
        </StatTile>
      </MemoryRouter>,
    );

    const tile = screen.getByRole("region", { name: "Today" });
    expect(tile).toHaveTextContent("3 need a look");
    expect(tile).toHaveTextContent("12cleaned4.2 GB saved");
    expect(screen.getByText("Chart")).toBeInTheDocument();
  });

  it("makes the label a link, with its own name, when it is given a destination", () => {
    render(
      <MemoryRouter>
        <StatTile
          label="Today"
          figure="12"
          to="/history"
          linkName="History: Today"
        >
          body
        </StatTile>
      </MemoryRouter>,
    );

    expect(
      screen.getByRole("link", { name: "History: Today" }),
    ).toHaveAttribute("href", "/history");
  });

  it("leaves the label plain text when there is nowhere to go", () => {
    render(
      <MemoryRouter>
        <StatTile label="Today" figure="12">
          body
        </StatTile>
      </MemoryRouter>,
    );

    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("omits the aside when it has none", () => {
    const { container } = render(
      <MemoryRouter>
        <StatTile label="Today" figure="12">
          body
        </StatTile>
      </MemoryRouter>,
    );

    expect(container.querySelector(".mm-stat__aside")).toBeNull();
  });
});
