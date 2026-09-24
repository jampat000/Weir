import { sendJson } from "../api/send-json";
import { apiFetch, readJson, requireOk } from "../api/client";
import type {
  ProcessingOperatorSettingsOut,
  ProcessingOperatorSettingsPutBody,
} from "./types";

export const processingOperatorSettingsPath = () =>
  "/api/v1/processing/operator-settings";

export async function fetchProcessingOperatorSettings(): Promise<ProcessingOperatorSettingsOut> {
  const path = processingOperatorSettingsPath();
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load processing settings");
  return readJson<ProcessingOperatorSettingsOut>(r);
}

export async function putProcessingOperatorSettings(
  body: ProcessingOperatorSettingsPutBody,
): Promise<ProcessingOperatorSettingsOut> {
  const path = processingOperatorSettingsPath();
  const r = await sendJson(
    path,
    "PUT",
    body,
    "Could not save processing settings",
  );
  return readJson<ProcessingOperatorSettingsOut>(r);
}
