import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { RESIZING_CLASS } from "../../lib/ui/resizing-class";
import { LiveTrace, type TraceSeries } from "./live-trace";
import type { TraceSample } from "./live-trace-math";

vi.mock("../../lib/ui/mm-format-date", () => ({
  useAppClockSecondsFormatter: () => (ms: number) => `t${ms / 1000}`,
}));

const WINDOW_MS = 10_000;
const percent = (value: number) => `${Math.round(value)}%`;

const samples = (...values: number[]): TraceSample[] =>
  values.map((value, index) => ({ at: 20_000 + index * 1000, value }));

const series = (...values: number[]): TraceSeries[] => [
  { key: "cpu", colour: "var(--mm-success)", samples: samples(...values) },
];

function Trace({
  lines,
  windowMs = WINDOW_MS,
}: {
  lines: TraceSeries[];
  windowMs?: number;
}) {
  return (
    <LiveTrace
      series={lines}
      windowMs={windowMs}
      top={100}
      format={percent}
      label="CPU, last 10 minutes"
    />
  );
}

const line = () =>
  screen.getByRole("img").querySelector<SVGPathElement>(".mm-trace__line")!;
const dot = () =>
  screen.getByRole("img").querySelector<HTMLElement>(".mm-trace__dot")!;
const lastPoint = () => {
  const points = (line().getAttribute("d") ?? "").split(" ");
  return {
    x: Number(points[points.length - 2].replace("L", "")),
    y: Number(points[points.length - 1]),
  };
};
/** Where a value from 0 to 100 sits on the line, as the drawing's own units put it. */
const yOf = (value: number) => 3 + 45 * (1 - value / 100);

beforeEach(() => {
  vi.useFakeTimers({
    toFake: ["requestAnimationFrame", "cancelAnimationFrame", "performance"],
  });
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  document.body.classList.remove(RESIZING_CLASS);
});

describe("LiveTrace", () => {
  it("draws the samples as a line and says what it shows", () => {
    render(<Trace lines={series(10, 20, 30)} />);

    expect(
      screen.getByRole("img", { name: "CPU, last 10 minutes" }),
    ).toBeInTheDocument();
    expect(line().getAttribute("d")).toMatch(/^M/);
  });

  it("draws nothing, and shows no dot, with no samples", () => {
    render(<Trace lines={series()} />);

    expect(line().getAttribute("d")).toBe("");
    expect(dot().style.opacity).toBe("0");
  });

  it("glows a dot on the newest point, at the right edge once it has arrived", () => {
    render(<Trace lines={series(10, 20, 30)} />);

    expect(dot().style.opacity).toBe("1");
    expect(dot().style.left).toBe("100%");
  });

  it("glides the head to a sample that arrives, rather than jumping to it", () => {
    const { rerender } = render(<Trace lines={series(10, 20, 30)} />);

    rerender(<Trace lines={series(10, 20, 30, 40)} />);
    expect(lastPoint().y).toBeCloseTo(yOf(30), 1);

    act(() => {
      vi.advanceTimersByTime(500);
    });
    expect(lastPoint().y).toBeLessThan(yOf(30));
    expect(lastPoint().y).toBeGreaterThan(yOf(40));

    act(() => {
      vi.advanceTimersByTime(600);
    });
    expect(lastPoint().y).toBeCloseTo(yOf(40), 1);
  });

  it("slides the whole line left as the head moves on", () => {
    const { rerender } = render(<Trace lines={series(10, 90, 10)} />);
    const settled = line().getAttribute("d");

    rerender(<Trace lines={series(10, 90, 10, 10)} />);
    act(() => {
      vi.advanceTimersByTime(500);
    });

    expect(line().getAttribute("d")).not.toBe(settled);
  });

  it("puts the head at the newest sample at once when reduced motion is asked for", () => {
    vi.stubGlobal("matchMedia", (query: string) => ({
      matches: query.includes("reduce"),
    }));
    const { rerender } = render(<Trace lines={series(10, 20, 30)} />);

    rerender(<Trace lines={series(10, 20, 30, 40)} />);

    expect(lastPoint().y).toBeCloseTo(yOf(40), 1);
  });

  it("holds still while the window is being resized", () => {
    document.body.classList.add(RESIZING_CLASS);
    const { rerender } = render(<Trace lines={series(10, 20, 30)} />);

    rerender(<Trace lines={series(10, 20, 30, 40)} />);

    expect(lastPoint().y).toBeCloseTo(yOf(40), 1);
  });

  it("draws one line for each series", () => {
    render(
      <Trace
        lines={[
          { key: "read", label: "read", colour: "red", samples: samples(1, 2) },
          {
            key: "write",
            label: "write",
            colour: "blue",
            samples: samples(3, 4),
          },
        ]}
      />,
    );

    expect(document.querySelectorAll(".mm-trace__line")).toHaveLength(2);
    expect(document.querySelectorAll(".mm-trace__dot")).toHaveLength(2);
  });
});

describe("LiveTrace's readout", () => {
  beforeEach(() => {
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({
      left: 0,
      top: 0,
      right: 100,
      bottom: 20,
      width: 100,
      height: 20,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    });
  });

  it("says the time and the value under the pointer, in Weir's time zone", () => {
    render(<Trace lines={series(10, 20, 30, 40, 50)} />);

    fireEvent.pointerMove(screen.getByRole("img"), { clientX: 100 });

    expect(screen.getByText("t24 · 50%")).toBeInTheDocument();
  });

  it("follows the pointer along the line", () => {
    render(<Trace lines={series(10, 20, 30, 40, 50)} />);

    fireEvent.pointerMove(screen.getByRole("img"), { clientX: 100 });
    fireEvent.pointerMove(screen.getByRole("img"), { clientX: 80 });

    expect(screen.getByText("t22 · 30%")).toBeInTheDocument();
  });

  it("goes when the pointer leaves", () => {
    render(<Trace lines={series(10, 20, 30)} />);

    fireEvent.pointerMove(screen.getByRole("img"), { clientX: 100 });
    fireEvent.pointerLeave(screen.getByRole("img"));

    expect(screen.queryByText(/^t\d+ · /)).toBeNull();
  });

  it("sits on the side of the line the pointer is not on", () => {
    render(<Trace lines={series(10, 20, 30, 40, 50)} windowMs={4000} />);

    fireEvent.pointerMove(screen.getByRole("img"), { clientX: 100 });
    expect(screen.getByText("t24 · 50%")).toHaveClass("mm-trace__read--left");

    fireEvent.pointerMove(screen.getByRole("img"), { clientX: 0 });
    expect(screen.getByText("t20 · 10%")).not.toHaveClass(
      "mm-trace__read--left",
    );
  });
});
