import { useId, useState, type ReactNode } from "react";
import type { UseQueryResult } from "@tanstack/react-query";
import { PageLoading } from "../../components/shared/page-loading";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { useMeQuery } from "../../lib/auth/queries";
import { useFetcherArrOperatorSettingsQuery } from "../../lib/fetcher/arr-operator-settings/queries";
import type { FetcherArrOperatorSettingsOut } from "../../lib/fetcher/arr-operator-settings/types";
import { failedImportManualQueuePassResultMessage } from "../../lib/fetcher/failed-imports/enqueue-messages";
import { showFailedImportManualQueuePassControl } from "../../lib/fetcher/failed-imports/eligibility";
import { jobInspectionDetailsForOperator } from "../../lib/fetcher/failed-imports/job-inspection-details";
import {
  fetcherJobDedupeKeyOperatorLabel,
  fetcherJobKindOperatorLabel,
} from "../../lib/fetcher/fetcher-job-operator-labels";
import { FETCHER_JOBS_INSPECTION_FILTER_OPTIONS } from "../../lib/fetcher/jobs-inspection/filter-labels";
import type { FetcherJobsInspectionFilter } from "../../lib/fetcher/jobs-inspection/queries";
import { useFetcherJobsInspectionQuery } from "../../lib/fetcher/jobs-inspection/queries";
import {
  useFailedImportCleanupPolicyQuery,
  useFailedImportQueueAttentionSnapshotQuery,
  useFailedImportRadarrEnqueueMutation,
  useFailedImportSonarrEnqueueMutation,
} from "../../lib/fetcher/failed-imports/queries";
import {
  failedImportTaskStatusPrimaryLabel,
  isHandlerOkFinalizeFailedStatus,
} from "../../lib/fetcher/failed-imports/task-status-labels";
import type {
  FailedImportCleanupPolicyAxis,
  FetcherFailedImportCleanupPolicyOut,
  FetcherFailedImportQueueAttentionAxis,
  FetcherFailedImportQueueAttentionSnapshot,
} from "../../lib/fetcher/failed-imports/types";
import type { FetcherJobInspectionRow } from "../../lib/fetcher/jobs-inspection/types";
import {
  FETCHER_FI_AT_A_GLANCE_SECTION_TITLE,
  FETCHER_FI_FILTER_DEFAULT_HELP,
  FETCHER_FI_FILTER_SINGLE_STATUS_HELP,
  FETCHER_FI_JOB_HISTORY_SHOW_LABEL,
  FETCHER_FI_JOB_HISTORY_LEAD,
  FETCHER_FI_JOB_HISTORY_SUBSECTION_TITLE,
  FETCHER_FI_LIST_EMPTY,
  FETCHER_FI_MANUAL_BTN_MOVIES,
  FETCHER_FI_MANUAL_BTN_TV,
  FETCHER_FI_MANUAL_ERR_MOVIES,
  FETCHER_FI_MANUAL_ERR_TV,
  FETCHER_FI_MANUAL_PENDING,
  FETCHER_FI_MANUAL_RESULT_MOVIES_PREFIX,
  FETCHER_FI_MANUAL_RESULT_TV_PREFIX,
  FETCHER_FI_MANUAL_SECTION_BODY,
  FETCHER_FI_MANUAL_SECTION_TITLE,
  FETCHER_FI_MANUAL_UTILITY_SECTION_TITLE,
  FETCHER_FI_NEEDS_ATTENTION_LEAD,
  FETCHER_FI_NEEDS_ATTENTION_SECTION_TITLE,
  FETCHER_FI_NEEDS_ATTENTION_SUPPORT_CANT_CHECK,
  FETCHER_FI_NEEDS_ATTENTION_SUPPORT_MOVIES_NOT_SETUP,
  FETCHER_FI_NEEDS_ATTENTION_SUPPORT_MOVIES_REVIEW,
  FETCHER_FI_NEEDS_ATTENTION_SUPPORT_NONE,
  FETCHER_FI_NEEDS_ATTENTION_SUPPORT_TV_NOT_SETUP,
  FETCHER_FI_NEEDS_ATTENTION_SUPPORT_TV_REVIEW,
  FETCHER_FI_PAGE_ERR_LOAD_TASKS,
  FETCHER_FI_PAGE_LOADING_TASKS,
  FETCHER_FI_SECTION_INTRO_PRIMARY,
  FETCHER_FI_TABLE_COL_STABLE_KEY,
  FETCHER_FI_TABLE_COL_WORK_TYPE,
  FETCHER_FI_TECHNICAL_SUMMARY_LABEL,
} from "../../lib/fetcher/failed-imports/user-copy";
import { MmListboxPicker } from "../../components/ui/mm-listbox-picker";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { FETCHER_TAB_RADARR_LABEL, FETCHER_TAB_SONARR_LABEL } from "./fetcher-display-names";
import { FetcherFailedImportsCleanupPolicySection } from "./fetcher-failed-imports-cleanup-policy";
import {
  FETCHER_TAB_PANEL_BLURB_CLASS,
  FETCHER_TAB_PANEL_INTRO_CLASS,
  FETCHER_TAB_PANEL_TITLE_CLASS,
} from "./fetcher-tab-panel-intro";

