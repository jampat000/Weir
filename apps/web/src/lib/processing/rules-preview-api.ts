import { fetchCsrfToken } from "../api/auth-api";
import { apiFetch, readJson, requireOk } from "../api/client";
import type { Schema } from "../api/types";

import { type ProcessingRuleSetWrite } from "./libraries-api";

export type ProcessingRulesPreviewTrack =
  Schema<"ProcessingRulesPreviewTrackOut">;
export type ProcessingRulesPreviewOriginalLanguage =
  Schema<"ProcessingRulesPreviewOriginalLanguageOut">;
export type ProcessingRulesPreviewResult = Schema<"ProcessingRulesPreviewOut">;

export type ProcessingRulesPreviewRequest = {
  libraryId: number;
  /** Exactly one of these two must be given. */
  relativePath?: string;
  absolutePath?: string;
  /** Unsaved rule edits to try; omit to preview the library's saved rule set. */
  rules?: ProcessingRuleSetWrite;
};

/** "Try on a file" (#502): read-only, never queues or writes anything. */
export async function previewProcessingRules({
  libraryId,
  relativePath,
  absolutePath,
  rules,
}: ProcessingRulesPreviewRequest): Promise<ProcessingRulesPreviewResult> {
  const csrf_token = await fetchCsrfToken();
  const path = `/api/v1/processing/libraries/${libraryId}/preview`;
  const response = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      csrf_token,
      ...(relativePath ? { relative_path: relativePath } : {}),
      ...(absolutePath ? { absolute_path: absolutePath } : {}),
      ...(rules ? { rules } : {}),
    }),
  });
  await requireOk(path, response, "Could not preview these rules on that file");
  return readJson<ProcessingRulesPreviewResult>(response);
}
