import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { ProcessingPage } from "./processing-page";

type Workflow = { id: number; name: string; enabled: boolean };

let workflows: Workflow[];

vi.mock("../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({ data: workflows }),
}));
vi.mock("./dashboard/live-view", () => ({
  LiveView: ({
    filter,
    workflowId,
  }: {
    filter: string;
    workflowId: number | null;
  }) => (
    <p data-testid="live-view">
      live {filter} {String(workflowId)}
    </p>
  ),
}));
vi.mock("./dashboard/system-view", () => ({
  SystemView: ({ workflowId }: { workflowId: number | null }) => (
    <p data-testid="system-view">system {String(workflowId)}</p>
  ),
}));

function Address() {
  const { pathname, search } = useLocation();
  return <p data-testid="address">{`${pathname}${search}`}</p>;
}

function show(address: string) {
  render(
    <MemoryRouter initialEntries={[address]}>
      <ProcessingPage />
      <Address />
    </MemoryRouter>,
  );
}

const view = () => screen.getByRole("group", { name: "Dashboard view" });

beforeEach(() => {
  workflows = [
    { id: 1, name: "TV", enabled: true },
    { id: 2, name: "Movies", enabled: true },
    { id: 3, name: "Archive", enabled: false },
  ];
});

describe("the Dashboard's views", () => {
  it("opens Live when the address names no view", () => {
    show("/");

    expect(screen.getByTestId("live-view")).toBeInTheDocument();
    expect(screen.queryByTestId("system-view")).toBeNull();
    expect(
      within(view()).getByRole("button", { name: "Live" }),
    ).toHaveAttribute("aria-pressed", "true");
  });

  it("opens System from the address", () => {
    show("/?view=system");

    expect(screen.getByTestId("system-view")).toBeInTheDocument();
    expect(screen.queryByTestId("live-view")).toBeNull();
    expect(
      within(view()).getByRole("button", { name: "System" }),
    ).toHaveAttribute("aria-pressed", "true");
  });

  it("puts System in the address and takes Live out of it again", () => {
    show("/");

    fireEvent.click(within(view()).getByRole("button", { name: "System" }));
    expect(screen.getByTestId("address")).toHaveTextContent("/?view=system");
    expect(screen.getByTestId("system-view")).toBeInTheDocument();

    fireEvent.click(within(view()).getByRole("button", { name: "Live" }));
    expect(screen.getByTestId("address")).toHaveTextContent(/^\/$/);
    expect(screen.getByTestId("live-view")).toBeInTheDocument();
  });

  it("offers the choice of kind of work on Live only", () => {
    show("/");
    expect(screen.getByTestId("live-filter")).toBeInTheDocument();

    fireEvent.click(within(view()).getByRole("button", { name: "System" }));
    expect(screen.queryByTestId("live-filter")).toBeNull();
  });
});

describe("the workflow picker", () => {
  it("lists the workflows that are switched on, after All workflows", () => {
    show("/");

    const picker = screen.getByTestId("workflow-picker");
    expect(picker).toHaveTextContent("All workflows");
    fireEvent.click(picker);
    expect(
      screen.getAllByRole("option").map((option) => option.textContent),
    ).toEqual(["All workflows", "TV", "Movies"]);
  });

  it("is not drawn when there is only one workflow to choose", () => {
    workflows = workflows.slice(0, 1);

    show("/");

    expect(screen.queryByTestId("workflow-picker")).toBeNull();
  });

  it("narrows the view to the workflow chosen and keeps it in the address", () => {
    show("/");

    fireEvent.click(screen.getByTestId("workflow-picker"));
    fireEvent.click(screen.getByRole("option", { name: "Movies" }));

    expect(screen.getByTestId("address")).toHaveTextContent("/?workflow=2");
    expect(screen.getByTestId("live-view")).toHaveTextContent("live all 2");
    expect(screen.getByTestId("workflow-picker")).toHaveTextContent("Movies");
  });

  it("starts narrowed to the workflow in the address, and keeps it across the views", () => {
    show("/?workflow=1");

    expect(screen.getByTestId("live-view")).toHaveTextContent("live all 1");

    fireEvent.click(within(view()).getByRole("button", { name: "System" }));
    expect(screen.getByTestId("address")).toHaveTextContent(
      "/?workflow=1&view=system",
    );
    expect(screen.getByTestId("system-view")).toHaveTextContent("system 1");
  });

  it("shows every workflow again when All workflows is chosen", () => {
    show("/?workflow=1");

    fireEvent.click(screen.getByTestId("workflow-picker"));
    fireEvent.click(screen.getByRole("option", { name: "All workflows" }));

    expect(screen.getByTestId("address")).toHaveTextContent(/^\/$/);
    expect(screen.getByTestId("live-view")).toHaveTextContent("live all null");
  });

  it("ignores a workflow that is switched off or does not exist", () => {
    show("/?workflow=3");

    expect(screen.getByTestId("live-view")).toHaveTextContent("live all null");
    expect(screen.getByTestId("workflow-picker")).toHaveTextContent(
      "All workflows",
    );
  });
});
