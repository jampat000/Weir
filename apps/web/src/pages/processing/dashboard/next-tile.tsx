import type { ReactNode } from "react";
import { Link } from "react-router-dom";

import { StatTile, StatUnit } from "../../../components/panels/stat-tile";
import {
  figureWords,
  lineWords,
  waitFraction,
  type NextItem,
} from "./next-model";

/** The most "then" lines the tile lists after the first thing. */
const THEN_LINES = 3;

type NextTileProps = {
  items: readonly NextItem[];
  now: number;
  paused: boolean;
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
    body: (
      <p className="mm-stat__idle">
        Files already being written finish; nothing new starts until you resume.
      </p>
    ),
  };
}

function nothingScheduledParts(): TileParts {
  return {
    figure: <StatUnit>nothing is scheduled</StatUnit>,
    body: (
      <p className="mm-stat__idle">
        Nothing is waiting on a timer. Turn on a workflow or a cleanup job to
        see it here.
      </p>
    ),
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
            <li key={item.key}>
              <Link to={item.to}>then {item.label}</Link>
              <span>{lineWords(item.at, now)}</span>
            </li>
          ))}
        </ul>
      </div>
    ),
  };
}

function partsOf(items: readonly NextItem[], now: number, paused: boolean) {
  if (paused) return pausedParts();
  const [first, ...rest] = items;
  return first
    ? scheduledParts([first, ...rest], now)
    : nothingScheduledParts();
}

/** The next thing Weir does on its own, with a countdown, and what comes after it. */
export function NextTile({ items, now, paused }: NextTileProps) {
  const { figure, body } = partsOf(items, now, paused);
  return (
    <StatTile label="Next" aside="on its own" figure={figure}>
      {body}
    </StatTile>
  );
}
