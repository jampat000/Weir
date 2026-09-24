import type { ReactNode } from "react";

import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import type { MaintenanceFamilyState } from "../../../../lib/processing/maintenance-api";
import type { CleanupJob } from "./cleanup-jobs";

/** Switching a destructive job on, or running it now: both delete files once they run. */
export type CleanupConfirmAction = "enable" | "run";

function confirmTitle(job: CleanupJob, action: CleanupConfirmAction): string {
  return action === "enable"
    ? `Switch on "${job.name}"?`
    : `Run "${job.name}" now?`;
}

function confirmLabel(action: CleanupConfirmAction): string {
  return action === "enable" ? "Switch on" : "Run now";
}

/** What a destructive job's confirm dialog asks, built from the job's own description. */
export function CleanupConfirmDialog({
  job,
  state,
  action,
  onCancel,
  onConfirm,
}: {
  job: CleanupJob;
  state: MaintenanceFamilyState;
  action: CleanupConfirmAction;
  onCancel: () => void;
  onConfirm: () => void;
}): ReactNode {
  return (
    <ConfirmDialog
      testId={`processing-maintenance-confirm-${job.family}-${action}`}
      title={confirmTitle(job, action)}
      description={
        <p>{state.description} Files it deletes cannot be recovered.</p>
      }
      confirmLabel={confirmLabel(action)}
      onCancel={onCancel}
      onConfirm={onConfirm}
    />
  );
}
