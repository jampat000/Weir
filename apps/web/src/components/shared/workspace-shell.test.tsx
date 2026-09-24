import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  WorkspacePage,
  WorkspacePanel,
  WorkspaceTabList,
} from "./workspace-shell";

// The title row carries Pause, which talks to the server; this test is about the tabs.
vi.mock("../shell/pause-control", () => ({ PauseControl: () => null }));

describe("workspace shell", () => {
  it("connects the themed horizontal tabs to their shared panel", () => {
    const onSelect = vi.fn();

    render(
      <WorkspacePage title="Example" description="Example sections">
        <WorkspaceTabList
          tabs={[
            { id: "overview", label: "Overview" },
            { id: "jobs", label: "Jobs" },
          ]}
          activeId="overview"
          onSelect={onSelect}
          ariaLabel="Example sections"
          idPrefix="example-tab"
          panelId="example-panel"
        />
        <WorkspacePanel
          id="example-panel"
          labelledBy="example-tab-overview"
          context="What this tab does."
        >
          <p>Panel content</p>
        </WorkspacePanel>
      </WorkspacePage>,
    );

    expect(screen.getByRole("heading", { name: "Example" })).toBeVisible();
    expect(
      screen.getByRole("tablist", { name: "Example sections" }),
    ).toHaveClass("mm-workspace-tabs__list");
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute(
      "aria-controls",
      "example-panel",
    );
    expect(screen.getByRole("tabpanel")).toHaveAttribute(
      "aria-labelledby",
      "example-tab-overview",
    );

    fireEvent.click(screen.getByRole("tab", { name: "Jobs" }));
    expect(onSelect).toHaveBeenCalledWith("jobs");
  });

  it("names the landmark apart from the tab list inside it", () => {
    renderTabs("overview", vi.fn());

    expect(
      screen.getByRole("navigation", { name: "Page tabs" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("tablist", { name: "Example sections" }),
    ).toBeInTheDocument();
  });

  it("lets Tab reach only the chosen tab", () => {
    renderTabs("jobs", vi.fn());

    expect(screen.getByRole("tab", { name: "Jobs" })).toHaveAttribute(
      "tabindex",
      "0",
    );
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute(
      "tabindex",
      "-1",
    );
  });

  it("moves to and chooses the next tab with the arrow keys, wrapping at the ends", () => {
    const onSelect = vi.fn();
    renderTabs("logs", onSelect);

    fireEvent.keyDown(screen.getByRole("tab", { name: "Logs" }), {
      key: "ArrowRight",
    });

    expect(onSelect).toHaveBeenLastCalledWith("overview");
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveFocus();
  });

  it("jumps to the last tab with End and back to the first with Home", () => {
    const onSelect = vi.fn();
    renderTabs("overview", onSelect);
    const overview = screen.getByRole("tab", { name: "Overview" });

    fireEvent.keyDown(overview, { key: "End" });
    expect(onSelect).toHaveBeenLastCalledWith("logs");

    fireEvent.keyDown(overview, { key: "Home" });
    expect(onSelect).toHaveBeenLastCalledWith("overview");
  });
});

function renderTabs(activeId: string, onSelect: (id: string) => void) {
  render(
    <WorkspaceTabList
      tabs={[
        { id: "overview", label: "Overview" },
        { id: "jobs", label: "Jobs" },
        { id: "logs", label: "Logs" },
      ]}
      activeId={activeId}
      onSelect={onSelect}
      ariaLabel="Example sections"
      idPrefix="example-tab"
      panelId="example-panel"
    />,
  );
}
