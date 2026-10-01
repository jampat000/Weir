import { render, screen } from "@testing-library/react";
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

  it("shows its count beside the title and the body under it", () => {
    renderPanel({ count: "4 cleaned today" });

    expect(screen.getByText("4 cleaned today")).toBeInTheDocument();
    expect(screen.getByText("Body")).toBeInTheDocument();
  });

  it("links to where its subject is managed, naming the panel in the link", () => {
    renderPanel({ to: "/history", toLabel: "History" });

    const link = screen.getByRole("link", { name: "History: Just finished" });
    expect(link).toHaveAttribute("href", "/history");
    expect(link).toHaveTextContent("History");
  });

  it("keeps the link's name when it shows only an arrow", () => {
    renderPanel({ to: "/history", toLabel: "History", iconOnly: true });

    const link = screen.getByRole("link", { name: "History: Just finished" });
    expect(link).not.toHaveTextContent("History");
  });

  it("has no link when it is given nowhere to go", () => {
    renderPanel();

    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("puts what leads the title before it", () => {
    renderPanel({ leading: <span data-testid="live-dot" /> });

    const heading = screen.getByRole("heading", { name: "Just finished" });
    const dot = screen.getByTestId("live-dot");
    expect(
      dot.compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });
});
