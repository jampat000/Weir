import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { WorkspacePage, WorkspacePanel } from "./workspace-shell";

describe("workspace shell", () => {
  it("names the panel by the tab that opens it", () => {
    render(
      <WorkspacePage dataTestId="example-page">
        <WorkspacePanel
          id="example-panel"
          labelledBy="example-tab-overview"
          context="What this tab does."
        >
          <p>Panel content</p>
        </WorkspacePanel>
      </WorkspacePage>,
    );

    expect(screen.getByTestId("example-page")).toBeVisible();
    expect(screen.getByRole("tabpanel")).toHaveAttribute(
      "aria-labelledby",
      "example-tab-overview",
    );
    expect(screen.getByText("What this tab does.")).toBeVisible();
    expect(screen.getByText("Panel content")).toBeVisible();
  });
});