const HANDLING_COUNT_KEYS: (keyof FailedImportCleanupPolicyAxis)[] = [
  "handling_quality_rejection",
  "handling_unmatched_manual_import",
  "handling_sample_release",
  "handling_corrupt_import",
  "handling_failed_download",
  "handling_failed_import",
];

function countConfiguredActions(axis: FailedImportCleanupPolicyAxis): number {
  return HANDLING_COUNT_KEYS.reduce((n, k) => n + (axis[k] !== "leave_alone" ? 1 : 0), 0);
}

function glanceCleanupLine(axis: FailedImportCleanupPolicyAxis | undefined): string {
  if (!axis) {
    return "…";
  }
  const n = countConfiguredActions(axis);
  if (n === 0) {
    return "All leave alone";
  }
  if (n === 1) {
    return "1 action enabled";
  }
  return `${n} actions enabled`;
}

function glanceAttentionLine(configured: boolean, axis: FetcherFailedImportQueueAttentionAxis | undefined): string {
  if (!axis) {
    return "…";
  }
  if (!configured) {
    return "Not set up yet";
  }
  if (axis.needs_attention_count === null) {
    return "Can't check right now";
  }
  const n = axis.needs_attention_count;
  if (n === 0) {
    return "None";
  }
  if (n === 1) {
    return "1 needs review";
  }
  return `${n} need review`;
}

function needsAttentionStatusLine(
  configured: boolean,
  axis: FetcherFailedImportQueueAttentionAxis | undefined,
): string {
  if (!axis) {
    return "…";
  }
  if (!configured) {
    return "Not set up yet";
  }
  if (axis.needs_attention_count === null) {
    return "Can't check right now";
  }
  const n = axis.needs_attention_count;
  if (n === 0) {
    return "None";
  }
  if (n === 1) {
    return "1 needs review";
  }
  return `${n} need review`;
}

function needsAttentionSupportLine(
  kind: "tv" | "movies",
  configured: boolean,
  axis: FetcherFailedImportQueueAttentionAxis | undefined,
): string | null {
  if (!axis) {
    return null;
  }
  if (!configured) {
    return kind === "tv" ? FETCHER_FI_NEEDS_ATTENTION_SUPPORT_TV_NOT_SETUP : FETCHER_FI_NEEDS_ATTENTION_SUPPORT_MOVIES_NOT_SETUP;
  }
  if (axis.needs_attention_count === null) {
    return FETCHER_FI_NEEDS_ATTENTION_SUPPORT_CANT_CHECK;
  }
  const n = axis.needs_attention_count;
  if (n > 0) {
    return kind === "tv" ? FETCHER_FI_NEEDS_ATTENTION_SUPPORT_TV_REVIEW : FETCHER_FI_NEEDS_ATTENTION_SUPPORT_MOVIES_REVIEW;
  }
  return FETCHER_FI_NEEDS_ATTENTION_SUPPORT_NONE;
}

function formatLastChecked(iso: string | null): string | null {
  if (!iso) {
    return null;
  }
  const d = new Date(iso);
  if (Number.isNaN(d.valueOf())) {
    return null;
  }
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(d);
}

function formatUpdated(iso: string): string {
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return iso;
    }
    return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(d);
  } catch {
    return iso;
  }
}

