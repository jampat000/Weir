import { useFitLevels } from "../../lib/ui/use-fit-levels";

export type TitleLineFit = {
  /** How many of the page's own steps (a search made small, a picker moved into its card) have been taken. */
  stage: number;
  /** How many chips are folded into More: none until every step has been taken. */
  folded: number;
};

type TitleLineFitOptions = {
  /** The chips' own box on the title line, once it is on the page. The box it sits in must fill the room. */
  row: HTMLElement | null;
  /** The steps to take, in order, before any chip folds; 0 where the chips have a line of their own. */
  stages: number;
  /** How many chips fold into More: the row's own count, or 0 where its chips never fold. The last go first, and one is always left. */
  chips?: number;
  /** Anything besides the room that changes how wide the row or the controls beside it want to be. */
  refit: string;
};

/**
 * How a page's filters give way when the title line is short of room: first each step the page names, in its order,
 * then the chips one at a time into a "More" menu. Whole chips only, and nothing scrolls. It climbs while the row is
 * wider than its room and settles on the first state in which it fits (see useFitLevels).
 */
export function useTitleLineFit({
  row,
  stages,
  chips = 0,
  refit,
}: TitleLineFitOptions): TitleLineFit {
  const level = useFitLevels(row, stages + Math.max(0, chips - 1), refit);
  return {
    stage: Math.min(level, stages),
    folded: Math.max(0, level - stages),
  };
}

/**
 * The chips that fold when `folded` of them do: the last ones, in order from the end. The chosen chip stays whatever
 * its place, so the choice is always in view. `chosen` is -1 when no chip is chosen.
 */
export function foldedChipIndexes(
  count: number,
  folded: number,
  chosen: number,
): ReadonlySet<number> {
  const out = new Set<number>();
  for (let index = count - 1; index >= 0 && out.size < folded; index -= 1) {
    if (index !== chosen) out.add(index);
  }
  return out;
}
