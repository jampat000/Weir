import { act, render } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { useFitLevels } from "./use-fit-levels";

/** A row that needs `needs` pixels and is given a room that grows with the level: 100 more for each. */
function Probe({
  needs,
  room,
  refit = "",
  enabled = true,
  levels = 3,
  report,
}: {
  needs: number;
  room: number;
  refit?: string;
  enabled?: boolean;
  levels?: number;
  report: (level: number) => void;
}) {
  const [row, setRow] = useState<HTMLDivElement | null>(null);
  const level = useFitLevels(row, levels, refit, enabled);
  report(level);
  if (row) {
    Object.defineProperty(row, "scrollWidth", {
      configurable: true,
      value: needs,
    });
    Object.defineProperty(row, "clientWidth", {
      configurable: true,
      value: room + level * 100,
    });
  }
  return (
    <header>
      <div ref={setRow} />
    </header>
  );
}

function levelFor(
  props: Partial<Parameters<typeof Probe>[0]> & { needs: number; room: number },
) {
  let level = -1;
  render(<Probe {...props} report={(l) => (level = l)} />);
  return level;
}

afterEach(() => vi.unstubAllGlobals());

describe("useFitLevels", () => {
  it("stays at 0 when the row fits", () => {
    expect(levelFor({ needs: 300, room: 400 })).toBe(0);
  });

  it("climbs one level at a time until the row fits, and no higher", () => {
    expect(levelFor({ needs: 450, room: 300 })).toBe(2);
  });

  it("stops at the last level when the row still does not fit", () => {
    expect(levelFor({ needs: 5000, room: 300 })).toBe(3);
  });

  it("does nothing where the controls cannot help", () => {
    expect(levelFor({ needs: 5000, room: 300, enabled: false })).toBe(0);
  });

  it("starts again from 0 when what the row holds changes", () => {
    let level = -1;
    const { rerender } = render(
      <Probe needs={450} room={300} report={(l) => (level = l)} />,
    );
    expect(level).toBe(2);

    act(() => {
      rerender(
        <Probe
          needs={150}
          room={300}
          refit="fewer"
          report={(l) => (level = l)}
        />,
      );
    });

    expect(level).toBe(0);
  });

  it("starts again from 0 when the box the row sits in changes size, so room that comes back gives words back", () => {
    let resized: () => void = () => undefined;
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(callback: () => void) {
          resized = callback;
        }
        observe() {}
        disconnect() {}
      },
    );
    let level = -1;
    const { rerender, container } = render(
      <Probe needs={450} room={300} report={(l) => (level = l)} />,
    );
    expect(level).toBe(2);

    const box = container.querySelector("header") as HTMLElement;
    Object.defineProperty(box, "clientWidth", {
      configurable: true,
      value: 900,
    });
    rerender(<Probe needs={450} room={500} report={(l) => (level = l)} />);
    act(() => resized());

    expect(level).toBe(0);
  });
});
