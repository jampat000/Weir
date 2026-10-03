import type { ReactNode } from "react";
import { Link } from "react-router-dom";

import { StatTile, StatUnit } from "../../../components/panels/stat-tile";
import type { Filter } from "../processing-filter";
import { useFittingRows } from "./fit-rows";
import {
  figureWords,
  lineWords,
  waitFraction,
  type NextItem,
} from "./next-model";

/** The most "then" lines the tile lists after the first thing. */
const THEN_LINES = 3;

/** What an empty tile says, for everything and for each kind of work it can be narrowed to. */
const NOTHING_SCHEDULED_WORDS: Record<Filter, string> = {
  all: "Switch on a workflow or a cleanup job.",
  download: "No new downloads are due to be looked for.",
  library: "No library cleaning is scheduled.",
};

type NextTileProps = {
  items: readonly NextItem[];
  now: number;
  paused: boolean;
  /** The kind of work the items were narrowed to, so an empty tile can say so. */
  filter?: Filter;
  /** The band keeps its three tiles across, so the tile is as tall as the band and lists only the rows that fit. */
  across?: boolean;
};

type TileParts = { figure: ReactNode; body: ReactNode };

function pausedParts(): TileParts {
  return {
    figure: (
      <>
        <span>Paused</span>
        <StatUnit>nothing new starts</StatUnit>
      </>
    ),
    body: <p className="mm-stat__idle">Running files finish first.</p>,
  };
}

function nothingScheduledParts(filter: Filter): TileParts {
  return {
    figure: <StatUnit>nothing scheduled</StatUnit>,
    body: <p className="mm-stat__idle">{NOTHING_SCHEDULED_WORDS[filter]}</p>,
  };
}

function scheduledParts(
  [first, ...rest]: readonly [NextItem, ...NextItem[]],
  now: number,
): TileParts {
  const fraction = waitFraction(first, now) ?? 0;
  return {
    figure: (
      <>
        <span data-testid="live-next-figure">{figureWords(first.at, now)}</span>
        <StatUnit>{first.label}</StatUnit>
      </>
    ),
    body: (
      <div>
        <span className="mm-next__bar" aria-hidden="true">
          <i style={{ width: `${Math.round(fraction * 100)}%` }} />
        </span>
        <ul className="mm-next__then" data-testid="live-next-then">
          {rest.slice(0, THEN_LINES).map((item) => (
            <li key={item.key} data-fit="">
              <Link to={item.to}>then {item.label}</Link>
              <span>{lineWords(item.at, now)}</span>
            </li>
          ))}
        </ul>
      </div>
    ),
  };
}

function partsOf(
  items: readonly NextItem[],
  now: number,
  paused: boolean,
  filter: Filter,
) {
  if (paused) return pausedParts();
  const [first, ...rest] = items;
  return first
    ? scheduledParts([first, ...rest], now)
    : nothingScheduledParts(filter);
}

/** The next thing Weir does on its own, with a countdown, and what comes after it. */
export function NextTile({
  items,
  now,
  paused,
  filter = "all",
  across = true,
}: NextTileProps) {
  const [bodyRef] = useFittingRows(across);
  const { figure, body } = partsOf(items, now, paused, filter);
  return (
    <StatTile label="Next" aside="on its own" figure={figure} bodyRef={bodyRef}>
      {body}
    </StatTile>
  );
}
