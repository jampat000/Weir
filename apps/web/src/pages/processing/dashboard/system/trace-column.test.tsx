import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { TraceColumn } from "./trace-column";

const whole = (value: number) => Math.round(value).toString();

function column(value: number | null) {
  return (
    <TraceColumn
      colour="var(--mm-success)"
      label="CPU"
      value={value}
      figure={whole}
      unit="%"
      sub="16 cores · Weir 3%"
    >
      <i data-testid="trace" />
    </TraceColumn>
  );
}

describe("a column with a trace", () => {
  it("shows the reading's name, its figure with the unit, a line of detail and the trace", () => {
    render(column(37.4));

    expect(screen.getByText("CPU")).toBeInTheDocument();
    expect(screen.getByText("37")).toBeInTheDocument();
    expect(screen.getByText("%")).toBeInTheDocument();
    expect(screen.getByText("16 cores · Weir 3%")).toBeInTheDocument();
    expect(screen.getByTestId("trace")).toBeInTheDocument();
  });

  it("gives its swatch, and so its trace, the colour it is given", () => {
    const { container } = render(column(1));

    expect(
      (container.firstElementChild as HTMLElement).style.getPropertyValue(
        "--mm-sy-colour",
      ),
    ).toBe("var(--mm-success)");
  });

  it("shows a dash for a reading that cannot be taken", () => {
    render(column(null));

    expect(screen.getByText("–")).toBeInTheDocument();
    expect(screen.queryByText("%")).toBeNull();
  });

  it("gives the detail line in full as a tooltip, for when it is cut short", () => {
    render(column(1));

    expect(screen.getByText("16 cores · Weir 3%")).toHaveAttribute(
      "title",
      "16 cores · Weir 3%",
    );
  });
});
