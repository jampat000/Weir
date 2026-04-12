import { useState } from "react";
import { PageLoading } from "../../components/shared/page-loading";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { useMeQuery } from "../../lib/auth/queries";
import {
  useRefinerPathSettingsQuery,
  useRefinerWatchedFolderRemuxScanDispatchEnqueueMutation,
} from "../../lib/refiner/queries";

function canTriggerRefinerJobs(role: string | undefined): boolean {
  return role === "operator" || role === "admin";
}

/** Manual watched-folder scan on ``refiner_jobs`` (classify; optional remux enqueue). */
export function RefinerWatchedFolderScanSection() {
  const me = useMeQuery();
  const paths = useRefinerPathSettingsQuery();
  const enqueueScan = useRefinerWatchedFolderRemuxScanDispatchEnqueueMutation();

  const [alsoEnqueueRemux, setAlsoEnqueueRemux] = useState(false);
  const [remuxDryRun, setRemuxDryRun] = useState(true);

  const canTrigger = canTriggerRefinerJobs(me.data?.role);
  const watchedSet = Boolean((paths.data?.refiner_watched_folder ?? "").trim());
  const outputSet = Boolean((paths.data?.refiner_output_folder ?? "").trim());
  const liveRemuxRequested = alsoEnqueueRemux && !remuxDryRun;
  const missingLivePrereq = liveRemuxRequested && !outputSet;

  if (paths.isPending || me.isPending) {
    return <PageLoading label="Loading Refiner path settings" />;
  }
  if (paths.isError) {
    return (
      <div
        className="mt-6 max-w-2xl rounded border border-red-900/40 bg-red-950/20 p-4 text-sm text-red-200"
        data-testid="refiner-watched-folder-scan-path-error"
        role="alert"
      >
        <p className="font-semibold">Could not load path settings for watched-folder scan</p>
        <p className="mt-1">
          {isLikelyNetworkFailure(paths.error)
            ? "Check that the MediaMop API is running."
            : isHttpErrorFromApi(paths.error)
              ? "Sign in, then try again."
              : "Request failed."}
        </p>
      </div>
    );
  }

  return (
    <section
      className="mt-6 max-w-2xl rounded border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-4 text-sm leading-relaxed text-[var(--mm-text2)]"
      aria-labelledby="refiner-watched-folder-scan-heading"
      data-testid="refiner-watched-folder-scan-section"
    >
      <h2 id="refiner-watched-folder-scan-heading" className="text-base font-semibold text-[var(--mm-text)]">
        Watched-folder remux scan (manual)
      </h2>
      <p className="mt-2">
        Queues one <code className="rounded bg-black/25 px-1 py-0.5 font-mono text-[0.85em]">
          refiner.watched_folder.remux_scan_dispatch.v1
        </code>{" "}
        job on <code className="rounded bg-black/25 px-1 py-0.5 font-mono text-[0.85em]">refiner_jobs</code>. The
        worker walks the <strong className="text-[var(--mm-text)]">saved watched folder</strong> for supported media
        files, applies the same Refiner ownership and upstream blocking rules as the candidate gate (Radarr and Sonarr
        download queues combined), then writes one Activity summary: scanned, skipped (non-media), waiting on upstream,
        and enqueued or skipped remux. There is <strong className="text-[var(--mm-text)]">no</strong> periodic schedule
        for this family in the first version — only this manual trigger.
      </p>
      <p className="mt-2 text-[var(--mm-text3)]">
        By default the scan only classifies files. Optional checkboxes enqueue{" "}
        <code className="rounded bg-black/25 px-1 font-mono text-[0.8em]">refiner.file.remux_pass.v1</code> rows for
        files that may proceed; remux defaults stay dry-run unless you explicitly choose live remux (requires a saved
        output folder). Files that already have a pending or in-flight remux pass for the same relative path are not
        enqueued again from the same scan.
      </p>

      {!canTrigger ? (
        <p className="mt-3 text-xs text-[var(--mm-text3)]">Operators and admins can queue this scan.</p>
      ) : null}

      {!watchedSet ? (
        <p className="mt-3 text-sm text-amber-200/90" role="status">
          Save a <strong className="text-[var(--mm-text)]">watched folder</strong> in Refiner path settings before
          queuing a scan.
        </p>
      ) : null}

      <div className="mt-4 space-y-2">
        <label className="flex cursor-pointer items-start gap-2">
          <input
            type="checkbox"
            className="mt-1"
            checked={alsoEnqueueRemux}
            disabled={!canTrigger || enqueueScan.isPending || !watchedSet}
            onChange={(e) => setAlsoEnqueueRemux(e.target.checked)}
          />
          <span>
            Also enqueue <code className="font-mono text-[0.85em]">refiner.file.remux_pass.v1</code> for eligible files
            (ownership OK, not blocked by active upstream work).
          </span>
        </label>
        {alsoEnqueueRemux ? (
          <label className="ml-6 flex cursor-pointer items-start gap-2">
            <input
              type="checkbox"
              className="mt-1"
              checked={remuxDryRun}
              disabled={!canTrigger || enqueueScan.isPending || !watchedSet}
              onChange={(e) => setRemuxDryRun(e.target.checked)}
            />
            <span>
              Remux passes are <strong className="text-[var(--mm-text)]">dry run</strong> (uncheck for live remux —
              requires saved output folder; same deletion and replacement rules as manual remux).
            </span>
          </label>
        ) : null}
      </div>

      {missingLivePrereq ? (
        <p className="mt-3 text-sm text-amber-200/90" role="status">
          Live remux enqueue needs a saved <strong className="text-[var(--mm-text)]">output folder</strong> in path
          settings.
        </p>
      ) : null}

      {enqueueScan.isError ? (
        <p className="mt-3 text-sm text-red-300" role="alert" data-testid="refiner-watched-folder-scan-enqueue-error">
          {enqueueScan.error instanceof Error ? enqueueScan.error.message : "Enqueue failed."}
        </p>
      ) : null}

      {enqueueScan.isSuccess ? (
        <p className="mt-3 text-xs text-[var(--mm-text3)]" data-testid="refiner-watched-folder-scan-enqueued-hint">
          Queued scan job #{enqueueScan.data.job_id}. When workers run, check Overview → Activity for the summary.
        </p>
      ) : null}

      <div className="mt-4">
        <button
          type="button"
          className="rounded bg-[var(--mm-accent)] px-3 py-1.5 text-sm font-medium text-[var(--mm-accent-contrast)] disabled:opacity-50"
          disabled={
            !canTrigger ||
            !watchedSet ||
            enqueueScan.isPending ||
            missingLivePrereq
          }
          onClick={() =>
            enqueueScan.mutate({
              enqueue_remux_jobs: alsoEnqueueRemux,
              remux_dry_run: remuxDryRun,
            })
          }
        >
          {enqueueScan.isPending ? "Queuing…" : "Queue watched-folder scan"}
        </button>
      </div>
    </section>
  );
}
