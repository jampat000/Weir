import { describe, expect, it } from "vitest";
import { FAILED_IMPORT_STATUS_HANDLER_OK_FINALIZE_FAILED } from "./task-status-labels";
import { showFailedImportManualQueuePassControl, showFetcherJobRecoverFinalizeControl } from "./eligibility";

describe("failed-import control eligibility", () => {
  it("allows manual queue pass controls for admin and operator only", () => {
    expect(showFailedImportManualQueuePassControl(undefined)).toBe(false);
    expect(showFailedImportManualQueuePassControl("viewer")).toBe(false);
    expect(showFailedImportManualQueuePassControl("admin")).toBe(true);
    expect(showFailedImportManualQueuePassControl("operator")).toBe(true);
  });

  it("allows recover only for handler_ok_finalize_failed and admin/operator", () => {
    expect(showFetcherJobRecoverFinalizeControl("admin", FAILED_IMPORT_STATUS_HANDLER_OK_FINALIZE_FAILED)).toBe(true);
    expect(showFetcherJobRecoverFinalizeControl("operator", FAILED_IMPORT_STATUS_HANDLER_OK_FINALIZE_FAILED)).toBe(
      true,
    );
    expect(showFetcherJobRecoverFinalizeControl("admin", "failed")).toBe(false);
    expect(showFetcherJobRecoverFinalizeControl("viewer", FAILED_IMPORT_STATUS_HANDLER_OK_FINALIZE_FAILED)).toBe(false);
  });
});
