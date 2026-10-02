import { useMemo } from "react";

import { Panel } from "../../../../components/panels/panel";
import { useSystemTasksQuery } from "../../../../lib/system/system-tasks";
import { classNames } from "../../../../lib/ui/class-names";
import { useNow } from "../../../../lib/ui/use-now";
import { useFittingRows } from "../fit-rows";
import { MoreCount } from "./more-count";
import { JOBS_PATH } from "./system-paths";
import {
  lastResultWords,
  nextWords,
  taskRows,
  tasksSummary,
  type TaskRow,
} from "./tasks-card-model";

/** Once a second, so a countdown moves between the server's updates. */
const TICK_MS = 1000;

const RESULT_MARKS: Record<TaskRow["state"], string> = {
  running: "",
  ok: "✓",
  failed: "✗",
  never: "",
};

function TaskLine({ row, now }: { row: TaskRow; now: number }) {
  const mark = RESULT_MARKS[row.state];
  return (
    <li
      className={classNames("mm-sy-task", `mm-sy-task--${row.state}`)}
      title={row.why ? `${row.label}: ${row.why}` : row.label}
      data-fit=""
      data-testid="system-task"
    >
      <span className="mm-sy-task__dot" aria-hidden="true" />
      <span className="mm-sy-task__name">
        <b>{row.label}</b>
        {row.why ? <span className="mm-sy-task__why">{row.why}</span> : null}
      </span>
      <span className="mm-sy-task__result">
        {row.state === "running" ? (
          <b className="mm-sy-task__running">running</b>
        ) : (
          <>
            {mark ? (
              <b
                className="mm-sy-task__mark"
                aria-label={row.state === "ok" ? "Worked" : "Failed"}
              >
                {mark}
              </b>
            ) : null}{" "}
            {lastResultWords(row, now)}
          </>
        )}
      </span>
      <span className="mm-sy-task__next">
        <b>{nextWords(row, now)}</b>
      </span>
    </li>
  );
}

/**
 * Dashboard › System: every task Weir runs on its own, running ones first and then the one due soonest. Each shows how
 * its last run went (a tick, or a red cross with the reason) and a countdown to the next, and a running task blinks
 * blue. The card's height decides how many whole rows show; the header says how many more there are, and links to
 * the jobs Weir has run.
 */
export function TasksCard() {
  const tasks = useSystemTasksQuery();
  const now = useNow(TICK_MS);
  const rows = useMemo(() => taskRows(tasks.data ?? []), [tasks.data]);
  const [listRef, fits] = useFittingRows();
  const more = rows.length - Math.min(fits, rows.length);
  return (
    <Panel
      title="Scheduled tasks"
      count={tasksSummary(rows)}
      aside={<MoreCount count={more} />}
      to={JOBS_PATH}
      toLabel="Jobs"
      dataTestId="system-tasks"
      className="mm-sy-card"
    >
      <div className="mm-sy-task mm-sy-task--head" aria-hidden="true">
        <span />
        <span>Task</span>
        <span>Last result</span>
        <span>Next</span>
      </div>
      <div ref={listRef} className="mm-sy-fit">
        {tasks.isError && rows.length === 0 ? (
          <p className="mm-sy-note">
            Weir could not read its scheduled tasks. It tries again by itself.
          </p>
        ) : null}
        {tasks.isSuccess && rows.length === 0 ? (
          <p className="mm-sy-note">No scheduled tasks yet.</p>
        ) : null}
        <ul className="mm-sy-list">
          {rows.map((row) => (
            <TaskLine key={row.key} row={row} now={now} />
          ))}
        </ul>
      </div>
    </Panel>
  );
}
