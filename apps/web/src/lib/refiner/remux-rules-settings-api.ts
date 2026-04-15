import { fetchCsrfToken } from "../api/auth-api";
import { apiFetch, readJson } from "../api/client";
import type { RefinerRemuxRulesSettingsOut, RefinerRemuxRulesSettingsPutBody } from "./types";

export const refinerRemuxRulesSettingsPath = () => "/api/v1/refiner/remux-rules-settings";

export async function fetchRefinerRemuxRulesSettings(): Promise<RefinerRemuxRulesSettingsOut> {
  const r = await apiFetch(refinerRemuxRulesSettingsPath());
  if (!r.ok) {
    throw new Error(`Could not load remux defaults (${r.status})`);
  }
  return readJson<RefinerRemuxRulesSettingsOut>(r);
}

export async function putRefinerRemuxRulesSettings(
  body: RefinerRemuxRulesSettingsPutBody,
): Promise<RefinerRemuxRulesSettingsOut> {
  const csrf_token = await fetchCsrfToken();
  const r = await apiFetch(refinerRemuxRulesSettingsPath(), {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...body, csrf_token }),
  });
  if (!r.ok) {
    let detail = r.statusText;
    try {
      const b = await readJson<{ detail?: string }>(r);
      if (typeof b.detail === "string") {
        detail = b.detail;
      }
    } catch {
      /* ignore */
    }
    throw new Error(detail || `Could not save remux defaults (${r.status})`);
  }
  return readJson<RefinerRemuxRulesSettingsOut>(r);
}
