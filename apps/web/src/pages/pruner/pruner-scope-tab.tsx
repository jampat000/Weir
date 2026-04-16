import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useMeQuery } from "../../lib/auth/queries";
import { fetchPrunerPreviewRun, postPrunerPreview } from "../../lib/pruner/api";
import type { PrunerServerInstance } from "../../lib/pruner/api";

type Ctx = { instanceId: number; instance: PrunerServerInstance | undefined };

export function PrunerScopeTab(props: { scope: "tv" | "movies" }) {
  const { instanceId, instance } = useOutletContext<Ctx>();
  const me = useMeQuery();
  const qc = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const [jsonPreview, setJsonPreview] = useState<string | null>(null);
  const canOperate = me.data?.role === "admin" || me.data?.role === "operator";

  const scopeRow = instance?.scopes.find((s) => s.media_scope === props.scope);
  const label = props.scope === "tv" ? "TV (episodes)" : "Movies (one row per movie item)";
  const isPlex = instance?.provider === "plex";

  async function runPreview() {
    setErr(null);
    setBusy(true);
    setPreview(null);
    try {
      const { pruner_job_id } = await postPrunerPreview(instanceId, props.scope);
      await qc.invalidateQueries({ queryKey: ["pruner", "instances", instanceId] });
      setPreview(
        `Queued preview job #${pruner_job_id}. When the worker finishes, refresh this page — scope summary reads from denormalized fields; full candidates stay in pruner_preview_runs.`,
      );
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function loadJson() {
    const uuid = scopeRow?.last_preview_run_uuid;
    if (!uuid) {
      setErr("No preview UUID yet for this scope.");
      return;
    }
    setErr(null);
    setBusy(true);
    setJsonPreview(null);
    try {
      const run = await fetchPrunerPreviewRun(instanceId, uuid);
      setJsonPreview(run.candidates_json);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="max-w-3xl space-y-3" aria-labelledby="pruner-scope-heading">
      <h2 id="pruner-scope-heading" className="text-base font-semibold text-[var(--mm-text)]">
        {label}
      </h2>
      <p className="text-sm text-[var(--mm-text2)]">
        {props.scope === "tv"
          ? "Previews list episodes missing a primary image (episode-level rows only)."
          : "Previews list movie items missing a primary image (one candidate row per movie library item)."}
      </p>
      {isPlex ? (
        <div
          className="rounded-md border border-amber-600/40 bg-amber-950/20 px-3 py-2 text-sm text-[var(--mm-text)]"
          role="status"
        >
          Plex: missing-primary preview is <strong>not supported</strong> in this release (the API records an explicit{" "}
          <code className="text-[0.85em]">unsupported</code> outcome after the job runs). Use the Connection tab for a
          real Plex ping.
        </div>
      ) : null}
      {scopeRow ? (
        <div className="rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)] px-4 py-3 text-sm text-[var(--mm-text2)]">
          <div>Last outcome: {scopeRow.last_preview_outcome ?? "—"}</div>
          <div>Last candidate count: {scopeRow.last_preview_candidate_count ?? "—"}</div>
          <div>Last error: {scopeRow.last_preview_error ?? "—"}</div>
        </div>
      ) : null}
      {canOperate ? (
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            className="rounded-md bg-[var(--mm-accent)] px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
            disabled={busy || isPlex}
            title={isPlex ? "Preview unsupported for Plex in this release." : undefined}
            onClick={() => void runPreview()}
          >
            Queue preview job
          </button>
          <button
            type="button"
            className="rounded-md border border-[var(--mm-border)] px-3 py-1.5 text-sm font-medium text-[var(--mm-text)] disabled:opacity-50"
            disabled={busy || !scopeRow?.last_preview_run_uuid}
            onClick={() => void loadJson()}
          >
            Load candidates JSON
          </button>
        </div>
      ) : (
        <p className="text-sm text-[var(--mm-text2)]">Sign in as an operator to queue previews.</p>
      )}
      {err ? (
        <p className="text-sm text-red-600" role="alert">
          {err}
        </p>
      ) : null}
      {preview ? <p className="text-sm text-[var(--mm-text)]">{preview}</p> : null}
      {jsonPreview ? (
        <pre className="max-h-96 overflow-auto rounded-md border border-[var(--mm-border)] bg-[var(--mm-surface2)] p-3 text-xs">
          {jsonPreview}
        </pre>
      ) : null}
    </section>
  );
}
