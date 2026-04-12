import { PageLoading } from "../../components/shared/page-loading";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { useRefinerRuntimeSettingsQuery } from "../../lib/refiner/queries";

export function RefinerRuntimeSettingsSection() {
  const q = useRefinerRuntimeSettingsQuery();

  if (q.isPending) {
    return <PageLoading label="Loading Refiner worker settings" />;
  }
  if (q.isError) {
    return <RefinerRuntimeSettingsError err={q.error} />;
  }
  const d = q.data;
  return (
    <section
      className="mt-6 max-w-2xl rounded border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-4 text-sm leading-relaxed text-[var(--mm-text2)]"
      aria-labelledby="refiner-runtime-settings-heading"
      data-testid="refiner-runtime-settings"
    >
      <h2 id="refiner-runtime-settings-heading" className="text-base font-semibold text-[var(--mm-text)]">
        In-process Refiner workers
      </h2>
      <p className="mt-2">
        This API process starts{" "}
        <strong className="text-[var(--mm-text)]">{d.in_process_refiner_worker_count}</strong> in-process Refiner
        worker task{d.in_process_refiner_worker_count === 1 ? "" : "s"} for{" "}
        <code className="rounded bg-black/25 px-1 py-0.5 font-mono text-[0.85em] text-[var(--mm-text)]">
          refiner_jobs
        </code>{" "}
        only. Other module lanes use separate worker counts.
      </p>
      <p className="mt-2">{d.worker_mode_summary}</p>
      <p className="mt-2 text-[var(--mm-text3)]">{d.sqlite_throughput_note}</p>
      <p className="mt-2 text-[var(--mm-text3)]">{d.configuration_note}</p>
      <p className="mt-2 text-xs text-[var(--mm-text3)]">{d.visibility_note}</p>

      <h3 className="mt-6 text-sm font-semibold text-[var(--mm-text)]">Watched-folder scan periodic enqueue</h3>
      <p className="mt-2 text-[var(--mm-text3)]">{d.watched_folder_scan_periodic_configuration_note}</p>
      <ul className="mt-2 list-disc space-y-1 pl-5 text-xs text-[var(--mm-text2)]">
        <li>
          Schedule enabled (env at startup):{" "}
          <span className="font-mono">{String(d.refiner_watched_folder_remux_scan_dispatch_schedule_enabled)}</span>
        </li>
        <li>
          Interval seconds:{" "}
          <span className="font-mono">{d.refiner_watched_folder_remux_scan_dispatch_schedule_interval_seconds}</span>
        </li>
        <li>
          Periodic enqueue remux jobs:{" "}
          <span className="font-mono">
            {String(d.refiner_watched_folder_remux_scan_dispatch_periodic_enqueue_remux_jobs)}
          </span>
        </li>
        <li>
          Periodic remux dry run:{" "}
          <span className="font-mono">
            {String(d.refiner_watched_folder_remux_scan_dispatch_periodic_remux_dry_run)}
          </span>
        </li>
      </ul>
      <p className="mt-2 text-xs text-[var(--mm-text3)]">
        This is an asyncio interval that enqueues <code className="font-mono text-[0.85em]">refiner.watched_folder.remux_scan_dispatch.v1</code>{" "}
        when idle — not a filesystem watcher. Activity summaries include{" "}
        <code className="font-mono text-[0.85em]">scan_trigger</code> (<code className="font-mono">manual</code> vs{" "}
        <code className="font-mono">periodic</code>).
      </p>
    </section>
  );
}

function RefinerRuntimeSettingsError({ err }: { err: unknown }) {
  return (
    <div
      className="mt-6 max-w-2xl rounded border border-red-900/40 bg-red-950/20 p-4 text-sm text-red-200"
      data-testid="refiner-runtime-settings-error"
      role="alert"
    >
      <p className="font-semibold">Could not load Refiner worker settings</p>
      <p className="mt-1">
        {isLikelyNetworkFailure(err)
          ? "Check that the MediaMop API is running."
          : isHttpErrorFromApi(err)
            ? "Sign in as an operator or admin, then try again."
            : "Request failed."}
      </p>
      {err instanceof Error ? <p className="mt-1 font-mono text-xs opacity-90">{err.message}</p> : null}
    </div>
  );
}
