import { Link } from "react-router-dom";

import { Panel } from "../../../../components/panels/panel";
import type { MaintenanceFamilyState } from "../../../../lib/processing/maintenance-api";
import { useProcessingMaintenanceQuery } from "../../../../lib/processing/maintenance-queries";
import type { AppSettings } from "../../../../lib/settings/types";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { CLEANUP_JOBS, everyWords } from "../cleanup/cleanup-jobs";
import { backupWords } from "../schedule/schedule-model";
import { setupTabPath } from "../../../../lib/settings/setup-areas";

const UNKNOWN = "—";
const NOT_YET = "Not yet";
const DEFAULT_BACKUP_HOURS = 24;
const DEFAULT_BACKUP_TIME = "02:00";

function whenWords(state: MaintenanceFamilyState | undefined): string {
  if (!state) return UNKNOWN;
  if (!state.enabled) return "Off";
  return state.interval_seconds
    ? `Every ${everyWords(state.interval_seconds)}`
    : "On";
}

function BackupRow({ settings }: { settings: AppSettings }) {
  const formatDate = useAppDateFormatter();
  return (
    <tr>
      <th scope="row" className="mm-quiet-table__name">
        Settings backup
      </th>
      <td data-label="When">
        {backupWords(
          Boolean(settings.configuration_backup_enabled),
          Number(
            settings.configuration_backup_interval_hours ??
              DEFAULT_BACKUP_HOURS,
          ),
          settings.configuration_backup_preferred_time ?? DEFAULT_BACKUP_TIME,
        )}
      </td>
      <td data-label="Last run">
        {settings.configuration_backup_last_run_at
          ? formatDate(settings.configuration_backup_last_run_at)
          : NOT_YET}
      </td>
      <td data-label="Next run">{UNKNOWN}</td>
      <td data-label="">
        <Link
          className="mm-quiet-link"
          to="/system?tab=backups"
          aria-label="Change in Backups"
        >
          Backups
        </Link>
      </td>
    </tr>
  );
}

/** When Weir's own jobs run: the cleanup jobs and the settings backup, each on its own clock. */
export function TimersSection({
  headingId,
  settings,
}: {
  headingId: string;
  settings: AppSettings;
}) {
  const maintenance = useProcessingMaintenanceQuery();
  const formatDate = useAppDateFormatter();
  const families = maintenance.data?.families ?? [];
  return (
    <Panel
      title="Next runs"
      headingId={headingId}
      count="Each runs on its own clock, whatever the workflow hours say."
      padded
    >
      <div className="mm-quiet-table-wrap">
        <table className="mm-quiet-table" data-testid="schedule-timers">
          <thead>
            <tr>
              <th scope="col">Job</th>
              <th scope="col">When</th>
              <th scope="col">Last run</th>
              <th scope="col">Next run</th>
              <th scope="col">
                <span className="sr-only">Where to change it</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {CLEANUP_JOBS.map((job) => {
              const state = families.find((f) => f.family === job.family);
              return (
                <tr key={job.family}>
                  <th scope="row" className="mm-quiet-table__name">
                    {job.name}
                  </th>
                  <td data-label="When">{whenWords(state)}</td>
                  <td data-label="Last run">
                    {state?.last_completed_at
                      ? formatDate(state.last_completed_at)
                      : NOT_YET}
                  </td>
                  <td data-label="Next run">
                    {state?.enabled && state.next_run_at
                      ? formatDate(state.next_run_at)
                      : UNKNOWN}
                  </td>
                  <td data-label="">
                    <Link
                      className="mm-quiet-link"
                      to={setupTabPath("cleanup")}
                      aria-label="Change in Cleanup"
                    >
                      Cleanup
                    </Link>
                  </td>
                </tr>
              );
            })}
            <BackupRow settings={settings} />
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
