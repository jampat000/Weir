import { apiFetch, readJson } from "../api/client";
import type { RefinerRuntimeSettingsOut } from "./types";

export const refinerRuntimeSettingsPath = () => "/api/v1/refiner/runtime-settings";

export async function fetchRefinerRuntimeSettings(): Promise<RefinerRuntimeSettingsOut> {
  const r = await apiFetch(refinerRuntimeSettingsPath());
  if (!r.ok) {
    throw new Error(`Could not load Refiner runtime settings (${r.status})`);
  }
  return readJson<RefinerRuntimeSettingsOut>(r);
}
