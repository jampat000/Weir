import { Fragment, useMemo, type CSSProperties, type ReactNode } from "react";

import { Panel } from "../../../../components/panels/panel";
import { ColumnsMenu } from "../../../../components/shared/columns-menu";
import { SortableColumnHeader } from "../../../../components/shared/sortable-column-header";
import { StatusDot } from "../../../../components/panels/status-dot";
import { useSystemTasksQuery } from "../../../../lib/system/system-tasks";
import { FitText, nameWords } from "../../../../lib/ui/fit-text";
import { sortRows } from "../../../../lib/ui/table-columns";
import { useNow } from "../../../../lib/ui/use-now";
import {
  useTableColumns,
  type TableColumns,
} from "../../../../lib/ui/use-table-columns";
import { useFittingRows } from "../fit-rows";
import { MoreCount } from "./more-count";
import {
  TASK_COLUMNS,
  TASK_SORT_VALUES,
  taskGrid,
  type TaskColumnId,
} from "./tasks-columns";
import { JOBS_PATH } from "./system-paths";
import {
  lastResultWords,
  nextWords,
  TASK_MEANING,
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

function TaskLine({
  row,
  now,
  order,
}: {
  row: TaskRow;
  now: number;
  order: readonly TaskColumnId[];
}) {
  const mark = RESULT_MARKS[row.state];
  const cells: Record<TaskColumnId, ReactNode> = {
    state: (
      <span role="cell">
        <StatusDot meaning={TASK_MEANING[row.state]} />
      </span>
    ),
    task: (
      <span role="rowheader" data-col="task" className="mm-sy-task__name">
        <b>
          <FitText
            className="block"
            words={nameWords(row.label)}
            title={row.label}
          />
        </b>
        {row.why ? (
          <span className="mm-sy-task__why mm-status-text">{row.why}</span>
        ) : null}
      </span>
    ),
    result: (
      <span role="cell" data-col="result" className="mm-sy-task__result">
        {row.state === "running" ? (
          <b className="mm-status-text">running</b>
        ) : (
          <>
            {mark ? (
              <b
                className="mm-status-text"
                aria-label={row.state === "ok" ? "Worked" : "Failed"}
              >
                {mark}
              </b>
            ) : null}{" "}
            {lastResultWords(row, now)}
          </>
        )}
      </span>
    ),
    next: (
      <span role="cell" data-col="next" className="mm-sy-task__next">
        <b>{nextWords(row, now)}</b>
      </span>
    ),
  };
  return (
    <li
      role="row"
      className="mm-sy-task"
      data-status={TASK_MEANING[row.state]}
      title={row.why ? `${row.label}: ${row.why}` : row.label}
      data-fit=""
      data-testid="system-task"
    >
      {order.map((id) => (
        <Fragment key={id}>{cells[id]}</Fragment>
      ))}
    </li>
  );
}

/** The header row: a blank over the dot, and a heading over each of the other columns, which sorts and moves them. */
function TaskHeadings({ columns }: { columns: TableColumns<TaskColumnId> }) {
  return (
    <div role="row" className="mm-sy-task mm-sy-task--head">
      {columns.order.map((id) =>
        id === "state" ? (
          <span key={id} role="presentation" />
        ) : (
          <SortableColumnHeader
            key={id}
            as="div"
            heading={columns.heading(id)}
          />
        ),
      )}
    </div>
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
  const columns = useTableColumns(TASK_COLUMNS);
  const rows = useMemo(
    () => sortRows(taskRows(tasks.data ?? []), columns.sort, TASK_SORT_VALUES),
    [tasks.data, columns.sort],
  );
  const [listRef, fits] = useFittingRows();
  const more = rows.length - Math.min(fits, rows.length);
  return (
    <Panel
      title="Scheduled tasks"
      count={tasksSummary(rows)}
      aside={
        <>
          <MoreCount count={more} />
          <ColumnsMenu table={columns} />
        </>
      }
      to={JOBS_PATH}
      toLabel="Jobs"
      dataTestId="system-tasks"
      className="mm-sy-card mm-sy-card--tasks"
    >
      <div
        ref={listRef}
        className="mm-sy-fit"
        style={taskGrid(columns.order) as CSSProperties}
        {...columns.tableProps}
      >
        {tasks.isError && rows.length === 0 ? (
          <p className="mm-sy-note">
            Weir could not read its scheduled tasks. It tries again by itself.
          </p>
        ) : null}
        {tasks.isSuccess && rows.length === 0 ? (
          <p className="mm-sy-note">No scheduled tasks yet.</p>
        ) : null}
        {rows.length > 0 ? (
          <div role="table" aria-label="Scheduled tasks">
            <TaskHeadings columns={columns} />
            <ul className="mm-sy-list" role="rowgroup">
              {rows.map((row) => (
                <TaskLine
                  key={row.key}
                  row={row}
                  now={now}
                  order={columns.order}
                />
              ))}
            </ul>
          </div>
        ) : null}
      </div>
    </Panel>
  );
}
