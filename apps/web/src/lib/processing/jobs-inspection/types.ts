import type { Schema } from "../../api/types";

/**
 * Kept by hand: the server always sends payload_json, which the schema marks optional, and older rows can
 * lack operator_message and next_action, which the schema marks required.
 */
export type ProcessingJobInspectionRow = {
  id: number;
  dedupe_key: string;
  job_kind: string;
  status: string;
  attempt_count: number;
  max_attempts: number;
  lease_owner: string | null;
  lease_expires_at: string | null;
  last_error: string | null;
  operator_message?: string;
  next_action?: string;
  technical_detail?: string | null;
  payload_json: string | null;
  created_at: string;
  updated_at: string;
};

export type ProcessingJobsInspectionOut = {
  jobs: ProcessingJobInspectionRow[];
  default_recent_slice: boolean;
};

export type ProcessingJobCancelPendingOut =
  Schema<"ProcessingJobCancelPendingOut">;
export type ProcessingJobRecoverFinalizeFailedOut =
  Schema<"ProcessingJobRecoverFinalizeFailedOut">;
