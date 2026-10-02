import { render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { PageToolbar } from "./page-toolbar";
import {
  ShellHeaderProvider,
  useHeaderTabsSlotRef,
} from "./shell-header-context";

const TABS = [
  { id: "overview", label: "Overview" },
  { id: "jobs", label: "Jobs" },
] as const;

/** The shell's header, reduced to the place its tabs go. */
function TitleLine() {
  const slotRef = useHeaderTabsSlotRef();
  return <div data-testid="title-line" ref={slotRef} />;
}

function stubWindowWidth(wide: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: wide && query.includes("min-width: 1024px"),
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function renderInShell(actions?: ReactNode) {
  return render(
    <ShellHeaderProvider>
      <TitleLine />
      <PageToolbar
        tabs={TABS}
        activeId="overview"
        onSelect={vi.fn()}
        ariaLabel="Example sections"
        idPrefix="example-tab"
        panelId="example-panel"
        actions={actions}
        dataTestId="example-tabs"
      />
    </ShellHeaderProvider>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("where the header has room", () => {
  it("puts the tabs on the title line, as the tab list that carries the test id", () => {
    stubWindowWidth(true);
    renderInShell();

    const titleLine = screen.getByTestId("title-line");
    const tabs = within(titleLine).getByRole("tablist", {
      name: "Example sections",
    });
    expect(tabs).toHaveAttribute("data-testid", "example-tabs");
    expect(tabs).toHaveClass("mm-page-tabs--title");
  });

  it("has no row under the title when the page has no buttons", () => {
    stubWindowWidth(true);
    const { container } = renderInShell();

    expect(container.querySelector(".mm-page-toolbar")).toBeNull();
    expect(screen.queryByRole("navigation", { name: "Page tabs" })).toBeNull();
  });

  it("keeps the page's buttons in a row of their own under the title", () => {
    stubWindowWidth(true);
    const { container } = renderInShell(
      <button type="button">Add workflow</button>,
    );

    const row = container.querySelector(".mm-page-toolbar");
    expect(row).toHaveClass("mm-page-toolbar--buttons");
    expect(
      within(row as HTMLElement).getByRole("button", { name: "Add workflow" }),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId("title-line")).queryByRole("button"),
    ).toBeNull();
  });
});

describe("where the header is too narrow", () => {
  it("leaves the header and puts the tabs in the page's own row, beside its buttons", () => {
    stubWindowWidth(false);
    renderInShell(<button type="button">Add workflow</button>);

    expect(
      within(screen.getByTestId("title-line")).queryByRole("tablist"),
    ).toBeNull();
    const row = screen.getByRole("navigation", { name: "Page tabs" })
      .parentElement as HTMLElement;
    expect(
      within(row).getByRole("tablist", { name: "Example sections" }),
    ).toHaveClass("mm-page-tabs--row");
    expect(
      within(row).getByRole("button", { name: "Add workflow" }),
    ).toBeInTheDocument();
  });

  it("is where the tabs stay when the window cannot say how wide it is", () => {
    renderInShell();

    expect(
      screen.getByRole("navigation", { name: "Page tabs" }),
    ).toBeInTheDocument();
  });
});