function GlanceColumn({
  title,
  cleanupLine,
  attentionLine,
}: {
  title: string;
  cleanupLine: string;
  attentionLine: string;
}) {
  return (
    <div className="flex h-full flex-col rounded-md border border-[var(--mm-border)] bg-[var(--mm-surface2)]/35 p-4 text-sm">
      <h3 className="text-sm font-semibold text-[var(--mm-text1)]">{title}</h3>
      <dl className="mt-3 space-y-2">
        <div>
          <dt className="text-xs font-medium text-[var(--mm-text3)]">Queue actions</dt>
          <dd className="mt-0.5 font-medium text-[var(--mm-text1)]">{cleanupLine}</dd>
        </div>
        <div>
          <dt className="text-xs font-medium text-[var(--mm-text3)]">Attention</dt>
          <dd className="mt-0.5 font-medium text-[var(--mm-text1)]">{attentionLine}</dd>
        </div>
      </dl>
    </div>
  );
}

function FetcherFailedImportsAtAGlanceSection({
  arr,
  attention,
  policy,
}: {
  arr: UseQueryResult<FetcherArrOperatorSettingsOut, Error>;
  attention: UseQueryResult<FetcherFailedImportQueueAttentionSnapshot, Error>;
  policy: UseQueryResult<FetcherFailedImportCleanupPolicyOut, Error>;
}) {
  const sonConfigured = arr.data?.sonarr_server_configured ?? false;
  const radConfigured = arr.data?.radarr_server_configured ?? false;
  const tvPolicy = policy.data?.tv_shows;
  const movPolicy = policy.data?.movies;
  const tvAtt = attention.data?.tv_shows;
  const movAtt = attention.data?.movies;

  return (
    <section
      className="mm-card mm-dash-card mm-fetcher-module-surface"
      aria-labelledby="mm-fetcher-fi-at-glance-heading"
      data-testid="fetcher-failed-imports-at-a-glance"
    >
      <h2 id="mm-fetcher-fi-at-glance-heading" className="mm-card__title">
        {FETCHER_FI_AT_A_GLANCE_SECTION_TITLE}
      </h2>
      <div className="mm-card__body mm-card__body--tight mt-1 grid gap-4 sm:grid-cols-2">
        <GlanceColumn
          title={FETCHER_TAB_SONARR_LABEL}
          cleanupLine={glanceCleanupLine(tvPolicy)}
          attentionLine={glanceAttentionLine(sonConfigured, tvAtt)}
        />
        <GlanceColumn
          title={FETCHER_TAB_RADARR_LABEL}
          cleanupLine={glanceCleanupLine(movPolicy)}
          attentionLine={glanceAttentionLine(radConfigured, movAtt)}
        />
      </div>
    </section>
  );
}

function NeedsAttentionAxisCard({
  title,
  kind,
  configured,
  axis,
}: {
  title: string;
  kind: "tv" | "movies";
  configured: boolean;
  axis: FetcherFailedImportQueueAttentionAxis | undefined;
}) {
  const status = needsAttentionStatusLine(configured, axis);
  const support = needsAttentionSupportLine(kind, configured, axis);
  const last = axis ? formatLastChecked(axis.last_checked_at) : null;
  return (
    <div className="flex h-full flex-col rounded-md border border-[var(--mm-border)] bg-[var(--mm-surface2)]/35 p-4 text-sm">
      <h3 className="text-sm font-semibold text-[var(--mm-text1)]">{title}</h3>
      <p className="mt-3 text-base font-semibold leading-snug text-[var(--mm-text1)]">{status}</p>
      {support ? <p className="mt-2 text-sm leading-snug text-[var(--mm-text2)]">{support}</p> : null}
      {last ? (
        <p className="mt-3 text-xs text-[var(--mm-text3)]">
          <span className="font-medium text-[var(--mm-text2)]">Last checked </span>
          {last}
        </p>
      ) : configured && axis?.needs_attention_count !== null ? (
        <p className="mt-3 text-xs text-[var(--mm-text3)]">No timestamp from a successful check yet.</p>
      ) : null}
    </div>
  );
}

