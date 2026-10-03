import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Chip } from "./chip";

describe("Chip", () => {
  it("is a plain label until given a meaning", () => {
    const { container } = render(<Chip>Idle</Chip>);

    const chip = screen.getByText("Idle");
    expect(chip).toHaveClass("mm-chip", "mm-chip--neutral");
    expect(chip).not.toHaveAttribute("data-status");
    expect(container.querySelector(".mm-status-dot")).toBeNull();
  });

  it("draws a meaning as its status pill, with the dot of that meaning", () => {
    const { container } = render(<Chip meaning="todo">Waiting</Chip>);

    const chip = screen.getByText("Waiting");
    expect(chip).toHaveClass("mm-chip", "mm-status-pill");
    expect(chip).not.toHaveClass("mm-chip--neutral");
    expect(chip).toHaveAttribute("data-status", "todo");
    expect(container.querySelector(".mm-status-dot")).toHaveAttribute(
      "data-status",
      "todo",
    );
  });

  it("draws a dot that a screen reader skips", () => {
    const { container } = render(<Chip meaning="done">Answering</Chip>);

    expect(container.querySelector(".mm-status-dot")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
  });

  it("can drop the dot of a status", () => {
    const { container } = render(
      <Chip meaning="done" dot={false}>
        Keep
      </Chip>,
    );

    expect(container.querySelector(".mm-status-dot")).toBeNull();
  });

  it("passes through attributes such as a test id", () => {
    render(
      <Chip meaning="broken" data-testid="status">
        Can&apos;t reach Weir
      </Chip>,
    );

    expect(screen.getByTestId("status")).toHaveTextContent("Can't reach Weir");
  });
});
