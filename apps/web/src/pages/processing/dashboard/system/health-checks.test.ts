import { describe, expect, it } from "vitest";

import type { ConnectionEntry } from "../../../../lib/connections/connection-model";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import type { SystemDrive } from "../../../../lib/system/system-stats-types";
import type { ToolRow } from "../health-model";
import type { WorkflowHealth } from "../use-health";
import {
  backupChecks,
  connectionChecks,
  storageChecks,
  toolChecks,
  weirChecks,
  workflowChecks,
} from "./health-checks";

const NOW = Date.parse("2026-10-02T10:00:00Z");
const HOUR = 3_600_000;

function workflowHealth(
  overrides: Partial<WorkflowHealth> & { id: number; name: string },
): WorkflowHealth {
  const { id, name, ...rest } = overrides;
  return {
    workflow: { id, name } as ProcessingLibrary,
    verdict: { words: "In sync", tone: "healthy" },
    why: null,
    chain: undefined,
    checkedAt: NOW,
    recheck: async () => undefined,
    ...rest,
  };
}

function connection(
  overrides: Partial<ConnectionEntry> & { key: string },
): ConnectionEntry {
  return {
    kind: "media_manager",
    id: 1,
    name: "Radarr",
    baseName: "Radarr",
    nickname: "",
    kindLabel: "Radarr",
    address: "http://radarr",
    enabled: true,
    state: "ok",
    checkedAt: NOW,
    answerMs: 84,
    detail: "",
    testable: true,
    ...overrides,
  };
}

describe("workflowChecks", () => {
  it("passes a workflow whose chain is in sync", () => {
    const [check] = workflowChecks([workflowHealth({ id: 2, name: "Movies" })]);
    expect(check).toMatchObject({
      tone: "ok",
      title: "Movies",
      fix: null,
      workflowId: 2,
      again: { area: "workflows", key: "2" },
    });
  });

  it("asks for a fix on a chain that needs one, with the reason and a link to the workflow", () => {
    const [check] = workflowChecks([
      workflowHealth({
        id: 2,
        name: "Movies",
        verdict: { words: "Needs a fix", tone: "warning" },
        why: "The output folder is missing.",
      }),
    ]);
    expect(check).toMatchObject({
      tone: "warn",
      title: "Movies needs a fix",
      why: "The output folder is missing.",
    });
    expect(check.fix?.to).toBe("/settings?tab=libraries&edit=2");
  });

  it("leaves a chain that is only being checked as neither passing nor failing", () => {
    const [check] = workflowChecks([
      workflowHealth({
        id: 2,
        name: "Movies",
        verdict: { words: "Checking…", tone: "neutral" },
      }),
    ]);
    expect(check).toMatchObject({ tone: "idle", why: "Checking…" });
  });
});

describe("connectionChecks", () => {
  it("fails a connection that is not answering, with what its test said", () => {
    const [check] = connectionChecks([
      connection({ key: "a", state: "down", detail: "Connection refused." }),
    ]);
    expect(check).toMatchObject({
      tone: "bad",
      title: "Radarr isn't answering",
      why: "Connection refused.",
    });
    expect(check.fix?.to).toBe("/settings?tab=media-managers");
  });

  it("warns on a connection that is slow, with how long it took", () => {
    const [check] = connectionChecks([
      connection({ key: "a", state: "slow", answerMs: 2500 }),
    ]);
    expect(check).toMatchObject({
      tone: "warn",
      why: "Its last answer came in 2,500 ms.",
    });
  });

  it("passes an answering connection and leaves an untested one open", () => {
    const [ok, untested] = connectionChecks([
      connection({ key: "a" }),
      connection({ key: "b", state: "untested" }),
    ]);
    expect(ok.tone).toBe("ok");
    expect(untested.tone).toBe("idle");
  });

  it("leaves out a connection that is switched off", () => {
    expect(
      connectionChecks([
        connection({ key: "a", enabled: false, state: "off" }),
      ]),
    ).toEqual([]);
  });

  it("looks again at the one connection", () => {
    const [check] = connectionChecks([connection({ key: "media_manager:1" })]);
    expect(check.again).toEqual({
      area: "connections",
      key: "media_manager:1",
    });
  });
});

