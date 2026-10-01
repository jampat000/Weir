import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Chip } from "./chip";

describe("Chip", () => {
  it("says its state in words", () => {
    render(<Chip tone="warning">Paused</Chip>);

    expect(screen.getByText("Paused")).toHaveClass(
      "mm-chip",
      "mm-chip--warning",
    );
  });

  it("is neutral until given a tone", () => {
    render(<Chip>Idle</Chip>);

    expect(screen.getByText("Idle")).toHaveClass("mm-chip--neutral");
  });

  it("draws a dot that a screen reader skips", () => {
    const { container } = render(<Chip tone="healthy">Answering</Chip>);

    expect(container.querySelector(".mm-chip__dot")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
  });

  it("can drop the dot", () => {
    const { container } = render(<Chip dot={false}>Idle</Chip>);

    expect(container.querySelector(".mm-chip__dot")).toBeNull();
  });

  it("passes through attributes such as a test id", () => {
    render(
      <Chip tone="failed" data-testid="status">
        Can&apos;t reach Weir
      </Chip>,
    );

    expect(screen.getByTestId("status")).toHaveTextContent("Can't reach Weir");
  });
});
