import { describe, expect, it } from "vitest";
import { FAILED_IMPORT_STATUS_HANDLER_OK_FINALIZE_FAILED } from "../failed-imports/task-status-labels";
import { FETCHER_JOBS_INSPECTION_FILTER_OPTIONS } from "./filter-labels";
import type { FetcherJobsInspectionFilter } from "./queries";

function labelFor(value: FetcherJobsInspectionFilter): string | undefined {
  return FETCHER_JOBS_INSPECTION_FILTER_OPTIONS.find(
    (o: { value: FetcherJobsInspectionFilter; label: string }) => o.value === value,
  )?.label;
}

describe("FETCHER_JOBS_INSPECTION_FILTER_OPTIONS", () => {
  it("keeps needs-manual-finish filter distinct from failed", () => {
    const h = labelFor(FAILED_IMPORT_STATUS_HANDLER_OK_FINALIZE_FAILED);
    const f = labelFor("failed");
    expect(h).toBeDefined();
    expect(f).toBeDefined();
    expect(h).not.toBe(f);
    expect(h).toBe("Needs follow-up");
    expect(f).toBe("Stopped with errors");
  });

  it("uses plain Finished wording for the terminal bucket without default or storage jargon", () => {
    const t = labelFor("terminal");
    expect(t).toBe("Finished");
    expect(t!.toLowerCase()).not.toContain("default");
    expect(t!.toLowerCase()).not.toContain("handler_ok");
    expect(t!.toLowerCase()).not.toContain("storage");
  });

  it("lists every filter value exactly once", () => {
    const values = FETCHER_JOBS_INSPECTION_FILTER_OPTIONS.map(
      (o: { value: FetcherJobsInspectionFilter; label: string }) => o.value,
    );
    expect(new Set(values).size).toBe(values.length);
    expect(values).toContain("terminal");
    expect(values).toContain("pending");
    expect(values).toContain("leased");
  });
});
