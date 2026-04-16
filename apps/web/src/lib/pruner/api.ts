import { apiFetch, apiErrorDetailToString, readJson } from "../api/client";
import { fetchCsrfToken } from "../api/auth-api";

export type PrunerScopeSummary = {
  media_scope: string;
  missing_primary_media_reported_enabled: boolean;
  preview_max_items: number;
  last_preview_run_uuid: string | null;
  last_preview_at: string | null;
  last_preview_candidate_count: number | null;
  last_preview_outcome: string | null;
  last_preview_error: string | null;
};

export type PrunerServerInstance = {
  id: number;
  provider: string;
  display_name: string;
  base_url: string;
  enabled: boolean;
  last_connection_test_at: string | null;
  last_connection_test_ok: boolean | null;
  last_connection_test_detail: string | null;
  scopes: PrunerScopeSummary[];
};

export type PrunerPreviewRun = {
  preview_run_id: string;
  server_instance_id: number;
  media_scope: string;
  rule_family_id: string;
  candidate_count: number;
  truncated: boolean;
  outcome: string;
  unsupported_detail: string | null;
  error_message: string | null;
  created_at: string;
  candidates_json: string;
};

export async function fetchPrunerInstances(): Promise<PrunerServerInstance[]> {
  const r = await apiFetch("/api/v1/pruner/instances");
  if (!r.ok) {
    throw new Error(`Pruner instances: ${r.status}`);
  }
  return readJson<PrunerServerInstance[]>(r);
}

export async function fetchPrunerInstance(id: number): Promise<PrunerServerInstance> {
  const r = await apiFetch(`/api/v1/pruner/instances/${id}`);
  if (!r.ok) {
    throw new Error(`Pruner instance: ${r.status}`);
  }
  return readJson<PrunerServerInstance>(r);
}

export async function fetchPrunerPreviewRun(
  instanceId: number,
  previewRunId: string,
): Promise<PrunerPreviewRun> {
  const r = await apiFetch(`/api/v1/pruner/instances/${instanceId}/preview-runs/${previewRunId}`);
  if (!r.ok) {
    throw new Error(`Pruner preview run: ${r.status}`);
  }
  return readJson<PrunerPreviewRun>(r);
}

export async function postPrunerConnectionTest(instanceId: number): Promise<{ pruner_job_id: number }> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(`/api/v1/pruner/instances/${instanceId}/connection-test`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ csrf_token }),
  });
  if (!r.ok) {
    const body = (await readJson<{ detail?: unknown }>(r).catch(() => ({}))) as { detail?: unknown };
    throw new Error(apiErrorDetailToString(body.detail) || `connection test: ${r.status}`);
  }
  return readJson<{ pruner_job_id: number }>(r);
}

export async function postPrunerPreview(
  instanceId: number,
  media_scope: "tv" | "movies",
): Promise<{ pruner_job_id: number }> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(`/api/v1/pruner/instances/${instanceId}/previews`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ media_scope, csrf_token }),
  });
  if (!r.ok) {
    const body = (await readJson<{ detail?: unknown }>(r).catch(() => ({}))) as { detail?: unknown };
    throw new Error(apiErrorDetailToString(body.detail) || `preview: ${r.status}`);
  }
  return readJson<{ pruner_job_id: number }>(r);
}
