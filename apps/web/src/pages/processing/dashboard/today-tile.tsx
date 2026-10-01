import { useState } from "react";

import {
  StatSide,
  StatTile,
  StatUnit,
} from "../../../components/panels/stat-tile";
import { formatBytes } from "../../../lib/format/bytes";
import type { Filter } from "../processing-filter";
import { useHandedBack } from "../use-handed-back";
import { chartCaption, handedBackSentence } from "./handed-back-words";
import { showNeedsPanel } from "./show-needs-panel";
import { TodayChart } from "./today-chart";
import { useNeedsYou } from "./use-needs-you";
import { useTodayFigures } from "./use-today-figures";

const PENDING = "…";

type TodayTileProps = {
  filter: Filter;
  now: number;
  /** Narrows every figure to one workflow; every workflow when null or left out. */
  workflowId?: number | null;
};

/**
 * What Weir has done today: how many files it cleaned and how much space that saved, with the last two
 * hours drawn under it. The aside takes the person to the Needs you panel, when anything is in it.
 */
export function TodayTile({ filter, now, workflowId = null }: TodayTileProps) {
  const figures = useTodayFigures(workflowId, now);
  const { count: needsALook } = useNeedsYou(workflowId);
  const { handed, total, partial } = useHandedBack(filter, now, workflowId);
  const [pointed, setPointed] = useState<number | null>(null);
  return (
    <StatTile
      label="Today"
      aside={
        needsALook > 0 ? (
          <button type="button" onClick={showNeedsPanel}>
            {needsALook.toLocaleString()} need a look →
          </button>
        ) : undefined
      }
      figure={
        <>
          <span data-testid="live-done-today">
            {figures ? figures.cleaned.toLocaleString() : PENDING}
          </span>
          <StatUnit>cleaned</StatUnit>
          {figures ? (
            <StatSide>
              {formatBytes(figures.savedBytes) || "0 B"} saved
            </StatSide>
          ) : null}
        </>
      }
    >
      <div className="mm-today" data-testid="live-handed-back">
        <TodayChart
          handed={handed}
          pointed={pointed}
          now={now}
          onPoint={setPointed}
        />
        <p className="mm-today__caption" aria-hidden="true">
          {chartCaption(handed, pointed, now)}
        </p>
        <span className="sr-only" data-testid="live-handed-back-sum">
          {handedBackSentence(handed, total, partial)}
        </span>
      </div>
    </StatTile>
  );
}
