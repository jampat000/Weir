/** The gap between two tabs, and between the last tab and More, in px. `column-gap` on `.mm-page-tabs` in weir-header.css is the same 18px. */
export const PAGE_TAB_GAP = 18;

export type PageTabsFit = {
  /** Indexes shown in the row, in the order they are drawn. */
  visible: number[];
  /** Indexes folded into the More menu, in their own order. */
  folded: number[];
};

function indexesFrom(start: number, count: number): number[] {
  return Array.from({ length: count }, (_, offset) => start + offset);
}

/** How many tabs from the front fit in `available` px. */
function tabsFromFrontThatFit(
  widths: readonly number[],
  available: number,
): number {
  let count = 0;
  let used = 0;
  while (count < widths.length) {
    const next = used + widths[count] + (count > 0 ? PAGE_TAB_GAP : 0);
    if (next > available) break;
    used = next;
    count += 1;
  }
  return count;
}

/**
 * Which tabs show in `room` px and which fold into "More", given each tab's own width and More's.
 *
 * Everything shows when it all fits. Otherwise More takes its own width plus one gap, the tabs that fit in what is
 * left show from the front, and the rest fold. The chosen tab never folds: when it would, it takes the last visible
 * place and the tab it displaces folds instead. `selectedIndex` is -1 when no tab is chosen.
 */
export function fitPageTabs(
  widths: readonly number[],
  room: number,
  moreWidth: number,
  selectedIndex: number,
): PageTabsFit {
  const count = widths.length;
  const all = widths.reduce((sum, width) => sum + width, 0);
  if (all + PAGE_TAB_GAP * Math.max(0, count - 1) <= room) {
    return { visible: indexesFrom(0, count), folded: [] };
  }

  const available = room - moreWidth - PAGE_TAB_GAP;
  const fromFront = tabsFromFrontThatFit(widths, available);
  const selectedFolds = selectedIndex >= fromFront && selectedIndex < count;
  if (!selectedFolds) {
    return {
      visible: indexesFrom(0, fromFront),
      folded: indexesFrom(fromFront, count - fromFront),
    };
  }

  // The chosen tab sits past the cut: the tabs before it keep what is left once it has its place.
  const beforeIt = tabsFromFrontThatFit(
    widths.slice(0, selectedIndex),
    available - widths[selectedIndex] - PAGE_TAB_GAP,
  );
  const visible = [...indexesFrom(0, beforeIt), selectedIndex];
  return {
    visible,
    folded: indexesFrom(0, count).filter((index) => !visible.includes(index)),
  };
}
