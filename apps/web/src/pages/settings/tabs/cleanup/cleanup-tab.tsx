import { useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import { useCanEdit } from "../../../../lib/auth/can-edit";
import type { MaintenanceFamily } from "../../../../lib/processing/maintenance-api";
import {
  useProcessingMaintenanceQuery,
  useRunProcessingMaintenance,
} from "../../../../lib/processing/maintenance-queries";
import { processingKeys } from "../../../../lib/processing/query-keys";
import {
  useProcessingOperatorSettingsQuery,
  useProcessingOperatorSettingsSaveMutation,
} from "../../../../lib/processing/queries";
import { plural } from "../../../../lib/ui/mm-plural";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";
import {
  CleanupConfirmDialog,
  type CleanupConfirmAction,
} from "./cleanup-confirm-dialog";
import { CLEANUP_JOBS, type CleanupJob } from "./cleanup-jobs";
import {
  CleanupJobRow,
  DaysSettingRow,
  type SaveSetting,
} from "./cleanup-rows";

/** The wait before an unclaimed hand-back copy may be removed. */
const WINDOW_MIN_DAYS = 1;
const WINDOW_MAX_DAYS = 365;
const WINDOW_DEFAULT_DAYS = 14;
/** File history is kept for up to ten years; 0 keeps every record. */
const RETENTION_MAX_DAYS = 3650;

/** Saves a setting, refreshes the jobs it changes, and says what happened. */
function useSaveSetting(onNotice: (notice: string | null) => void) {
  const save = useProcessingOperatorSettingsSaveMutation();
  const queryClient = useQueryClient();
  const saveSetting: SaveSetting = async (body, said) => {
    onNotice(null);
    try {
      await save.mutateAsync(body);
      void queryClient.invalidateQueries({
        queryKey: processingKeys.maintenance,
      });
      onNotice(said);
    } catch {
      onNotice("That change could not be saved. Refresh and try again.");
    }
  };
  return { saveSetting, saving: save.isPending };
}

/** Runs a job now for both kinds of library, so "Run now" means the whole job. */
function useRunNow(onNotice: (notice: string | null) => void) {
  const run = useRunProcessingMaintenance();
  const runNow = async (family: MaintenanceFamily, name: string) => {
    onNotice(null);
    try {
      const movies = await run.mutateAsync({ family, mediaScope: "movie" });
      const tv = await run.mutateAsync({ family, mediaScope: "tv" });
      onNotice(
        movies.queued || tv.queued
          ? `${name}: started. Its result shows under Last run.`
          : movies.detail,
      );
    } catch {
      onNotice(`${name} could not be started.`);
    }
  };
  return { runNow, running: run.isPending };
}

/**
 * Settings › Cleanup: the small jobs that keep Weir's folders and records tidy, each with its own
 * switch and timer. A change applies within half a minute, with no restart: Weir's timers read the
 * switch and the interval again every 30 seconds.
 */
export function CleanupTab() {
  const editable = useCanEdit();
  const maintenance = useProcessingMaintenanceQuery();
  const settings = useProcessingOperatorSettingsQuery();
  const ids = useId();
  const [notice, setNotice] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<{
    job: CleanupJob;
    action: CleanupConfirmAction;
  } | null>(null);
  const { saveSetting, saving } = useSaveSetting(setNotice);
  const { runNow, running } = useRunNow(setNotice);

  if (maintenance.isPending || settings.isPending) {
    return <PageLoading label="Loading cleanup settings" />;
  }
  if (maintenance.isError || settings.isError) {
    return <SettingsLoadError what="cleanup settings" />;
  }

  const families = maintenance.data.families;
  const confirmingState = confirming
    ? families.find((f) => f.family === confirming.job.family)
    : undefined;

  const enableJob = (job: CleanupJob) =>
    void saveSetting(
      { [job.enabledField]: true },
      `${job.name} is on. It first runs within half a minute.`,
    );

  return (
    <div
      className="mm-quiet-stack"
      data-testid="processing-maintenance-section"
    >
      <SaveModelNote model="instant" />
      <p className="mm-quiet-note">
        Small jobs that keep Weir&rsquo;s folders and records tidy. Each runs on
        its own timer, and a change applies within half a minute, with no
        restart.
      </p>

      {notice ? (
        <p
          className="text-sm font-medium text-mm-text1"
          role="status"
          data-testid="processing-maintenance-notice"
        >
          {notice}
        </p>
      ) : null}

      {families.length === 0 ? (
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
                return state ? (
                  <CleanupJobRow
                    key={job.family}
                    job={job}
                    state={state}
                    switchId={`${ids}-${job.family}-on`}
                    editable={editable}
                    saving={saving}
                    running={running}
                    onSave={saveSetting}
                    onRun={() => void runNow(job.family, job.name)}
                    onRequestConfirm={(action) =>
                      setConfirming({ job, action })
                    }
                  />
                ) : null;
              })}
              <DaysSettingRow
                testId="processing-maintenance-handback-window"
                name="Cleaned copies nobody picked up wait"
                description="How long a copy Weir made for a media manager waits before Cleaned copies nobody picked up may remove it."
                inputId={`${ids}-window`}
                inputLabel="Cleaned copies nobody picked up wait for"
                saved={
                  settings.data.unclaimed_handback_window_days ??
                  WINDOW_DEFAULT_DAYS
                }
                min={WINDOW_MIN_DAYS}
                max={WINDOW_MAX_DAYS}
                editable={editable}
                saving={saving}
                savedWords={(days) =>
                  `Cleaned copies nobody picked up now wait ${plural(days, "day", "days")}.`
                }
                onSave={saveSetting}
                toBody={(days) => ({ unclaimed_handback_window_days: days })}
              />
              <DaysSettingRow
                testId="processing-maintenance-file-history"
                name="Old file history"
                description="Removes a file’s record from History once it is older than this. 0 keeps every record."
                inputId={`${ids}-retention`}
                inputLabel="Keep file history for"
                saved={settings.data.file_log_retention_days}
                min={0}
                max={RETENTION_MAX_DAYS}
                lastRun="Checked every hour"
                editable={editable}
                saving={saving}
                savedWords={(days) =>
                  days === 0
                    ? "History keeps every record."
                    : `History keeps ${days} days of records.`
                }
                onSave={saveSetting}
                toBody={(days) => ({ file_log_retention_days: days })}
              />
            </tbody>
          </table>
        </div>
      )}

      {confirming && confirmingState ? (
        <CleanupConfirmDialog
          job={confirming.job}
          state={confirmingState}
          action={confirming.action}
          onCancel={() => setConfirming(null)}
          onConfirm={() => {
            const { job, action } = confirming;
            setConfirming(null);
            if (action === "enable") {
              enableJob(job);
            } else {
              void runNow(job.family, job.name);
            }
          }}
        />
      ) : null}
    </div>
  );
}