function FetcherFailedImportsNeedsAttentionSection({
  arr,
  attention,
}: {
  arr: UseQueryResult<FetcherArrOperatorSettingsOut, Error>;
  attention: UseQueryResult<FetcherFailedImportQueueAttentionSnapshot, Error>;
}) {
  return (
    <section
      className="mm-card mm-dash-card mm-fetcher-module-surface"
      aria-labelledby="mm-fetcher-fi-needs-attention-heading"
      data-testid="fetcher-failed-imports-needs-attention"
    >
      <h2 id="mm-fetcher-fi-needs-attention-heading" className="mm-card__title">
        {FETCHER_FI_NEEDS_ATTENTION_SECTION_TITLE}
      </h2>
      <p className="mm-card__body mm-card__body--tight text-sm leading-relaxed text-[var(--mm-text2)]">
        {FETCHER_FI_NEEDS_ATTENTION_LEAD}
      </p>
      <div className="mm-card__body mm-card__body--tight mt-4 grid gap-4 sm:grid-cols-2">
        <NeedsAttentionAxisCard
          title={FETCHER_TAB_SONARR_LABEL}
          kind="tv"
          configured={Boolean(arr.data?.sonarr_server_configured)}
          axis={attention.data?.tv_shows}
        />
        <NeedsAttentionAxisCard
          title={FETCHER_TAB_RADARR_LABEL}
          kind="movies"
          configured={Boolean(arr.data?.radarr_server_configured)}
          axis={attention.data?.movies}
        />
      </div>
    </section>
  );
}

function FetcherFailedImportsManualQueuePassPanel({ enabled }: { enabled: boolean }) {
  const mRad = useFailedImportRadarrEnqueueMutation();
  const mSon = useFailedImportSonarrEnqueueMutation();

  if (!enabled) {
    return null;
  }

  const queueBusy = mRad.isPending || mSon.isPending;

  return (
    <div className="space-y-3" data-testid="fetcher-failed-imports-manual-queue-pass">
      <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap">
        <button
          type="button"
          className={mmActionButtonClass({
            variant: "secondary",
            disabled: queueBusy,
          })}
          data-testid="fetcher-failed-imports-queue-pass-radarr"
          disabled={queueBusy}
          onClick={() => mRad.mutate()}
        >
          {mRad.isPending ? FETCHER_FI_MANUAL_PENDING : FETCHER_FI_MANUAL_BTN_MOVIES}
        </button>
        <button
          type="button"
          className={mmActionButtonClass({
            variant: "secondary",
            disabled: queueBusy,
          })}
          data-testid="fetcher-failed-imports-queue-pass-sonarr"
          disabled={queueBusy}
          onClick={() => mSon.mutate()}
        >
          {mSon.isPending ? FETCHER_FI_MANUAL_PENDING : FETCHER_FI_MANUAL_BTN_TV}
        </button>
      </div>
      {mRad.isError ? (
        <p className="mt-2 text-sm text-red-400" role="alert">
          {mRad.error instanceof Error ? mRad.error.message : FETCHER_FI_MANUAL_ERR_MOVIES}
        </p>
      ) : null}
      {mSon.isError ? (
        <p className="mt-2 text-sm text-red-400" role="alert">
          {mSon.error instanceof Error ? mSon.error.message : FETCHER_FI_MANUAL_ERR_TV}
        </p>
      ) : null}
      {mRad.isSuccess && mRad.data ? (
        <p className="mt-2 text-sm text-[var(--mm-text2)]" data-testid="fetcher-failed-imports-queue-pass-radarr-result">
          {FETCHER_FI_MANUAL_RESULT_MOVIES_PREFIX} {failedImportManualQueuePassResultMessage(mRad.data)}
        </p>
      ) : null}
      {mSon.isSuccess && mSon.data ? (
        <p className="mt-2 text-sm text-[var(--mm-text2)]" data-testid="fetcher-failed-imports-queue-pass-sonarr-result">
          {FETCHER_FI_MANUAL_RESULT_TV_PREFIX} {failedImportManualQueuePassResultMessage(mSon.data)}
        </p>
      ) : null}
    </div>
  );
}

function JobDetailsCell({ lastError, jobKind }: { lastError: string | null; jobKind: string }) {
  const { friendly, technical } = jobInspectionDetailsForOperator(lastError, jobKind);
  if (!technical) {
    return <span className="text-sm text-[var(--mm-text2)]">{friendly}</span>;
  }
  return (
    <div className="max-w-md">
      <p className="text-sm text-[var(--mm-text1)]">{friendly}</p>
      <details className="mt-1.5">
        <summary className="cursor-pointer text-xs text-[var(--mm-text3)] hover:text-[var(--mm-text2)]">
          {FETCHER_FI_TECHNICAL_SUMMARY_LABEL}
        </summary>
        <pre className="mt-1 max-h-28 overflow-auto rounded border border-[var(--mm-border)] bg-black/15 p-2 font-mono text-[10px] leading-snug text-[var(--mm-text3)]">
          {technical}
        </pre>
      </details>
    </div>
  );
}

