import { useId, useState } from "react";

import { MmOnOffSwitch } from "../../components/ui/mm-on-off-switch";
import { useMeQuery } from "../../lib/auth/queries";
import type {
  MaintenanceFamily,
  MaintenanceFamilyState,
} from "../../lib/processing/maintenance-api";
import {
  processingMaintenanceKey,
  useProcessingMaintenanceQuery,
  useRunProcessingMaintenance,
} from "../../lib/processing/maintenance-queries";
import {
  useProcessingOperatorSettingsQuery,
  useProcessingOperatorSettingsSaveMutation,
} from "../../lib/processing/queries";
import type { ProcessingOperatorSettingsPutBody } from "../../lib/processing/types";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../lib/ui/mm-format-date";
import { useQueryClient } from "@tanstack/react-query";

function canEdit(role: string | undefined): boolean {
  return role === "admin" || role === "operator";
}

/** The two cleanup jobs Weir times, in words a person uses, with the setting each one's switch and timer save to. */
export const CLEANUP_JOBS: {
  family: MaintenanceFamily;
  name: string;
  enabledField: "work_temp_stale_sweep_enabled" | "failure_cleanup_enabled";
  intervalField:
    | "work_temp_stale_sweep_interval_seconds"
    | "failure_cleanup_interval_seconds";
  /** It deletes something a person cannot get back, so its description is painted in the warning colour. */
  destructive: boolean;
}[] = [
  {
    family: "work_temp_stale_sweep",
    name: "Leftover work files",
    enabledField: "work_temp_stale_sweep_enabled",
    intervalField: "work_temp_stale_sweep_interval_seconds",
    destructive: false,
  },
  {
    family: "failure_cleanup",
    name: "Downloads of failed files",
    enabledField: "failure_cleanup_enabled",
    intervalField: "failure_cleanup_interval_seconds",
    destructive: true,
  },
];

const EVERY: { seconds: number; label: string }[] = [
  { seconds: 900, label: "15 minutes" },
  { seconds: 1800, label: "30 minutes" },
  { seconds: 3600, label: "hour" },
  { seconds: 6 * 3600, label: "6 hours" },
  { seconds: 12 * 3600, label: "12 hours" },
  { seconds: 86400, label: "day" },
  { seconds: 7 * 86400, label: "7 days" },
];

/** "15 minutes", "hour", "3 days": the interval after the word "Every". */
export function everyWords(seconds: number): string {
  const known = EVERY.find((e) => e.seconds === seconds);
  if (known) return known.label;
  if (seconds % 86400 === 0) return `${seconds / 86400} days`;
  if (seconds % 3600 === 0) return `${seconds / 3600} hours`;
  return `${Math.round(seconds / 60)} minutes`;
}

function lastRunLine(
  job: MaintenanceFamilyState,
  formatDate: (iso: string) => string,
): string {
  if (job.running > 0) return "Running now.";
  if (job.pending > 0) return "Queued, waiting for a free worker.";
  if (job.last_failed_at)
    return `Failed ${formatDate(job.last_failed_at)}: ${job.last_error ?? "no reason recorded"}.`;
  if (job.last_completed_at) return formatDate(job.last_completed_at);
  return "Not yet.";
}

/**
 * Settings › Cleanup (was Housekeeping; canvas board 8, 23 Sep 2026): the small jobs that keep Weir's folders and
 * records tidy, each with its own switch and timer. A change applies within half a minute, with no restart: Weir's
 * timers read the switch and the interval again every 30 seconds.
 */
