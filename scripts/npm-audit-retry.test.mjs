// node --test scripts/npm-audit-retry.test.mjs
import assert from "node:assert/strict";
import { test } from "node:test";

import { auditWithRetry, classifyAuditOutput, meetsAuditLevel, RegistryUnavailableError, unapprovedAdvisories } from "./npm-audit-retry.mjs";

const CLEAN_REPORT = { metadata: { vulnerabilities: { info: 0, low: 0, moderate: 0, high: 0, critical: 0 } } };
const HIGH_REPORT = { metadata: { vulnerabilities: { info: 0, low: 2, moderate: 0, high: 1, critical: 0 } } };

test("a normal audit report is not transient", () => {
  const outcome = classifyAuditOutput({ stdout: JSON.stringify(CLEAN_REPORT), stderr: "" });
  assert.equal(outcome.transient, false);
  assert.deepEqual(outcome.report, CLEAN_REPORT);
});

test("output that is not JSON is treated as a registry problem", () => {
  const outcome = classifyAuditOutput({ stdout: "npm ERR! network timeout", stderr: "" });
  assert.equal(outcome.transient, true);
});

test("npm's own audit-endpoint error envelope is a registry problem", () => {
  const stdout = JSON.stringify({ error: { code: "E503", summary: "audit endpoint returned an error" } });
  const outcome = classifyAuditOutput({ stdout, stderr: "" });
  assert.equal(outcome.transient, true);
});

test("an unrecognized error envelope is not assumed to be transient", () => {
  const stdout = JSON.stringify({ error: { code: "EACCES", summary: "permission denied" } });
  const outcome = classifyAuditOutput({ stdout, stderr: "" });
  assert.equal(outcome.transient, false);
});

test("meetsAuditLevel fails only at or above the requested severity", () => {
  assert.equal(meetsAuditLevel(HIGH_REPORT, "high"), true);
  assert.equal(meetsAuditLevel(HIGH_REPORT, "critical"), false);
  assert.equal(meetsAuditLevel(HIGH_REPORT, "low"), true);
  assert.equal(meetsAuditLevel(CLEAN_REPORT, "low"), false);
});

test("a registry error is retried and a later clean run succeeds", async () => {
  let calls = 0;
  const run = () => {
    calls += 1;
    return calls < 3 ? { stdout: "npm ERR! network timeout", stderr: "" } : { stdout: JSON.stringify(CLEAN_REPORT), stderr: "" };
  };
  const waits = [];
  const report = await auditWithRetry({ cwd: ".", retries: 3, baseDelayMs: 10, run, wait: async (ms) => waits.push(ms) });
  assert.deepEqual(report, CLEAN_REPORT);
  assert.equal(calls, 3);
  assert.equal(waits.length, 2);
});

test("a real vulnerability report is never retried", async () => {
  let calls = 0;
  const run = () => {
    calls += 1;
    return { stdout: JSON.stringify(HIGH_REPORT), stderr: "" };
  };
  const report = await auditWithRetry({ cwd: ".", retries: 3, baseDelayMs: 10, run, wait: async () => {} });
  assert.deepEqual(report, HIGH_REPORT);
  assert.equal(calls, 1);
});

test("a registry that never recovers fails with a message that blames the registry", async () => {
  const run = () => ({ stdout: "npm ERR! network timeout", stderr: "" });
  await assert.rejects(
    auditWithRetry({ cwd: ".", retries: 2, baseDelayMs: 10, run, wait: async () => {} }),
    (error) => error instanceof RegistryUnavailableError && /3 attempt/.test(error.message),
  );
});

const ADVISORY_REPORT = {
  vulnerabilities: {
    braces: { via: [{ url: "https://github.com/advisories/GHSA-aaaa-bbbb-cccc", severity: "high" }] },
    micromatch: { via: ["braces"] },
    tiny: { via: [{ url: "https://github.com/advisories/GHSA-low0-0000-0000", severity: "low" }] },
  },
};
const CURRENT = { advisory: "GHSA-aaaa-bbbb-cccc", expires: "2026-11-02", mitigation: "dev only" };

test("an advisory with no exception is reported", () => {
  assert.deepEqual(unapprovedAdvisories(ADVISORY_REPORT, { level: "high", today: "2026-10-03" }), ["braces: GHSA-aaaa-bbbb-cccc"]);
});

test("a current exception with a mitigation approves its advisory", () => {
  assert.deepEqual(unapprovedAdvisories(ADVISORY_REPORT, { exceptions: [CURRENT], level: "high", today: "2026-10-03" }), []);
});

test("an expired exception approves nothing", () => {
  const [finding] = unapprovedAdvisories(ADVISORY_REPORT, { exceptions: [CURRENT], level: "high", today: "2026-11-03" });
  assert.match(finding, /expired 2026-11-02/);
});

test("an exception without a mitigation approves nothing", () => {
  const [finding] = unapprovedAdvisories(ADVISORY_REPORT, { exceptions: [{ ...CURRENT, mitigation: "" }], level: "high", today: "2026-10-03" });
  assert.match(finding, /no mitigation/);
});

test("advisories below the level are left out, and at level info every one counts", () => {
  assert.equal(unapprovedAdvisories(ADVISORY_REPORT, { exceptions: [CURRENT], level: "high", today: "2026-10-03" }).length, 0);
  assert.deepEqual(unapprovedAdvisories(ADVISORY_REPORT, { exceptions: [CURRENT], level: "info", today: "2026-10-03" }), ["tiny: GHSA-low0-0000-0000"]);
});

test("an advisory of unknown severity is never skipped", () => {
  const report = { vulnerabilities: { odd: { via: [{ url: "https://github.com/advisories/GHSA-odd0-0000-0000" }] } } };
  assert.deepEqual(unapprovedAdvisories(report, { level: "critical", today: "2026-10-03" }), ["odd: GHSA-odd0-0000-0000"]);
});
