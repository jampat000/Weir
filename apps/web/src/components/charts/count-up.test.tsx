import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { COUNT_UP_MS } from "./count-up-math";
import { CountUp } from "./count-up";
import { RESIZING_CLASS } from "../../lib/ui/resizing-class";

const whole = (value: number) => Math.round(value).toString();

function Figure({ value }: { value: number }) {
  return (
    <p data-testid="figure">
      <CountUp value={value} format={whole} />
    </p>
  );
}

const shown = () => screen.getByTestId("figure").textContent;

beforeEach(() => {
  vi.useFakeTimers({
    toFake: ["requestAnimationFrame", "cancelAnimationFrame", "performance"],
  });
});

afterEach(() => {
  vi.useRealTimers();
  document.body.classList.remove(RESIZING_CLASS);
});

describe("CountUp", () => {
  it("shows its first value at once, without counting from nothing", () => {
    render(<Figure value={42} />);

    expect(shown()).toBe("42");
  });

  it("counts to a new value over 520 ms instead of jumping", () => {
    const { rerender } = render(<Figure value={0} />);

    rerender(<Figure value={100} />);
    act(() => {
      vi.advanceTimersByTime(COUNT_UP_MS / 2);
    });

    const part = Number(shown());
    expect(part).toBeGreaterThan(0);
    expect(part).toBeLessThan(100);
  });

  it("lands on the new value exactly", () => {
    const { rerender } = render(<Figure value={0} />);

    rerender(<Figure value={100} />);
    act(() => {
      vi.advanceTimersByTime(COUNT_UP_MS + 100);
    });

    expect(shown()).toBe("100");
  });

  it("starts the next count from where the last had got to", () => {
    const { rerender } = render(<Figure value={0} />);
    rerender(<Figure value={100} />);
    act(() => {
      vi.advanceTimersByTime(COUNT_UP_MS / 2);
    });
    const part = Number(shown());

    rerender(<Figure value={0} />);
    act(() => {
      vi.advanceTimersByTime(16);
    });

    expect(Number(shown())).toBeLessThanOrEqual(part);
    expect(Number(shown())).toBeGreaterThan(0);
  });

  it("shows a new value at once while the window is being resized", () => {
    document.body.classList.add(RESIZING_CLASS);
    const { rerender } = render(<Figure value={0} />);

    rerender(<Figure value={100} />);

    expect(shown()).toBe("100");
  });

  it("shows a new value at once when reduced motion is asked for", () => {
    vi.stubGlobal("matchMedia", (query: string) => ({
      matches: query.includes("reduce"),
    }));
    const { rerender } = render(<Figure value={0} />);

    rerender(<Figure value={100} />);

    expect(shown()).toBe("100");
    vi.unstubAllGlobals();
  });
});
