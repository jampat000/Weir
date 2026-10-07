// node --test scripts/verify-golden-path-for-release.test.mjs
// The release gate's judgement of a commit's statuses, on hand-built status objects.
import assert from "node:assert/strict";
import { test } from "node:test";

import { NO_PASSING_RUN, evaluateStatuses, latestStatus, requiresGoldenPath } from "./verify-golden-path-for-release.mjs";

let nextId = 1;
const status = (overrides = {}) => ({
  id: nextId++,
  context: "golden-path",
  state: "success",
  description: "Passed on the clean VM",
  created_at: "2026-10-07T10:00:00Z",
  ...overrides,
});

test("a success for the golden-path context passes", () => {
  assert.deepEqual(evaluateStatuses([status()]), []);
});

test("a commit with no statuses, or none for the context, does not pass", () => {
  assert.match(evaluateStatuses([]).join("\n"), /no status with the context "golden-path"/);
  assert.match(evaluateStatuses([status({ context: "ci-passed" })]).join("\n"), /no status with the context "golden-path"/);
});

test("a success under another context does not count", () => {
  assert.notDeepEqual(evaluateStatuses([status({ context: "golden-path-draft" }), status({ context: "ci-passed" })]), []);
});

test("failure, error and pending do not pass", () => {
  for (const state of ["failure", "error", "pending"]) {
    assert.match(evaluateStatuses([status({ state })]).join("\n"), new RegExp(`latest "golden-path" status is ${state}`));
  }
});

test("the failure's description is part of the reason", () => {
  assert.match(evaluateStatuses([status({ state: "failure", description: "Pause did not hold" })]).join("\n"), /Pause did not hold/);
});

test("the newest status for the context counts, whatever order the API lists them in", () => {
  const passed = status({ created_at: "2026-10-07T10:00:00Z" });
  const failedLater = status({ state: "failure", created_at: "2026-10-07T11:00:00Z" });
  assert.notDeepEqual(evaluateStatuses([failedLater, passed]), []);
  assert.notDeepEqual(evaluateStatuses([passed, failedLater]), []);

  const failedFirst = status({ state: "failure", created_at: "2026-10-07T09:00:00Z" });
  const passedLater = status({ created_at: "2026-10-07T12:00:00Z" });
  assert.deepEqual(evaluateStatuses([passedLater, failedFirst]), []);
  assert.deepEqual(evaluateStatuses([failedFirst, passedLater]), []);
});

test("two statuses made in the same second are told apart by id", () => {
  const first = status({ state: "failure" });
  const second = status();
  assert.equal(latestStatus([second, first]).id, second.id);
  assert.equal(latestStatus([first, second]).id, second.id);
  assert.deepEqual(evaluateStatuses([first, second]), []);
});

test("other contexts never hide the golden-path status", () => {
  const passed = status();
  const newerOther = status({ context: "ci-passed", state: "failure", created_at: "2026-10-07T13:00:00Z" });
  assert.deepEqual(evaluateStatuses([newerOther, passed]), []);
});

test("the refusal tells the operator what to do", () => {
  assert.equal(
    NO_PASSING_RUN,
    "This commit has no passing golden-path run. Run the golden path on this exact build (see docs/release.md), then re-run the release.",
  );
});

test("a stable tag waits for the golden path; a release candidate does not", () => {
  assert.equal(requiresGoldenPath("v1.0.0"), true);
  assert.equal(requiresGoldenPath("refs/tags/v2.3.4"), true);
  assert.equal(requiresGoldenPath("v1.0.0-rc.4"), false);
  assert.equal(requiresGoldenPath("1.1.0-beta.1"), false);
});

test("something that is not a release tag is refused rather than waved through", () => {
  assert.throws(() => requiresGoldenPath("main"), /Not a release tag/);
  assert.throws(() => requiresGoldenPath(undefined), /Not a release tag/);
});
