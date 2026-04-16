import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useMeQuery } from "../../lib/auth/queries";
import { postPrunerConnectionTest } from "../../lib/pruner/api";
import type { PrunerServerInstance } from "../../lib/pruner/api";

type Ctx = { instanceId: number; instance: PrunerServerInstance | undefined };

export function PrunerConnectionTab() {
  const { instanceId, instance } = useOutletContext<Ctx>();
  const me = useMeQuery();
  const qc = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const canOperate = me.data?.role === "admin" || me.data?.role === "operator";

  async function runTest() {
    setErr(null);
    setMsg(null);
    setBusy(true);
    try {
      const { pruner_job_id } = await postPrunerConnectionTest(instanceId);
      await qc.invalidateQueries({ queryKey: ["pruner", "instances", instanceId] });
      setMsg(`Queued connection test job #${pruner_job_id}.`);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="max-w-3xl space-y-3" aria-labelledby="pruner-conn-heading">
      <h2 id="pruner-conn-heading" className="text-base font-semibold text-[var(--mm-text)]">
        Connection test
      </h2>
      <p className="text-sm text-[var(--mm-text2)]">
        Emby/Jellyfin: minimal <code className="text-[0.85em]">System/Info/Public</code> ping. Plex:{" "}
        <code className="text-[0.85em]">GET /identity</code> with optional token.
      </p>
      {instance ? (
        <div className="rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)] px-4 py-3 text-sm text-[var(--mm-text2)]">
          <div>Last test: {instance.last_connection_test_at ?? "never"}</div>
          <div>OK: {instance.last_connection_test_ok === null ? "—" : instance.last_connection_test_ok ? "yes" : "no"}</div>
          <div>Detail: {instance.last_connection_test_detail ?? "—"}</div>
        </div>
      ) : null}
      {canOperate ? (
        <button
          type="button"
          className="rounded-md bg-[var(--mm-accent)] px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
          disabled={busy}
          onClick={() => void runTest()}
        >
          Queue connection test
        </button>
      ) : (
        <p className="text-sm text-[var(--mm-text2)]">Sign in as an operator to run connection tests.</p>
      )}
      {err ? (
        <p className="text-sm text-red-600" role="alert">
          {err}
        </p>
      ) : null}
      {msg ? <p className="text-sm text-[var(--mm-text)]">{msg}</p> : null}
    </section>
  );
}
