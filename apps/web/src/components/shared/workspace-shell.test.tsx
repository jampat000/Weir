import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  WorkspacePage,
  WorkspacePanel,
  WorkspaceRailLayout,
  WorkspaceTabList,
} from "./workspace-shell";

describe("workspace shell", () => {
  it("connects horizontal workspace tabs to their shared panel", () => {
    const onSelect = vi.fn();

    render(
      <WorkspacePage
        variant="tabs"
        eyebrow="MediaMop"
        title="Example"
        description="Example workspace"
      >
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

  it("keeps rail navigation and content in one semantic workspace", () => {
    render(
      <WorkspaceRailLayout
        navigation={
          <button role="tab" aria-selected="true">
            General
          </button>
        }
        navigationLabel="Settings sections"
        panelId="settings-panel"
        panelLabelledBy="settings-tab-general"
        dataTestId="settings-workspace"
      >
        General settings
      </WorkspaceRailLayout>,
    );

    expect(screen.getByTestId("settings-workspace")).toHaveClass(
      "mm-workspace-layout--rail",
    );
    expect(
      screen.getByRole("navigation", { name: "Settings sections" }),
    ).toBeVisible();
    expect(
      screen.getByRole("tablist", { name: "Settings sections" }),
    ).toBeVisible();
    expect(screen.getByRole("tabpanel")).toHaveTextContent("General settings");
  });
});
