import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { StatusDot } from "./status-dot";

describe("StatusDot", () => {
  it("carries its meaning and is hidden from assistive technology", () => {
    const { container } = render(<StatusDot meaning="attention" />);

    const dot = container.firstElementChild;
    expect(dot).toHaveAttribute("data-status", "attention");
    expect(dot).toHaveAttribute("aria-hidden", "true");
    expect(dot).toHaveClass("mm-status-dot");
  });

  it("keeps a class the caller adds", () => {
    const { container } = render(<StatusDot meaning="done" className="mm-x" />);

    expect(container.firstElementChild).toHaveClass("mm-status-dot", "mm-x");
  });
});