export function ProcessingMaintenanceSection() {
  const formatDate = useAppDateFormatter();
  const me = useMeQuery();
  const maintenance = useProcessingMaintenanceQuery();
  const settings = useProcessingOperatorSettingsQuery();
  const save = useProcessingOperatorSettingsSaveMutation();
  const run = useRunProcessingMaintenance();
  const queryClient = useQueryClient();
  const ids = useId();
  const [notice, setNotice] = useState<string | null>(null);
  const [retention, setRetention] = useState<string | null>(null);

  const editable = canEdit(me.data?.role);
  const families = maintenance.data?.families ?? [];

  async function change(body: ProcessingOperatorSettingsPutBody, said: string) {
    setNotice(null);
    try {
      await save.mutateAsync(body);
      void queryClient.invalidateQueries({
        queryKey: processingMaintenanceKey(),
      });
      setNotice(said);
    } catch {
      setNotice("That change could not be saved. Refresh and try again.");
    }
  }

  async function runNow(family: MaintenanceFamily, name: string) {
    setNotice(null);
    try {
      // One job per kind of library: both, so "Run now" means the whole job.
      const movies = await run.mutateAsync({ family, mediaScope: "movie" });
      const tv = await run.mutateAsync({ family, mediaScope: "tv" });
      setNotice(
        movies.queued || tv.queued
          ? `${name}: started. Its result shows under Last run.`
          : movies.detail,
      );
    } catch {
      setNotice(`${name} could not be started.`);
    }
  }

  const retentionDays =
    retention ?? String(settings.data?.file_log_retention_days ?? "");
  const retentionValue = Number.parseInt(retentionDays, 10);
  const retentionDirty =
    settings.data !== undefined &&
    retention !== null &&
    retentionValue !== settings.data.file_log_retention_days;

  return (
    <div
      className="mm-quiet-stack"
      data-testid="processing-maintenance-section"
    >
      <p className="mm-quiet-note">
        Small jobs that keep Weir&rsquo;s folders and records tidy. Each runs on
        its own timer, and a change applies within half a minute, with no
        restart.
      </p>

      {notice ? (
        <p
          className="text-sm font-medium text-[var(--mm-text1)]"
          role="status"
          data-testid="processing-maintenance-notice"
        >
          {notice}
        </p>
      ) : null}

      {families.length === 0 && !maintenance.isLoading ? (
        <p className="mm-quiet-note">
          No cleanup jobs are available on this instance.
        </p>
      ) : (
        <div className="mm-quiet-table-wrap">
          <table className="mm-quiet-table mm-cleanup-table">
            <thead>
              <tr>
                <th scope="col">Job</th>
                <th scope="col">On</th>
                <th scope="col">Every</th>
                <th scope="col">Last run</th>
                <th scope="col">Next run</th>
                {editable ? (
                  <th scope="col">
                    <span className="sr-only">Run now</span>
                  </th>
                ) : null}
              </tr>
            </thead>
            <tbody>
              {CLEANUP_JOBS.map((job) => {
                const state = families.find((f) => f.family === job.family);
                if (!state) return null;
                const interval = state.interval_seconds ?? 3600;
                const choices = EVERY.some((e) => e.seconds === interval)
                  ? EVERY
                  : [
                      ...EVERY,
                      { seconds: interval, label: everyWords(interval) },
                    ];
                return (
                  <tr
                    key={job.family}
                    data-testid={`processing-maintenance-${job.family}`}
                  >
                    <th scope="row" className="mm-quiet-table__name">
                      <span>{job.name}</span>
                      <span
                        className={`mm-cleanup-what${
                          job.destructive ? " mm-cleanup-what--warn" : ""
                        }`}
                      >
                        {state.description}
                      </span>
                    </th>
                    <td data-label="On">
                      <MmOnOffSwitch
                        id={`${ids}-${job.family}-on`}
                        label={`${job.name} on`}
                        enabled={state.enabled}
                        disabled={!editable || save.isPending}
                        layout="control"
                        onChange={(on) =>
                          void change(
                            { [job.enabledField]: on },
                            on
                              ? `${job.name} is on. It first runs within half a minute.`
                              : `${job.name} is off.`,
                          )
                        }
                      />
                    </td>
                    <td data-label="Every">
                      <select
                        className="mm-input mm-cleanup-every"
                        aria-label={`How often ${job.name} runs`}
                        value={interval}
                        disabled={!editable || save.isPending}
                        onChange={(event) => {
                          const seconds = Number(event.target.value);
                          void change(
                            { [job.intervalField]: seconds },
                            `${job.name} now runs every ${everyWords(seconds)}.`,
                          );
                        }}
                      >
                        {choices.map((choice) => (
                          <option key={choice.seconds} value={choice.seconds}>
                            {/^(hour|day)$/.test(choice.label)
                              ? `1 ${choice.label}`
                              : choice.label}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td data-label="Last run">
                      {lastRunLine(state, formatDate)}
                    </td>
                    <td data-label="Next run">
                      {!state.enabled
                        ? "Off"
                        : state.next_run_at
                          ? formatDate(state.next_run_at)
                          : "Within half a minute"}
                    </td>
                    {editable ? (
                      <td data-label="Run now">
                        <button
                          type="button"
                          className={mmActionButtonClass({
                            variant: "tertiary",
                          })}
                          disabled={run.isPending}
                          onClick={() => void runNow(job.family, job.name)}
                          data-testid={`processing-maintenance-run-${job.family}`}
                        >
                          Run now
                        </button>
                      </td>
                    ) : null}
                  </tr>
                );
              })}
              <tr data-testid="processing-maintenance-file-history">
                <th scope="row" className="mm-quiet-table__name">
                  <span>Old file history</span>
                  <span className="mm-cleanup-what">
                    Removes a file&rsquo;s record from History once it is older
                    than this. 0 keeps every record.
                  </span>
                </th>
                <td data-label="On" colSpan={2}>
                  <span className="mm-setrow__unit">
                    <label className="sr-only" htmlFor={`${ids}-retention`}>
                      Keep file history for
                    </label>
                    <input
                      id={`${ids}-retention`}
                      className="mm-input mm-setrow__number"
                      type="number"
                      min={0}
                      max={3650}
                      value={retentionDays}
                      disabled={!editable || save.isPending}
                      onChange={(event) => setRetention(event.target.value)}
                    />
                    days
                    {retentionDirty &&
                    retentionValue >= 0 &&
                    retentionValue <= 3650 ? (
                      <button
                        type="button"
                        className={mmActionButtonClass({
                          variant: "secondary",
                        })}
                        onClick={() =>
                          void change(
                            { file_log_retention_days: retentionValue },
                            retentionValue === 0
                              ? "History keeps every record."
                              : `History keeps ${retentionValue} days of records.`,
                          ).then(() => setRetention(null))
                        }
                      >
                        Save
                      </button>
                    ) : null}
                  </span>
                </td>
                <td data-label="Last run">Checked every hour</td>
                <td data-label="Next run" />
                {editable ? <td /> : null}
              </tr>
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
