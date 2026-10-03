import { describe, expect, it } from "vitest";

import { fitPageTabs } from "./page-tabs-fit";

// Six tabs of 80px with 18px between them take 6 * 80 + 5 * 18 = 570px. More is 60px wide.
const SIX_TABS = [80, 80, 80, 80, 80, 80];
const MORE = 60;

describe("fitPageTabs", () => {
  it("shows every tab, and no More, when they all fit", () => {
    expect(fitPageTabs(SIX_TABS, 570, MORE, 0)).toEqual({
      visible: [0, 1, 2, 3, 4, 5],
      folded: [],
    });
  });

  it("folds the tabs from the end that do not fit beside More and its gap", () => {
    // 569px: More and its gap take 78, leaving 491. Five tabs need 5 * 80 + 4 * 18 = 472; six do not fit at all.
    expect(fitPageTabs(SIX_TABS, 569, MORE, 0)).toEqual({
      visible: [0, 1, 2, 3, 4],
      folded: [5],
    });
    // 300px leaves 222: two tabs need 178 and three need 276.
    expect(fitPageTabs(SIX_TABS, 300, MORE, 1)).toEqual({
      visible: [0, 1],
      folded: [2, 3, 4, 5],
    });
  });

  it("measures each tab by its own width", () => {
    const uneven = [40, 200, 40];
    expect(fitPageTabs(uneven, 316, 50, 0).folded).toEqual([]);
    // 200px leaves 132 beside More: the first tab fits, and the second does not.
    expect(fitPageTabs(uneven, 200, 50, 0)).toEqual({
      visible: [0],
      folded: [1, 2],
    });
  });

  it("gives a chosen tab that would fold the last visible place, and folds the tab it displaced", () => {
    const fit = fitPageTabs(SIX_TABS, 300, MORE, 5);

    expect(fit.visible).toEqual([0, 5]);
    expect(fit.folded).toEqual([1, 2, 3, 4]);
  });

  it("keeps the chosen tab when only it fits", () => {
    const fit = fitPageTabs(SIX_TABS, 150, MORE, 3);

    expect(fit.visible).toEqual([3]);
    expect(fit.folded).toEqual([0, 1, 2, 4, 5]);
  });

  it("folds everything when no tab is chosen and none fits", () => {
    expect(fitPageTabs(SIX_TABS, 100, MORE, -1)).toEqual({
      visible: [],
      folded: [0, 1, 2, 3, 4, 5],
    });
  });

  it("follows the room as the window is resized", () => {
    const shown = (room: number) =>
      fitPageTabs(SIX_TABS, room, MORE, 0).visible.length;

    expect([700, 570, 480, 300, 200].map(shown)).toEqual([6, 6, 4, 2, 1]);
    expect([200, 300, 480, 570, 700].map(shown)).toEqual([1, 2, 4, 6, 6]);
  });
});
