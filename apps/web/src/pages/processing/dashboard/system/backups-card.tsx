import { Link } from "react-router-dom";

import { Chip } from "../../../../components/panels/chip";
import { Panel } from "../../../../components/panels/panel";
import { StatusDot } from "../../../../components/panels/status-dot";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useAppSettingsQuery,
  useConfigurationBackupsQuery,
  useUpdateStateQuery,
  useUpdateStatusQuery,
} from "../../../../lib/settings/queries";
import { useMediaToolsQuery } from "../../../../lib/system/media-tools";
import {
  parseAppTime,
  useAppClockFormatter,
} from "../../../../lib/ui/mm-format-date";
import { useNow } from "../../../../lib/ui/use-now";
import { useFittingRows } from "../fit-rows";
import { toolRows } from "../health-model";
import {
  latestBackups,
  nextBackupWords,
  scheduleWords,
  updateFacts,
  type BackupRow,
} from "./backups-card-model";
import { nextBackupAt } from "./backup-schedule";
import { MoreCount } from "./more-count";
import { ABOUT_PATH, BACKUPS_PATH } from "./system-paths";
import { whenWords, type WhenFormat } from "./system-time";
import { useBackUpNow } from "./use-back-up-now";

/** The next-backup countdown moves by minutes, so a minute is as often as it is worked out again. */
const TICK_MS = 60_000;

function BackupLine({
  row,
  when,
  fit = false,
}: {
  row: BackupRow;
  when: string;
  /** The row is a part of its own that the card's height may leave out. */
  fit?: boolean;
}) {
  return (
    <li
      className="mm-sy-backup"
      data-fit={fit ? "" : undefined}
      data-testid="system-backup"
    >
      <StatusDot meaning="done" />
      <span>{when}</span>
      <span className="mm-sy-backup__size">{row.size}</span>
    </li>
  );
}

