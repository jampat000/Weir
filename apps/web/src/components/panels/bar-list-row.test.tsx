import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { BarListRow } from "./bar-list-row";

const bar = (container: HTMLElement) =>
  container.querySelector<HTMLElement>(".mm-bar-row__track i");

describe("BarListRow", () => {
  it("shows the label, the number and a bar as full as its share", () => {
    const { container } = render(
      <BarListRow
        label="Already clean"
        value="12"
        fraction={0.5}
        color="var(--mm-lane-queued)"
      />,
    );

    expect(screen.getByText("Already clean")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(bar(container)).toHaveStyle({ width: "50%" });
  });

  it("holds a share outside 0 to 1 to the ends of the track", () => {
    const over = render(
      <BarListRow label="a" value="1" fraction={3} color="red" />,
    );
    expect(bar(over.container)).toHaveStyle({ width: "100%" });

    const under = render(
      <BarListRow label="b" value="1" fraction={-1} color="red" />,
    );
    expect(bar(under.container)).toHaveStyle({ width: "0%" });
  });

  it("reads a share that is not a number as empty", () => {
    const { container } = render(
      <BarListRow label="a" value="0" fraction={Number.NaN} color="red" />,
    );

    expect(bar(container)).toHaveStyle({ width: "0%" });
  });

  it("is a link, named for what it shows, when it has somewhere to go", () => {
    render(
      <MemoryRouter>
        <BarListRow
          label="Rejected"
          value="3"
          fraction={0.2}
          color="red"
          to="/activity?show=failed"
          linkName="Rejected: 3, show in Activity"
        />
      </MemoryRouter>,
    );

    expect(
      screen.getByRole("link", { name: "Rejected: 3, show in Activity" }),
    ).toHaveAttribute("href", "/activity?show=failed");
  });

  it("is plain text when it has nowhere to go", () => {
    render(<BarListRow label="a" value="1" fraction={0.1} color="red" />);

    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("marks an empty row and a lit row so they can be drawn quietly and brightly", () => {
    const empty = render(
      <BarListRow label="a" value="0" fraction={0} color="red" empty />,
    );
    expect(empty.container.firstChild).toHaveClass("mm-bar-row--empty");

    const lit = render(
      <BarListRow label="b" value="4" fraction={1} color="red" lit />,
    );
    expect(lit.container.firstChild).toHaveClass("mm-bar-row--lit");
  });
});
