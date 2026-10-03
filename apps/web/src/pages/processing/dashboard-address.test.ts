import { describe, expect, it } from "vitest";

import {
  filterFromSearch,
  resolveWorkflow,
  viewFromSearch,
  workflowFromSearch,
} from "./dashboard-address";

const params = (query: string) => new URLSearchParams(query);

describe("viewFromSearch", () => {
  it("opens Live unless the address asks for System", () => {
    expect(viewFromSearch(params(""))).toBe("live");
    expect(viewFromSearch(params("view=live"))).toBe("live");
    expect(viewFromSearch(params("view=nonsense"))).toBe("live");
    expect(viewFromSearch(params("view=system"))).toBe("system");
  });
});

describe("filterFromSearch", () => {
  it("narrows to the kind of work the address names", () => {
    expect(filterFromSearch(params("work=download"))).toBe("download");
    expect(filterFromSearch(params("work=library"))).toBe("library");
  });

  it("shows everything when the address names nothing it knows", () => {
    for (const other of ["", "all", "nonsense", "Library"]) {
      expect(filterFromSearch(params(`work=${other}`))).toBe("all");
    }
    expect(filterFromSearch(params(""))).toBe("all");
  });
});

describe("workflowFromSearch", () => {
  it("reads a whole workflow id", () => {
    expect(workflowFromSearch(params("workflow=7"))).toBe(7);
  });

  it("ignores anything that is not a whole id", () => {
    for (const bad of ["", "0", "-2", "1.5", "abc", "3x", "12345678901"]) {
      expect(workflowFromSearch(params(`workflow=${bad}`))).toBeNull();
    }
    expect(workflowFromSearch(params(""))).toBeNull();
  });
});

describe("resolveWorkflow", () => {
  const known = [{ id: 1 }, { id: 2 }];

  it("keeps a workflow that is on the list", () => {
    expect(resolveWorkflow(2, known)).toBe(2);
  });

  it("shows every workflow for one that is not on the list", () => {
    expect(resolveWorkflow(9, known)).toBeNull();
  });

  it("keeps the link's choice while the list is still loading", () => {
    expect(resolveWorkflow(9, undefined)).toBe(9);
  });

  it("has nothing to resolve when no workflow was asked for", () => {
    expect(resolveWorkflow(null, known)).toBeNull();
  });
});
