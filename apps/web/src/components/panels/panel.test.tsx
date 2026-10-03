import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { Panel } from "./panel";

function renderPanel(props: Partial<Parameters<typeof Panel>[0]> = {}) {
  return render(
    <MemoryRouter>
      <Panel title="Just finished" {...props}>
        <p>Body</p>
      </Panel>
    </MemoryRouter>,
  );
}

describe("Panel", () => {
  it("is a region named by its title", () => {
    renderPanel();

    expect(
      screen.getByRole("region", { name: "Just finished" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Just finished" }),
    ).toBeInTheDocument();
  });

  it("keeps a note under its title inside the header, and the title still names the panel", () => {
    renderPanel({ title: "Connections", note: "5/5 answering" });

    const region = screen.getByRole("region", { name: "Connections" });
    expect(region.querySelector(".mm-panel__head--note")).not.toBeNull();
    expect(within(region).getByText("5/5 answering")).toHaveClass(
      "mm-panel__note",
    );
  });

  it("shows its count beside the title and the body under it", () => {
    renderPanel({ count: "4 cleaned today" });

    expect(screen.getByText("4 cleaned today")).toBeInTheDocument();
    expect(screen.getByText("Body")).toBeInTheDocument();
  });

  it("puts controls in the count's place, and describes the panel for a screen reader", () => {
    renderPanel({
      count: "4 cleaned today",
      controls: <button type="button">Movies</button>,
      description: "4 cleaned today",
    });

    const header = screen
      .getByRole("heading", { name: "Just finished" })
      .closest("header") as HTMLElement;
    expect(
      within(header).getByRole("button", { name: "Movies" }),
    ).toBeVisible();
    expect(header.querySelector(".mm-panel__count")).toBeNull();
    expect(
      screen.getByRole("region", { name: "Just finished" }),
    ).toHaveAccessibleDescription("4 cleaned today");
  });

  it("links to where its subject is managed, naming the panel in the link", () => {
    renderPanel({ to: "/activity", toLabel: "Activity" });

    const link = screen.getByRole("link", { name: "Activity: Just finished" });
    expect(link).toHaveAttribute("href", "/activity");
    expect(link).toHaveTextContent("Activity");
  });

  it("keeps the link's name when it shows only an arrow", () => {
    renderPanel({ to: "/activity", toLabel: "Activity", iconOnly: true });

    const link = screen.getByRole("link", { name: "Activity: Just finished" });
    expect(link).not.toHaveTextContent("Activity");
  });

  it("shows other words than the link's name when asked, and keeps the name for a screen reader", () => {
    renderPanel({ to: "/activity", toLabel: "Activity", toText: "3 more" });

    const link = screen.getByRole("link", { name: "Activity: Just finished" });
    expect(link).toHaveTextContent("3 more");
    expect(link).not.toHaveTextContent("Activity");
  });

  it("has no link when it is given nowhere to go", () => {
    renderPanel();

    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("is a level 3 heading inside a page's own section when asked", () => {
    renderPanel({ headingLevel: 3 });

    expect(
      screen.getByRole("heading", { level: 3, name: "Just finished" }),
    ).toBeInTheDocument();
  });

  it("puts its own controls in the header, after the title", () => {
    const { container } = renderPanel({
      aside: <button type="button">Add one</button>,
    });

    const header = container.querySelector<HTMLElement>(".mm-panel__head");
    expect(header).toContainElement(
      screen.getByRole("button", { name: "Add one" }),
    );
  });

  it("lets the page point at its title", () => {
    renderPanel({ headingId: "files-heading" });

    expect(
      screen.getByRole("heading", { name: "Just finished" }),
    ).toHaveAttribute("id", "files-heading");
  });

  it("pads its body and stops clipping when it holds fields", () => {
    const { container } = renderPanel({ padded: true });

    expect(container.querySelector(".mm-panel")).toHaveClass(
      "mm-panel--padded",
    );
  });
});
