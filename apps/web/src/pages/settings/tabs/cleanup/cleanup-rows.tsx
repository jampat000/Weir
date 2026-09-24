import { useState } from "react";

import { MmOnOffSwitch } from "../../../../components/ui/mm-on-off-switch";
import type { MaintenanceFamilyState } from "../../../../lib/processing/maintenance-api";
import type { ProcessingOperatorSettingsPutBody } from "../../../../lib/processing/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import type { CleanupConfirmAction } from "./cleanup-confirm-dialog";
import {
  choiceLabel,
  everyChoices,
  everyWords,
  lastRunLine,
  type CleanupJob,
} from "./cleanup-jobs";

/** An interval the server has not reported yet reads as hourly, the jobs' own default. */
const DEFAULT_INTERVAL_SECONDS = 3600;

/** Saves one operator setting and says in words what changed. */
export type SaveSetting = (
  body: ProcessingOperatorSettingsPutBody,
  said: string,
) => Promise<void>;

function nextRunWords(
  state: MaintenanceFamilyState,
  formatDate: (iso: string) => string,
): string {
  if (!state.enabled) return "Off";
  return state.next_run_at
    ? formatDate(state.next_run_at)
    : "Within half a minute";
}

/** One cleanup job: its switch, its timer, when it last and next runs, and Run now. */
export function CleanupJobRow({
  job,
  state,
  switchId,
  editable,
  saving,
  running,
  onSave,
  onRun,
  onRequestConfirm,
}: {
  job: CleanupJob;
  state: MaintenanceFamilyState;
  switchId: string;
  editable: boolean;
  saving: boolean;
  running: boolean;
  onSave: SaveSetting;
  onRun: () => void;
  /** Asks for confirmation before a destructive job is switched on or run now. */
  onRequestConfirm: (action: CleanupConfirmAction) => void;
}) {
  const formatDate = useAppDateFormatter();
  const interval = state.interval_seconds ?? DEFAULT_INTERVAL_SECONDS;
  return (
    <tr data-testid={`processing-maintenance-${job.family}`}>
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
          id={switchId}
          label={`${job.name} on`}
          enabled={state.enabled}
          disabled={!editable || saving}
          layout="control"
          onChange={(on) => {
            if (on && job.destructive) {
              onRequestConfirm("enable");
              return;
            }
            void onSave(
              { [job.enabledField]: on },
              on
                ? `${job.name} is on. It first runs within half a minute.`
                : `${job.name} is off.`,
            );
          }}
        />
      </td>
      <td data-label="Every">
        <select
          className="mm-input mm-cleanup-every"
          aria-label={`How often ${job.name} runs`}
          value={interval}
          disabled={!editable || saving}
          onChange={(event) => {
            const seconds = Number(event.target.value);
            void onSave(
              { [job.intervalField]: seconds },
              `${job.name} now runs every ${everyWords(seconds)}.`,
            );
          }}
        >
          {everyChoices(interval).map((choice) => (
            <option key={choice.seconds} value={choice.seconds}>
              {choiceLabel(choice.label)}
            </option>
          ))}
        </select>
      </td>
      <td data-label="Last run">{lastRunLine(state, formatDate)}</td>
      <td data-label="Next run">{nextRunWords(state, formatDate)}</td>
      {editable ? (
        <td data-label="Run now">
          <button
            type="button"
            className={mmActionButtonClass({ variant: "tertiary" })}
            disabled={running}
            onClick={() =>
              job.destructive ? onRequestConfirm("run") : onRun()
            }
            data-testid={`processing-maintenance-run-${job.family}`}
          >
            Run now
          </button>
        </td>
      ) : null}
    </tr>
  );
}

/**
 * A setting counted in days, in the same table as the jobs it governs. Save appears once the value
 * differs from the saved one and is within range.
 */
export function DaysSettingRow({
  testId,
  name,
  description,
  inputId,
  inputLabel,
  saved,
  min,
  max,
  lastRun,
  editable,
  saving,
  savedWords,
  onSave,
  toBody,
}: {
  testId: string;
  name: string;
  description: string;
  inputId: string;
  inputLabel: string;
  saved: number | undefined;
  min: number;
  max: number;
  lastRun?: string;
  editable: boolean;
  saving: boolean;
  savedWords: (days: number) => string;
  onSave: SaveSetting;
  toBody: (days: number) => ProcessingOperatorSettingsPutBody;
}) {
  const [draft, setDraft] = useState<string | null>(null);
  const shown = draft ?? String(saved ?? "");
  const value = Number.parseInt(shown, 10);
  const dirty = saved !== undefined && draft !== null && value !== saved;
  const inRange = value >= min && value <= max;
  return (
    <tr data-testid={testId}>
      <th scope="row" className="mm-quiet-table__name">
        <span>{name}</span>
        <span className="mm-cleanup-what">{description}</span>
      </th>
      <td data-label="On" colSpan={2}>
        <span className="mm-setrow__unit">
          <label className="sr-only" htmlFor={inputId}>
            {inputLabel}
          </label>
          <input
            id={inputId}
            className="mm-input mm-setrow__number"
            type="number"
            min={min}
            max={max}
            value={shown}
            disabled={!editable || saving}
            onChange={(event) => setDraft(event.target.value)}
          />
          days
          {dirty && inRange ? (
            <button
              type="button"
              className={mmActionButtonClass({ variant: "secondary" })}
              disabled={saving}
              onClick={() =>
                void onSave(toBody(value), savedWords(value)).then(() =>
                  setDraft(null),
                )
              }
            >
              {saving ? "Saving…" : "Save"}
            </button>
          ) : null}
        </span>
      </td>
      <td data-label="Last run">{lastRun}</td>
      <td data-label="Next run" />
      {editable ? <td /> : null}
    </tr>
  );
}
