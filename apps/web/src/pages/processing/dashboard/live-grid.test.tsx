import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { gridRows } from "./dashboard-layout";
import { LiveGrid } from "./live-grid";

describe("the Live view's grid", () => {
  it("is the shared grid with the right column beside it, its rows a roomy page's until measured", () => {
    render(
      <LiveGrid layout={{ sideBySide: true, band: "across" }}>
        <p>part</p>
      </LiveGrid>,
    );

    const grid = screen.getByTestId("dashboard-live");
    expect(grid).toHaveClass("mm-dash__grid", "mm-dash__grid--beside");
    expect(grid.style.getPropertyValue("grid-template-rows")).toBe(
      gridRows(0).template,
    );
    expect(screen.getByText("part")).toBeInTheDocument();
  });

  it("is a column of parts, with no rows of its own, where the page scrolls", () => {
    render(
      <LiveGrid layout={{ sideBySide: false, band: "stacked" }}>
        <p>part</p>
      </LiveGrid>,
    );

    const grid = screen.getByTestId("dashboard-live");
    expect(grid).toHaveClass("mm-dash__grid");
    expect(grid).not.toHaveClass("mm-dash__grid--beside");
    expect(grid.style.getPropertyValue("grid-template-rows")).toBe("");
  });
});