function TaskRow({ job }: { job: FetcherJobInspectionRow }) {
  const emphasizeFinalize = isHandlerOkFinalizeFailedStatus(job.status);
  return (
    <tr
      data-testid="fetcher-jobs-inspection-row"
      data-job-status={job.status}
      className={
        emphasizeFinalize ? "border-l-2 border-l-[var(--mm-accent)] bg-[rgba(212,175,55,0.06)]" : undefined
      }
    >
      <td className="mm-fetcher-fi-inspection__cell align-top py-2 pr-3">
        <div className="font-medium text-[var(--mm-text)]" data-testid="fetcher-failed-imports-status-label">
          {failedImportTaskStatusPrimaryLabel(job.status)}
        </div>
      </td>
      <td className="mm-fetcher-fi-inspection__cell align-top py-2 pr-3 text-sm text-[var(--mm-text)]">
        <span data-testid="fetcher-jobs-inspection-job-kind-label">{fetcherJobKindOperatorLabel(job.job_kind)}</span>
      </td>
      <td className="mm-fetcher-fi-inspection__cell align-top py-2 pr-3 text-sm text-[var(--mm-text)] break-words">
        <span data-testid="fetcher-jobs-inspection-stable-key-label">
          {fetcherJobDedupeKeyOperatorLabel(job.dedupe_key, job.job_kind)}
        </span>
      </td>
      <td className="mm-fetcher-fi-inspection__cell align-top py-2 pr-3 text-sm text-[var(--mm-text2)] whitespace-nowrap">
        {job.attempt_count} / {job.max_attempts}
      </td>
      <td className="mm-fetcher-fi-inspection__cell align-top py-2 pr-3 text-sm text-[var(--mm-text2)] whitespace-nowrap">
        {formatUpdated(job.updated_at)}
      </td>
      <td className="mm-fetcher-fi-inspection__cell align-top py-2 pr-0">
        <JobDetailsCell lastError={job.last_error} jobKind={job.job_kind} />
      </td>
    </tr>
  );
}