function ToolLines() {
  const tools = useMediaToolsQuery();
  const rows = tools.data ? toolRows(tools.data) : null;
  return (
    <section
      className="mm-sy-part mm-sy-part--inline"
      aria-label="Tools"
      data-fit=""
      data-testid="system-tools"
    >
      <h3 className="mm-sy-part__title">Tools</h3>
      {rows === null ? (
        <p className="mm-sy-note">Reading the tools…</p>
      ) : (
        <ul className="mm-sy-inline-list">
          {rows.map((tool) => (
            <li
              key={tool.key}
              className="mm-sy-tool"
              title={tool.banner}
              data-testid="system-tool"
            >
              <b>{tool.name}</b>
              <span>{tool.version}</span>
              <span
                className="mm-sy-tool__mark mm-status-text"
                data-status={tool.meaning}
                aria-label={
                  tool.meaning === "done" ? "Installed" : "Not installed"
                }
              >
                {tool.meaning === "done" ? "✓" : "—"}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function UpdateLines() {
  const status = useUpdateStatusQuery();
  const state = useUpdateStateQuery();
  const facts = status.data ? updateFacts(status.data, state.data) : null;
  return (
    <section
      className="mm-sy-part mm-sy-part--inline"
      aria-label="Updates"
      data-fit=""
      data-testid="system-updates"
    >
      <h3 className="mm-sy-part__title">Updates</h3>
      {facts === null ? (
        <p className="mm-sy-note">
          {status.isError ? "Weir could not check for updates." : "Checking…"}
        </p>
      ) : (
        <div className="mm-sy-update">
          <span>
            Running <b>{facts.current}</b>
          </span>
          {facts.upToDate ? null : (
            <span>
              Latest <b>{facts.latest}</b>
            </span>
          )}
          <Link to={ABOUT_PATH} className="mm-sy-update__state">
            <Chip meaning={facts.meaning}>{facts.state}</Chip>
          </Link>
        </div>
      )}
    </section>
  );
}

/**
 * Dashboard › System: the backup schedule with "Back up now" and when the next one runs, the newest backup, where
 * updating stands and the tools Weir writes with, one line each, then the older backups. The card's height decides
 * how many whole parts show, and the header says how many rows the rest come to.
 */
export function BackupsCard() {
  const settings = useAppSettingsQuery();
  const backups = useConfigurationBackupsQuery(true);
  const backUp = useBackUpNow();
  const now = useNow(TICK_MS);
  const clock = useAppClockFormatter();
  const [listRef, fits] = useFittingRows();
  const format: WhenFormat = {
    timeZone: settings.data?.app_timezone || undefined,
    clock,
  };
  const rows = latestBackups(backups.data?.items ?? []);
  const schedule = settings.data
    ? scheduleWords({
        enabled: settings.data.configuration_backup_enabled,
        intervalHours: settings.data.configuration_backup_interval_hours,
        preferredTime: settings.data.configuration_backup_preferred_time,
      })
    : "";
  const next = settings.data
    ? nextBackupWords(
        nextBackupAt(
          {
            enabled: settings.data.configuration_backup_enabled,
            intervalHours: settings.data.configuration_backup_interval_hours,
            preferredTime: settings.data.configuration_backup_preferred_time,
            lastRunAt: parseAppTime(
              settings.data.configuration_backup_last_run_at,
            ),
          },
          now,
          format.timeZone,
        ),
        now,
      )
    : "";
  const [latest, ...earlier] = rows;
  // The parts in the order they are measured, each worth the rows it holds: the heading, the newest backup, the
  // updates, the tools, and the older backups, which come last so they are what a short card leaves out.
  const parts = [1, latest ? 1 : 0, 1, 1, earlier.length].filter(
    (rowCount) => rowCount > 0,
  );
  const more = parts.slice(fits).reduce((sum, rowCount) => sum + rowCount, 0);
  return (
    <Panel
      title="Backups and tools"
      aside={<MoreCount count={more} />}
      to={BACKUPS_PATH}
      toLabel="Backups"
      dataTestId="system-backups"
      className="mm-sy-card"
    >
      <div ref={listRef} className="mm-sy-fit">
        <section
          className="mm-sy-part"
          aria-label="Backups"
          data-fit=""
          data-testid="system-backup-head"
        >
          <div className="mm-sy-part__head">
            <h3 className="mm-sy-part__title">Backups</h3>
            <button
              type="button"
              className="mm-sy-btn"
              disabled={backUp.isPending}
              title="Saves Weir's settings to a backup file now. Nothing is changed."
              onClick={() => backUp.mutate()}
            >
              {backUp.isPending ? "Backing up…" : "Back up now"}
            </button>
          </div>
          <p className="mm-sy-part__line" title={schedule}>
            <span>{schedule}</span>
            {next ? <span>{next}</span> : null}
          </p>
          {backUp.isError ? (
            <p
              className="mm-sy-note mm-status-text"
              data-status="broken"
              role="alert"
            >
              {errorMessage(backUp.error, "Weir could not make a backup.")}
            </p>
          ) : null}
        </section>
        {rows.length === 0 && backups.isSuccess ? (
          <p className="mm-sy-note">No backup has been made yet.</p>
        ) : null}
        {latest ? (
          <ul className="mm-sy-list">
            <BackupLine
              row={latest}
              when={whenWords(latest.at, now, format)}
              fit
            />
          </ul>
        ) : null}
        <UpdateLines />
        <ToolLines />
        {earlier.length > 0 ? (
          <section
            className="mm-sy-part mm-sy-part--earlier"
            aria-label="Earlier backups"
            data-fit=""
          >
            <h3 className="mm-sy-part__title">Earlier backups</h3>
            <ul className="mm-sy-list">
              {earlier.map((row) => (
                <BackupLine
                  key={row.id}
                  row={row}
                  when={whenWords(row.at, now, format)}
                />
              ))}
            </ul>
          </section>
        ) : null}
      </div>
    </Panel>
  );
}
