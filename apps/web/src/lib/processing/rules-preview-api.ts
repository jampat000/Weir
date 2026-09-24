import { sendJson } from "../api/send-json";
import { readJson } from "../api/client";
import type { Schema } from "../api/types";

import { type ProcessingRuleSetWrite } from "./rule-sets-api";

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
  const path = `/api/v1/processing/libraries/${libraryId}/preview`;
  const response = await sendJson(
    path,
    "POST",
    {
      ...(relativePath ? { relative_path: relativePath } : {}),
      ...(absolutePath ? { absolute_path: absolutePath } : {}),
      ...(rules ? { rules } : {}),
    },
    "Could not preview these rules on that file",
  );
  return readJson<ProcessingRulesPreviewResult>(response);
}