function FetcherFailedImportsJobHistoryContent({
  filter,
  setFilter,
  jobs,
  default_terminal_only,
  isEmpty,
}: {
  filter: FetcherJobsInspectionFilter;
  setFilter: (f: FetcherJobsInspectionFilter) => void;
  jobs: FetcherJobInspectionRow[];
  default_terminal_only: boolean;
  isEmpty: boolean;
}) {
  const filterLabelId = useId();
  return (
    <>
      <label className="block max-w-xl">
        <span id={filterLabelId} className="text-xs font-medium text-[var(--mm-text3)]">
          {FETCHER_FI_JOB_HISTORY_SHOW_LABEL}
        </span>
        <MmListboxPicker
          className="max-w-xl"
          data-testid="fetcher-failed-imports-status-filter"
          ariaLabelledBy={filterLabelId}
          placeholder="Select filter"
          options={FETCHER_JOBS_INSPECTION_FILTER_OPTIONS.map((o) => ({ value: o.value, label: o.label }))}
          value={filter}
          onChange={(v) => setFilter(v as FetcherJobsInspectionFilter)}
        />
      </label>
      {default_terminal_only ? (
        <p className="mt-2 text-sm text-[var(--mm-text3)]">{FETCHER_FI_FILTER_DEFAULT_HELP}</p>
      ) : (
        <p className="mt-2 text-sm text-[var(--mm-text3)]">{FETCHER_FI_FILTER_SINGLE_STATUS_HELP}</p>
      )}
      {isEmpty ? (
        <p className="mt-4 text-sm text-[var(--mm-text2)]" data-testid="fetcher-jobs-inspection-empty">
          {FETCHER_FI_LIST_EMPTY}
        </p>
      ) : (
        <div className="mt-4 overflow-x-auto">
          <table className="w-full min-w-[38rem] border-collapse text-left">
            <thead>
              <tr className="border-b border-[var(--mm-border)] text-xs uppercase tracking-wide text-[var(--mm-text3)]">
                <th className="pb-2 pr-3 font-semibold">Status</th>
                <th className="pb-2 pr-3 font-semibold">{FETCHER_FI_TABLE_COL_WORK_TYPE}</th>
                <th className="pb-2 pr-3 font-semibold">{FETCHER_FI_TABLE_COL_STABLE_KEY}</th>
                <th className="pb-2 pr-3 font-semibold">Retries</th>
                <th className="pb-2 pr-3 font-semibold">Last change</th>
                <th className="pb-2 font-semibold">Details</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--mm-border)]">
              {jobs.map((j) => (
                <TaskRow key={j.id} job={j} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

/** Fetcher Failed imports tab: glance → cleanup → attention → manual checks & history. */
export function FetcherFailedImportsWorkspace() {
  const [filter, setFilter] = useState<FetcherJobsInspectionFilter>("terminal");
  const me = useMeQuery();
  const arr = useFetcherArrOperatorSettingsQuery();
  const attention = useFailedImportQueueAttentionSnapshotQuery();
  const cleanupPolicy = useFailedImportCleanupPolicyQuery();
  const q = useFetcherJobsInspectionQuery(filter);

  let jobHistorySlot: ReactNode;
  if (q.isPending) {
    jobHistorySlot = <PageLoading label={FETCHER_FI_PAGE_LOADING_TASKS} />;
  } else if (q.isError) {
    const err = q.error;
    jobHistorySlot = (
      <div className="space-y-2">
        <p className="text-sm text-[var(--mm-text2)]">
          {isLikelyNetworkFailure(err)
            ? "Could not reach the MediaMop API. Check that the backend is running."
            : isHttpErrorFromApi(err)
              ? "The server refused this request. Sign in again or check API logs."
              : FETCHER_FI_PAGE_ERR_LOAD_TASKS}
        </p>
        {err instanceof Error ? <p className="font-mono text-xs text-[var(--mm-text3)]">{err.message}</p> : null}
      </div>
    );
  } else {
    const { jobs, default_terminal_only } = q.data;
    jobHistorySlot = (
      <FetcherFailedImportsJobHistoryContent
        filter={filter}
        setFilter={setFilter}
        jobs={jobs}
        default_terminal_only={default_terminal_only}
        isEmpty={jobs.length === 0}
      />
    );
  }

  return (
    <div data-testid="fetcher-failed-imports-workspace">
      <header className={FETCHER_TAB_PANEL_INTRO_CLASS}>
        <h2 className={FETCHER_TAB_PANEL_TITLE_CLASS}>Failed imports</h2>
        <p className={FETCHER_TAB_PANEL_BLURB_CLASS}>{FETCHER_FI_SECTION_INTRO_PRIMARY}</p>
      </header>
      <div className="space-y-6">
        <FetcherFailedImportsAtAGlanceSection arr={arr} attention={attention} policy={cleanupPolicy} />
        <FetcherFailedImportsCleanupPolicySection role={me.data?.role} />
        <FetcherFailedImportsNeedsAttentionSection arr={arr} attention={attention} />
        <section
          className="mm-card mm-dash-card mm-fetcher-module-surface overflow-x-auto"
          aria-labelledby="mm-fetcher-fi-manual-utility-heading"
          data-testid="fetcher-failed-imports-manual-utility"
        >
          <h2 id="mm-fetcher-fi-manual-utility-heading" className="mm-card__title">
            {FETCHER_FI_MANUAL_UTILITY_SECTION_TITLE}
          </h2>
          <div className="mm-card__body mm-card__body--tight space-y-8">
            <div>
              <h3 className="text-base font-semibold text-[var(--mm-text1)]">{FETCHER_FI_MANUAL_SECTION_TITLE}</h3>
              <p className="mt-1 text-sm text-[var(--mm-text3)]">{FETCHER_FI_MANUAL_SECTION_BODY}</p>
              <div className="mt-3">
                <FetcherFailedImportsManualQueuePassPanel enabled={showFailedImportManualQueuePassControl(me.data?.role)} />
              </div>
            </div>
            <div className="border-t border-[var(--mm-border)] pt-6">
              <h3 className="text-base font-semibold text-[var(--mm-text1)]">{FETCHER_FI_JOB_HISTORY_SUBSECTION_TITLE}</h3>
              <p className="mt-1 text-sm text-[var(--mm-text3)]">{FETCHER_FI_JOB_HISTORY_LEAD}</p>
              <div className="mt-4">{jobHistorySlot}</div>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
