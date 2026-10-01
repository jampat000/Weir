/** The multiples of a power of ten a chart's top may be: 1, 2, 2.5, 5, 10 times 10^n. */
const ROUND_STEPS = [1, 2, 2.5, 5, 10];

/** The top of a chart's scale when nothing has been measured, so an empty chart still has a scale. */
const EMPTY_SCALE_TOP = 1;

/**
 * The top of a chart's scale: the next of 1, 2, 2.5, 5 or 10 times a power of ten at or above the highest
 * value shown, so the gridlines read as round numbers.
 */
export function roundScaleTop(highest: number): number {
  if (!(highest > 0) || !Number.isFinite(highest)) return EMPTY_SCALE_TOP;
  const decade = 10 ** Math.floor(Math.log10(highest));
  const step = ROUND_STEPS.find((candidate) => candidate * decade >= highest);
  return (step ?? 10) * decade;
}
