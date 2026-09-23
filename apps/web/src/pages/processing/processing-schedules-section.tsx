import { useEffect, useId, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { PageLoading } from "../../components/shared/page-loading";
import {
  QuietSection,
  quietActionRowClass,
} from "../../components/shared/quiet-section";
import { SettingRow } from "../../components/shared/settings-group";
import { MmListboxPicker } from "../../components/ui/mm-listbox-picker";
import {
  isHttpErrorFromApi,
  isLikelyNetworkFailure,
} from "../../lib/api/error-guards";
import { canEdit } from "../../lib/auth/can-edit";
import { useMeQuery } from "../../lib/auth/queries";
import {
  writeFromProcessingLibrary,
  type ProcessingLibrary,
} from "../../lib/processing/libraries-api";
import {
  useProcessingLibrariesQuery,
  useUpdateProcessingLibrary,
} from "../../lib/processing/libraries-queries";
import { useProcessingMaintenanceQuery } from "../../lib/processing/maintenance-queries";
import { useProcessingWatchedFolderRemuxScanDispatchEnqueueMutation } from "../../lib/processing/queries";
import {
  useAppSettingsQuery,
  useAppSettingsSaveMutation,
} from "../../lib/settings/queries";
import {
  CURATED_TIMEZONE_ID_SET,
  curatedTimezoneOptionsSorted,
} from "../../lib/settings/timezone-options";
import type { AppSettings } from "../../lib/settings/types";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../lib/ui/mm-format-date";
import { CLEANUP_JOBS, everyWords } from "./processing-maintenance-section";
import { ScheduleGridEditor } from "./schedule-grid-editor";
import {
  DAY_NAMES,
  backupWords,
  effectiveGrid,
  weekHours,
  windowNow,
  zoneClock,
} from "./schedule-model";
import { errorMessage } from "../../lib/api/error-message";

/** A minute-by-minute clock, so "open until" and "it is 14:32 there" stay true while the page is open. */
function useMinuteClock(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 30_000);
    return () => window.clearInterval(timer);
  }, []);
  return now;
}

/**
 * The time zone, here because every time on this page is read in it (canvas board 6, 23 Sep 2026). It lived under
 * System › About, a page away from the schedules it changes.
 */
function TimeZoneRow({
  settings,
  editable,
  now,
}: {
  settings: AppSettings;
  editable: boolean;
  now: Date;
}) {
  const save = useAppSettingsSaveMutation();
  const saved = CURATED_TIMEZONE_ID_SET.has(settings.app_timezone || "")
    ? settings.app_timezone
    : "";
  const [zone, setZone] = useState(saved);
  useEffect(() => setZone(saved), [saved]);
  const options = useMemo(() => curatedTimezoneOptionsSorted(), []);
  const clock = zoneClock(now, settings.app_timezone || "UTC");
  const dirty = zone !== saved && zone !== "";

  return (
    <SettingRow
      label={<span id="schedule-timezone-label">Time zone</span>}
      hint={
        <>
          It is {DAY_NAMES[clock.weekday]} {String(clock.hour).padStart(2, "0")}
          :{String(clock.minute).padStart(2, "0")} there now. Every time on this
          page, and across Weir, is in this zone.
        </>
      }
    >
      <div className="mm-schedule-zone">
        <MmListboxPicker
          ariaLabelledBy="schedule-timezone-label"
          placeholder="Select time zone"
          disabled={!editable || save.isPending}
          options={options.map((tz) => ({ value: tz.id, label: tz.label }))}
          value={zone}
          onChange={setZone}
        />
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={!editable || !dirty || save.isPending}
          data-testid="schedule-save-timezone"
          onClick={() =>
            save.mutate({
              product_display_name: settings.product_display_name,
              signed_in_home_notice: settings.signed_in_home_notice,
              setup_wizard_state: settings.setup_wizard_state,
              app_timezone: zone,
              log_retention_days: settings.log_retention_days,
            })
          }
        >
          {save.isPending ? "Saving…" : "Save"}
        </button>
      </div>
      {save.isError ? (
        <p className="mm-status-text--failed mt-2 text-sm" role="alert">
          {errorMessage(save.error, "The time zone could not be saved.")}
        </p>
      ) : null}
    </SettingRow>
  );
}

