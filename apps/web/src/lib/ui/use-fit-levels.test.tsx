import { act, render } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

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
});
