import { useState } from "react";

import { Panel } from "../../../../components/panels/panel";
import { SegmentedControl } from "../../../../components/panels/segmented-control";
import { classNames } from "../../../../lib/ui/class-names";
import { useAppClockSecondsFormatter } from "../../../../lib/ui/mm-format-date";
import { useNow } from "../../../../lib/ui/use-now";
import { useFittingRows } from "../fit-rows";
import {
  LOG_FILTERS,
  defaultFilter,
  emptyLogWords,
  isFresh,
  logSummaryWords,
  shownLines,
  type LogFilter,
  type LogLine,
} from "./log-card-model";
import { FitText } from "../../../../lib/ui/fit-text";
import { MoreCount } from "./more-count";
import { SERVER_LOG_PATH } from "./system-paths";
import { useSystemLog } from "./use-system-log";

/** Checked once a second, so a line stops pulsing when its moment is over. */
const TICK_MS = 1000;
/** The most lines drawn: the card's height decides how many whole ones show, and this bounds what is measured. */
const MOST_LINES_DRAWN = 60;

const LEVEL_WORDS: Record<LogLine["level"], string> = {
  error: "Error",
  warning: "Warning",
  info: "Info",
};

function LogRow({
  line,
  clock,
  fresh,
}: {
  line: LogLine;
  clock: (ms: number) => string;
  fresh: boolean;
}) {
  return (
    <li
      className={classNames(
        "mm-sy-log",
        `mm-sy-log--${line.level}`,
        fresh && "mm-sy-log--new",
      )}
      data-fit=""
      data-testid="system-log-line"
    >
      <time
        className="mm-sy-log__time"
        dateTime={new Date(line.at).toISOString()}
      >
        {clock(line.at)}
      </time>
      <span className={`mm-sy-log__level mm-sy-log__level--${line.level}`}>
        {LEVEL_WORDS[line.level]}
      </span>
      <span className="mm-sy-log__message" title={line.message}>
        {line.message}
      </span>
    </li>
  );
}

/**
 * Dashboard › System: what Weir has written to its log, newest first, with today's errors and warnings counted and a
 * switch between everything, errors and warnings. A warning or error that is written while the page is open arrives
 * at the top and pulses once. The card's height decides how many whole lines show.
 */
export function LogCard() {
  const log = useSystemLog();
  const clock = useAppClockSecondsFormatter();
  const now = useNow(TICK_MS);
  const [picked, setPicked] = useState<LogFilter | null>(null);
  const filter = picked ?? defaultFilter(log.counts);
  const [listRef, fits] = useFittingRows();
  const shown = shownLines(log.lines, filter);
  const drawn = shown.slice(0, MOST_LINES_DRAWN);
  const more = shown.length - Math.min(fits, drawn.length);
  return (
    <Panel
      title="Log"
      count={
        log.loading ? (
          ""
        ) : (
          <FitText
            words={logSummaryWords(log.counts)}
            className="mm-sy-count"
          />
        )
      }
      aside={
        <>
          <MoreCount count={more} />
          <SegmentedControl
            ariaLabel="Show"
            options={LOG_FILTERS}
            value={filter}
            onChange={setPicked}
          />
        </>
      }
      to={SERVER_LOG_PATH}
      toLabel="Full log"
      dataTestId="system-log"
      className="mm-sy-card mm-sy-card--log"
    >
      <div ref={listRef} className="mm-sy-fit">
        {log.failed ? (
          <p className="mm-sy-note">
            Weir could not read its log. It tries again by itself.
          </p>
        ) : null}
        {!log.loading && !log.failed && shown.length === 0 ? (
          <p className="mm-sy-note">{emptyLogWords(filter)}</p>
        ) : null}
        <ul className="mm-sy-list">
          {drawn.map((line) => (
            <LogRow
              key={line.key}
              line={line}
              clock={clock}
              fresh={isFresh(line, now)}
            />
          ))}
        </ul>
      </div>
    </Panel>
  );
}
