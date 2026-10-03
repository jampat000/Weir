import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { SegmentedControl } from "./segmented-control";

const OPTIONS = [
  { value: "all", label: "Everything" },
  { value: "download", label: "New downloads" },
  { value: "library", label: "Library cleaning" },
] as const;

describe("SegmentedControl", () => {
  it("is a named group of buttons, with only the chosen one pressed", () => {
    render(
      <SegmentedControl
        options={OPTIONS}
        value="download"
        onChange={() => undefined}
        ariaLabel="Show work from"
      />,
    );

    const group = screen.getByRole("group", { name: "Show work from" });
    expect(group).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "New downloads" }),
    ).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Everything" })).toHaveAttribute(
      "aria-pressed",
      "false",
    );
  });

  it("reports the option that was clicked", () => {
    const onChange = vi.fn();
    render(
      <SegmentedControl
        options={OPTIONS}
        value="all"
        onChange={onChange}
        ariaLabel="Show work from"
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Library cleaning" }));

    expect(onChange).toHaveBeenCalledWith("library");
  });

  it("folds the options it is told to into a More menu, and still reports a choice made there", () => {
    const onChange = vi.fn();
    render(
      <SegmentedControl
        options={OPTIONS}
        value="all"
        onChange={onChange}
        ariaLabel="Show work from"
        fold={{ hidden: new Set(["library"]), menuLabel: "More sources" }}
      />,
    );

    expect(
      screen.queryByRole("button", { name: "Library cleaning" }),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "More" }));
    const menu = screen.getByRole("menu", { name: "More sources" });
    fireEvent.click(
      within(menu).getByRole("menuitem", { name: "Library cleaning" }),
    );

    expect(onChange).toHaveBeenCalledWith("library");
  });

  it("has no More button when nothing is folded", () => {
    render(
      <SegmentedControl
        options={OPTIONS}
        value="all"
        onChange={() => undefined}
        ariaLabel="Show work from"
        fold={{ hidden: new Set<string>(), menuLabel: "More sources" }}
      />,
    );

    expect(
      screen.queryByRole("button", { name: "More" }),
    ).not.toBeInTheDocument();
  });
});
