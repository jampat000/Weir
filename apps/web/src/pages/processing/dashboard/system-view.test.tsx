import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { GRID_COLUMNS } from "./dashboard-layout";
import { SystemView } from "./system-view";

vi.mock("../../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({
    data: [],
    isPending: false,
    isError: false,
  }),
}));
vi.mock("../../../lib/pause/pause-queries", () => ({
  usePauseQuery: () => ({ data: { paused: false } }),
}));
vi.mock("./use-next-items", () => ({ useNextItems: () => [] }));
vi.mock("./health-detail", () => ({ HealthDetail: () => <p>health</p> }));
vi.mock("./timers-panel", () => ({ TimersPanel: () => <p>timers</p> }));
vi.mock("../../system/tabs/logs/jobs-section", () => ({
  JobsSection: () => <p>jobs</p>,
}));

describe("the System view's layout", () => {
  it("shares Live's columns where the right column sits beside the page", () => {
    render(
      <SystemView
        workflowId={null}
        layout={{ sideBySide: true, band: "across" }}
      />,
    );

    const view = screen.getByTestId("dashboard-system");
    expect(view).toHaveClass("mm-sys--beside");
    expect(view.style.getPropertyValue("--dash-columns")).toBe(GRID_COLUMNS);
  });

  it("stacks its parts where the page is narrow", () => {
    render(
      <SystemView
        workflowId={null}
        layout={{ sideBySide: false, band: "stacked" }}
      />,
    );

    expect(screen.getByTestId("dashboard-system")).not.toHaveClass(
      "mm-sys--beside",
    );
  });
});
