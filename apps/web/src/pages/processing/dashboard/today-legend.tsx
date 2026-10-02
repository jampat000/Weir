import type { HandedBack } from "../handed-back-model";
import { FitText } from "./system/fit-words";
import { TONES, legendWords } from "./handed-back-words";

type TodayLegendProps = {
  /** What finished in the last two hours, by how each file turned out. */
  totals: HandedBack["totals"];
};

/**
 * The split of the last two hours under the chart, in the colours Just finished uses for its dots: how many were
 * cleaned, were already clean and need a look. A tone with nothing in it is left out. Each count gets an equal share
 * of the row and says as much of its words as the share holds, down to the bare number.
 */
export function TodayLegend({ totals }: TodayLegendProps) {
  const shown = TONES.filter((tone) => totals[tone] > 0);
  if (shown.length === 0) return null;
  return (
    <div
      aria-hidden="true"
      className="cs-legend"
      data-testid="today-legend"
      style={{ gridTemplateColumns: `repeat(${shown.length}, minmax(0, 1fr))` }}
    >
      {shown.map((tone) => (
        <span key={tone} className="cs-legend__part">
          <i className={`cs-legend__dot cs-legend__dot--${tone}`} />
          <FitText
            className="cs-legend__words"
            words={legendWords(tone, totals[tone])}
          />
        </span>
      ))}
    </div>
  );
}
