/** The multiples of a power of ten a chart's top may be: 1, 2, 2.5, 5, 10 times 10^n. */
const ROUND_STEPS = [1, 2, 2.5, 5, 10];

/** The top of a chart's scale when nothing has been measured, so an empty chart still has a scale. */
const EMPTY_SCALE_TOP = 1;

/** The lowest top of a chart that counts whole things, so its half line is a whole number too. */
const LOWEST_WHOLE_TOP = 2;

export type RoundScaleOptions = {
  /**
   * For a chart that counts whole things: the top and its half are both whole numbers, never lower than 2
   * ("2 files" and "1 file"), so no gridline reads "0.5 files".
   */
  wholeNumbers?: boolean;
};

function tops(highest: number, wholeNumbers: boolean): number[] {
  const decade = 10 ** Math.floor(Math.log10(highest));
  return ROUND_STEPS.map((step) => step * decade).filter(
    (top) => !wholeNumbers || Number.isInteger(top / 2),
  );
}

/**
 * The top of a chart's scale: the next of 1, 2, 2.5, 5 or 10 times a power of ten at or above the highest
 * value shown, so the gridlines read as round numbers.
 */
export function roundScaleTop(
  highest: number,
  { wholeNumbers = false }: RoundScaleOptions = {},
): number {
  const measured = highest > 0 && Number.isFinite(highest) ? highest : 0;
  if (wholeNumbers) {
    const shown = Math.max(measured, LOWEST_WHOLE_TOP);
    return tops(shown, true).find((top) => top >= shown) ?? shown;
  }
  if (measured === 0) return EMPTY_SCALE_TOP;
  return tops(measured, false).find((top) => top >= measured) ?? measured;
}
