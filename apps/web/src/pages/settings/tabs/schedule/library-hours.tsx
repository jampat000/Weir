import { useState } from "react";

import { quietActionRowClass } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  writeFromProcessingLibrary,
  type ProcessingLibrary,
} from "../../../../lib/processing/libraries-api";
import { useUpdateProcessingLibrary } from "../../../../lib/processing/libraries-queries";
import { useProcessingWatchedFolderRemuxScanDispatchEnqueueMutation } from "../../../../lib/processing/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { everyWords } from "../cleanup-tab";
import { ScheduleGridEditor } from "./schedule-grid-editor";
import {
  DAY_NAMES,
  effectiveGrid,
  weekHours,
  windowNow,
} from "./schedule-model";

/** A library's week at a glance: seven rows of 24 hours, lit where it may start work. */
function WeekStrip({ grid, name }: { grid: string; name: string }) {
  return (
    <div
      className="mm-week"
      role="img"
      aria-label={`${name}: when it may start work, Monday to Sunday`}
    >
      {weekHours(grid).map((day, d) => (
        <div className="mm-week__day" key={DAY_NAMES[d]}>
          <span className="mm-week__label">{DAY_NAMES[d].slice(0, 1)}</span>
          {day.map((on, h) => (
            <span
              key={h}
              className={on ? "mm-week__hour is-on" : "mm-week__hour"}
            />
          ))}
        </div>
      ))}
    </div>
  );
}

function RightNow({
  grid,
  zone,
  now,
}: {
  grid: string;
  zone: string;
  now: Date;
}) {
  const state = windowNow(grid, zone, now);
  switch (state.kind) {
    case "any":
      return <span>Any time</span>;
    case "never":
      return (
        <span className="mm-status-text--warning">
          Never: no hours are chosen
        </span>
      );
    case "open":
      return (
        <span>
          <span className="mm-status-text--healthy">Open</span>
          {state.until ? (
            <span className="mm-quiet-table__sub">until {state.until}</span>
          ) : null}
        </span>
      );
    case "closed":
      return (
        <span>
          <span className="mm-status-text--warning">Closed</span>
          <span className="mm-quiet-table__sub">opens {state.opens}</span>
        </span>
      );
  }
}

function ScanNowButton({
  library,
  editable,
}: {
  library: ProcessingLibrary;
  editable: boolean;
}) {
  const queueScan =
    useProcessingWatchedFolderRemuxScanDispatchEnqueueMutation();
  const watchedSet = Boolean(library.watched_folder.trim());
  return (
    <>
      <button
        type="button"
        aria-label={`Scan ${library.name} now`}
        className={mmActionButtonClass({ variant: "secondary" })}
        disabled={!editable || !watchedSet || queueScan.isPending}
        title={
          watchedSet ? undefined : "This library has no watched folder yet."
        }
        onClick={() =>
          queueScan.mutate({
            enqueue_remux_jobs: true,
            media_scope: library.media_type,
            library_id: library.id,
          })
        }
      >
        {queueScan.isPending
          ? "Starting…"
          : queueScan.isSuccess
            ? "Scan queued"
            : "Scan now"}
      </button>
      {queueScan.isError ? (
        <span className="mm-status-text--failed block text-xs" role="alert">
          {errorMessage(queueScan.error, "The scan could not be queued.")}
        </span>
      ) : null}
    </>
  );
}

/** One library's row: its week, whether it may start work now, and how often it looks. */
export function LibraryHoursRow({
  library,
  zone,
  now,
  editing,
  editable,
  onToggleEdit,
}: {
  library: ProcessingLibrary;
  zone: string;
  now: Date;
  editing: boolean;
  editable: boolean;
  onToggleEdit: () => void;
}) {
  const grid = effectiveGrid(library);
  return (
    <tr
      className={editing ? "is-selected" : undefined}
      data-testid="schedule-library-row"
    >
      <th scope="row" className="mm-quiet-table__name">
        {library.name}
        {!library.enabled ? (
          <span className="mm-quiet-table__sub">Switched off in Libraries</span>
        ) : null}
      </th>
      <td data-label="Week">
        <WeekStrip grid={grid} name={library.name} />
      </td>
      <td data-label="Right now">
        <RightNow grid={grid} zone={zone} now={now} />
      </td>
      <td data-label="Looks for new files">
        Every {everyWords(library.scan_interval_seconds)}
      </td>
      <td data-label="Actions" className="mm-schedule-actions">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          aria-expanded={editing}
          onClick={onToggleEdit}
        >
          Change hours
        </button>
        <ScanNowButton library={library} editable={editable} />
      </td>
    </tr>
  );
}

/** The hours editor for one library, under the table. Saving it is the whole of that library's schedule. */
export function LibraryHoursEditor({
  library,
  editable,
  onDone,
}: {
  library: ProcessingLibrary;
  editable: boolean;
  onDone: () => void;
}) {
  const update = useUpdateProcessingLibrary();
  const [grid, setGrid] = useState(() => effectiveGrid(library));
  const dirty = grid !== effectiveGrid(library);

  const save = () =>
    update.mutate(
      {
        id: library.id,
        data: {
          ...writeFromProcessingLibrary(library),
          // The drawn week is the schedule from now on: the older days-and-hours window is retired so the
          // two can never disagree about when this library runs.
          schedule_grid: grid,
          schedule_enabled: true,
          schedule_hours_limited: false,
        },
      },
      { onSuccess: onDone },
    );

  return (
    <div
      className="mm-schedule-editor"
      data-testid="schedule-library-editor"
      aria-label={`${library.name} hours`}
    >
      <h4 className="mm-schedule-editor__title">
        {library.name}: when it may start work
      </h4>
      <ScheduleGridEditor
        value={grid}
        onChange={setGrid}
        disabled={!editable || update.isPending}
      />
      {update.isError ? (
        <p className="mm-status-text--failed mt-2 text-sm" role="alert">
          {errorMessage(update.error, "These hours could not be saved.")}
        </p>
      ) : null}
      <div className={`${quietActionRowClass} mt-4`}>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={!editable || !dirty || update.isPending}
          data-testid="schedule-library-save"
          onClick={save}
        >
          {update.isPending ? "Saving…" : `Save ${library.name}'s hours`}
        </button>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "tertiary" })}
          onClick={onDone}
        >
          Close
        </button>
      </div>
    </div>
  );
}