/** A library's week at a glance: seven rows of 24 hours, lit where it may start work. */
function WeekStrip({ grid, name }: { grid: string; name: string }) {
  const hours = weekHours(grid);
  return (
    <div
      className="mm-week"
      role="img"
      aria-label={`${name}: when it may start work, Monday to Sunday`}
    >
      {hours.map((day, d) => (
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

/** The hours editor for one library, under the table. Saving it is the whole of that library's schedule. */
function LibraryHoursEditor({
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
          onClick={() =>
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
            )
          }
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

/**
 * Settings › Schedule (canvas board 6, 23 Sep 2026): the time zone, a week per library with its hours editor below,
 * and when Weir's own jobs run. The two Movies/TV "watched-folder windows" that were here are gone: they were saved
 * and never read, so they changed nothing.
 */
export function ProcessingSchedulesSection() {
  const me = useMeQuery();
  const settings = useAppSettingsQuery();
  const libraries = useProcessingLibrariesQuery();
  const maintenance = useProcessingMaintenanceQuery();
  const formatDate = useAppDateFormatter();
  const now = useMinuteClock();
  const ids = useId();
  const [editing, setEditing] = useState<number | null>(null);
  const editable = canEdit(me.data?.role);

  if (settings.isPending || libraries.isPending || me.isPending) {
    return <PageLoading label="Loading schedules" />;
  }
  if (settings.isError || libraries.isError) {
    const error = settings.error ?? libraries.error;
    return (
      <ul className="mm-interrupt" role="alert">
        <li className="mm-interrupt__item">
          <span className="mm-interrupt__text">
            <strong className="font-semibold">Could not load schedules.</strong>{" "}
            {isLikelyNetworkFailure(error)
              ? "Check that the Weir API is running."
              : isHttpErrorFromApi(error)
                ? "Sign in, then try again."
                : "Request failed."}
          </span>
        </li>
      </ul>
    );
  }
  if (!settings.data || !libraries.data) return null;

  const zone = settings.data.app_timezone || "UTC";
  const ordered = [...libraries.data].sort(
    (x, y) => x.display_order - y.display_order,
  );
  const editingLibrary = ordered.find((l) => l.id === editing) ?? null;
  const families = maintenance.data?.families ?? [];
  const suite = settings.data;

  return (
    <div className="mm-quiet-stack" data-testid="processing-schedules-section">
      <TimeZoneRow settings={suite} editable={editable} now={now} />

      <QuietSection headingId={`${ids}-libraries`} heading="Libraries">
        <p className="mm-quiet-note">
          When each library may start work. A file already being processed when
          its hours end is finished, not stopped.
        </p>
        {ordered.length === 0 ? (
          <p className="mt-4 text-sm text-[var(--mm-text3)]">
            Add a library under Libraries to schedule it here.
          </p>
        ) : (
          <div className="mm-quiet-table-wrap mt-4">
            <table className="mm-quiet-table mm-schedule-table">
              <thead>
                <tr>
                  <th scope="col">Library</th>
                  <th scope="col">Week (Mon to Sun, midnight to midnight)</th>
                  <th scope="col">Right now</th>
                  <th scope="col">Looks for new files</th>
                  <th scope="col">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {ordered.map((library) => {
                  const grid = effectiveGrid(library);
                  return (
                    <tr
                      key={library.id}
                      className={
                        editing === library.id ? "is-selected" : undefined
                      }
                      data-testid="schedule-library-row"
                    >
                      <th scope="row" className="mm-quiet-table__name">
                        {library.name}
                        {!library.enabled ? (
                          <span className="mm-quiet-table__sub">
                            Switched off in Libraries
                          </span>
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
                          className={mmActionButtonClass({
                            variant: "secondary",
                          })}
                          aria-expanded={editing === library.id}
                          onClick={() =>
                            setEditing(
                              editing === library.id ? null : library.id,
                            )
                          }
                        >
                          Change hours
                        </button>
                        <ScanNowButton library={library} editable={editable} />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        {editingLibrary ? (
          <LibraryHoursEditor
            key={editingLibrary.id}
            library={editingLibrary}
            editable={editable}
            onDone={() => setEditing(null)}
          />
        ) : null}
      </QuietSection>

      <QuietSection headingId={`${ids}-timers`} heading="Weir's own timers">
        <p className="mm-quiet-note">
          These run on their own clocks, whatever the library hours say.
        </p>
        <div className="mm-quiet-table-wrap mt-4">
          <table className="mm-quiet-table" data-testid="schedule-timers">
            <thead>
              <tr>
                <th scope="col">Job</th>
                <th scope="col">When</th>
                <th scope="col">Last run</th>
                <th scope="col">Next run</th>
              </tr>
            </thead>
            <tbody>
              {CLEANUP_JOBS.map((job) => {
                const state = families.find((f) => f.family === job.family);
                return (
                  <tr key={job.family}>
                    <th scope="row" className="mm-quiet-table__name">
                      {job.name}
                      <Link
                        className="mm-quiet-table__sub mm-schedule-link"
                        to="/settings?tab=cleanup"
                      >
                        Change in Cleanup
                      </Link>
                    </th>
                    <td data-label="When">
                      {!state
                        ? "—"
                        : !state.enabled
                          ? "Off"
                          : state.interval_seconds
                            ? `Every ${everyWords(state.interval_seconds)}`
                            : "On"}
                    </td>
                    <td data-label="Last run">
                      {state?.last_completed_at
                        ? formatDate(state.last_completed_at)
                        : "Not yet"}
                    </td>
                    <td data-label="Next run">
                      {state?.enabled && state.next_run_at
                        ? formatDate(state.next_run_at)
                        : "—"}
                    </td>
                  </tr>
                );
              })}
              <tr>
                <th scope="row" className="mm-quiet-table__name">
                  Settings backup
                  <Link
                    className="mm-quiet-table__sub mm-schedule-link"
                    to="/system?tab=backups"
                  >
                    Change in Backups
                  </Link>
                </th>
                <td data-label="When">
                  {backupWords(
                    Boolean(suite.configuration_backup_enabled),
                    Number(suite.configuration_backup_interval_hours ?? 24),
                    suite.configuration_backup_preferred_time ?? "02:00",
                  )}
                </td>
                <td data-label="Last run">
                  {suite.configuration_backup_last_run_at
                    ? formatDate(suite.configuration_backup_last_run_at)
                    : "Not yet"}
                </td>
                <td data-label="Next run">—</td>
              </tr>
            </tbody>
          </table>
        </div>
      </QuietSection>
    </div>
  );
}
