import { describe, expect, it } from "vitest";

import { systemLogExportPath, systemLogPath } from "./system-log-api";

describe("the log's sort in a request", () => {
  it("names the sort and the direction in the list's address", () => {
    expect(systemLogPath({ sort: "workflow", direction: "asc" })).toBe(
      "/api/v1/system/log?sort=workflow&direction=asc",
    );
  });

  it("leaves them out when none is asked for", () => {
    expect(systemLogPath({ source: ["job"] })).toBe(
      "/api/v1/system/log?source=job",
    );
  });

  it("asks for the export in the same order as the list", () => {
    expect(
      systemLogExportPath("csv", { sort: "level", direction: "asc" }),
    ).toBe("/api/v1/system/log/export?sort=level&direction=asc&format=csv");
  });
});