describe("toolChecks", () => {
  const ffmpeg = (tone: ToolRow["tone"]): ToolRow => ({
    key: "ffmpeg",
    name: "FFmpeg",
    version: "7.1",
    banner: "ffmpeg version 7.1",
    tone,
  });

  it("fails when FFmpeg is missing", () => {
    const [check] = toolChecks([ffmpeg("failed")], NOW);
    expect(check).toMatchObject({ tone: "bad", title: "FFmpeg is missing" });
  });

  it("only notes that the optional mkvmerge is missing", () => {
    const [check] = toolChecks(
      [{ ...ffmpeg("neutral"), key: "mkvmerge", name: "mkvmerge" }],
      NOW,
    );
    expect(check.tone).toBe("note");
  });

  it("passes a tool that is installed and says its version line", () => {
    const [check] = toolChecks([ffmpeg("healthy")], NOW);
    expect(check).toMatchObject({ tone: "ok", why: "ffmpeg version 7.1" });
  });

  it("has no checks while the tools have not answered", () => {
    expect(toolChecks(null, null)).toEqual([]);
  });
});

describe("storageChecks", () => {
  function drive(overrides: Partial<SystemDrive>): SystemDrive {
    return {
      name: "D:",
      path: "D:\\",
      total_bytes: 2_000_000_000_000,
      free_bytes: 1_500_000_000_000,
      weir_bytes: 0,
      keep_free_bytes: 20_000_000_000,
      full_in_days: null,
      read_bytes_per_sec: null,
      write_bytes_per_sec: null,
      busy_percent: null,
      workflows: [{ id: 3, name: "Movies", roles: ["output"] }],
      ...overrides,
    };
  }

  it("fails a drive with less free than Weir keeps free, and links to a workflow that writes there", () => {
    const [check] = storageChecks([drive({ free_bytes: 5_000_000_000 })], NOW);
    expect(check).toMatchObject({ tone: "bad", title: "D: is low on space" });
    expect(check.fix?.to).toBe("/settings?tab=libraries&edit=3");
  });

  it("warns on a drive that fills within a week", () => {
    const [check] = storageChecks([drive({ full_in_days: 3 })], NOW);
    expect(check).toMatchObject({ tone: "warn", title: "D: fills up soon" });
    expect(check.why).toContain("about 3 days");
  });

  it("passes a drive with room, and says how much", () => {
    const [check] = storageChecks([drive({})], NOW);
    expect(check.tone).toBe("ok");
    expect(check.why).toMatch(/free\.$/);
  });

  it("has no checks while the drives have not been read", () => {
    expect(storageChecks(null, null)).toEqual([]);
  });
});

describe("backupChecks", () => {
  const facts = {
    enabled: true,
    intervalHours: 24,
    lastBackupAt: NOW - HOUR,
    checkedAt: NOW,
  };

  it("passes while the newest backup is within the schedule", () => {
    expect(backupChecks(facts, NOW)[0].tone).toBe("ok");
  });

  it("warns when automatic backups are off", () => {
    const [check] = backupChecks({ ...facts, enabled: false }, NOW);
    expect(check).toMatchObject({ tone: "warn" });
    expect(check.fix?.to).toBe("/system?tab=backups");
  });

  it("warns when no backup has been made", () => {
    const [check] = backupChecks({ ...facts, lastBackupAt: null }, NOW);
    expect(check.why).toBe("No backup has been made yet.");
  });

  it("warns when the newest backup is older than two intervals", () => {
    const [check] = backupChecks(
      { ...facts, lastBackupAt: NOW - 49 * HOUR },
      NOW,
    );
    expect(check.tone).toBe("warn");
  });

  it("has no check before the settings have loaded", () => {
    expect(backupChecks(null, NOW)).toEqual([]);
  });
});

describe("weirChecks", () => {
  it("fails when a worker has stopped and says what it reported", () => {
    const [check] = weirChecks({
      stoppedWorkers: ["The cleanup worker stopped."],
      updateVersion: null,
      checkedAt: NOW,
    });
    expect(check).toMatchObject({
      tone: "bad",
      why: "The cleanup worker stopped.",
    });
  });

  it("passes when every worker runs, and notes a newer version without counting it", () => {
    const checks = weirChecks({
      stoppedWorkers: [],
      updateVersion: "3.3.0",
      checkedAt: NOW,
    });
    expect(checks.map((check) => check.tone)).toEqual(["ok", "note"]);
    expect(checks[1].title).toBe("Weir 3.3.0 is out");
  });

  it("has no checks before readiness has answered", () => {
    expect(weirChecks(null)).toEqual([]);
  });
});
