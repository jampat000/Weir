import { describe, expect, it } from "vitest";
import { jobInspectionDetailsForOperator } from "./job-inspection-details";

describe("jobInspectionDetailsForOperator", () => {
  it("maps Sonarr connection errors to friendly copy and keeps technical behind", () => {
    const r = jobInspectionDetailsForOperator(
      "HTTP 401 from Sonarr at http://localhost:8989",
      "fetcher.sonarr_failed_import_cleanup.v1",
    );
    expect(r.friendly).toMatch(/Sonarr/i);
    expect(r.friendly).toMatch(/connection details needed to run cleanup/i);
    expect(r.technical).toContain("401");
  });

  it("returns dash when no error", () => {
    const r = jobInspectionDetailsForOperator(null, "any");
    expect(r.friendly).toBe("—");
    expect(r.technical).toBeNull();
  });
});
