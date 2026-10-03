import { render } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import {
  foldedChipIndexes,
  useTitleLineFit,
  type TitleLineFit,
} from "./title-line-fit";

/** A row that needs 100px more than it has until the page has taken `stages + fold` steps, one more px each. */
function Probe({
  stages,
  chips,
  needs,
  report,
}: {
  stages: number;
  chips?: number;
  needs: number;
  report: (fit: TitleLineFit) => void;
}) {
  const [row, setRow] = useState<HTMLDivElement | null>(null);
  const fit = useTitleLineFit({ row, stages, chips, refit: "" });
  report(fit);
  if (row) {
    Object.defineProperty(row, "scrollWidth", {
      configurable: true,
      value: needs - (fit.stage + fit.folded) * 100,
    });
    Object.defineProperty(row, "clientWidth", {
      configurable: true,
      value: 0,
    });
  }
  return (
    <div>
      <div ref={setRow} />
    </div>
  );
}

function fitFor(props: { stages: number; chips?: number; needs: number }) {
  let fit: TitleLineFit = { stage: -1, folded: -1 };
  render(<Probe {...props} report={(next) => (fit = next)} />);
  return fit;
}

describe("useTitleLineFit", () => {
  it("takes the page's own steps before it folds any chip", () => {
    expect(fitFor({ stages: 2, chips: 5, needs: 150 })).toEqual({
      stage: 2,
      folded: 0,
    });
  });

  it("folds chips one at a time once every step is taken, and stops when the row fits", () => {
    expect(fitFor({ stages: 2, chips: 5, needs: 350 })).toEqual({
      stage: 2,
      folded: 2,
    });
  });

  it("always leaves one chip", () => {
    expect(fitFor({ stages: 1, chips: 4, needs: 9000 })).toEqual({
      stage: 1,
      folded: 3,
    });
  });

  it("never folds where the page has no chips to fold", () => {
    expect(fitFor({ stages: 3, needs: 9000 })).toEqual({
      stage: 3,
      folded: 0,
    });
  });

  it("changes nothing when the row fits", () => {
    expect(fitFor({ stages: 2, chips: 5, needs: 0 })).toEqual({
      stage: 0,
      folded: 0,
    });
  });
});

describe("foldedChipIndexes", () => {
  it("folds nothing when nothing is asked", () => {
    expect([...foldedChipIndexes(5, 0, 0)]).toEqual([]);
  });

  it("folds from the end, one chip at a time", () => {
    expect([...foldedChipIndexes(5, 2, 0)].sort()).toEqual([3, 4]);
  });

  it("never folds the chosen chip: the next one in takes its turn", () => {
    expect([...foldedChipIndexes(5, 2, 4)].sort()).toEqual([2, 3]);
  });

  it("leaves the chosen chip when everything else has folded", () => {
    expect([...foldedChipIndexes(4, 9, 1)].sort()).toEqual([0, 2, 3]);
  });

  it("folds from the end when no chip is chosen", () => {
    expect([...foldedChipIndexes(3, 1, -1)]).toEqual([2]);
  });
});
