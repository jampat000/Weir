// node --test scripts/verify-ci-for-release.test.mjs
// The release gate's judgement of one CI run, on hand-built run and job objects.
import assert from "node:assert/strict";
import { test } from "node:test";

import { evaluateRun } from "./verify-ci-for-release.mjs";

const passedJobs = [
  { name: "changes", conclusion: "success", steps: [] },
  { name: "ci-passed", conclusion: "success", steps: [{ name: "Every job that was due passed", conclusion: "success" }] },
];
const run = (overrides = {}) => ({ event: "push", status: "completed", conclusion: "success", ...overrides });

test("a completed push run whose ci-passed passed qualifies", () => {
  assert.deepEqual(evaluateRun(run(), passedJobs), []);
});

test("a manual run qualifies too", () => {
  assert.deepEqual(evaluateRun(run({ event: "workflow_dispatch" }), passedJobs), []);
});

test("a pull_request run never qualifies", () => {
  assert.match(evaluateRun(run({ event: "pull_request" }), passedJobs).join("\n"), /only push or workflow_dispatch/);
});

test("a run that is still going, failed or was cancelled does not qualify", () => {
  assert.match(evaluateRun(run({ status: "in_progress", conclusion: null }), passedJobs).join("\n"), /still in_progress/);
  assert.match(evaluateRun(run({ conclusion: "failure" }), passedJobs).join("\n"), /concluded failure/);
  assert.match(evaluateRun(run({ conclusion: "cancelled" }), passedJobs).join("\n"), /concluded cancelled/);
});

test("a green run without a passing ci-passed job or verdict step does not qualify", () => {
  assert.match(evaluateRun(run(), []).join("\n"), /job "ci-passed" is missing/);
  const failedJob = [{ name: "ci-passed", conclusion: "failure", steps: [] }];
  assert.match(evaluateRun(run(), failedJob).join("\n"), /job "ci-passed" concluded failure/);
  const noStep = [{ name: "ci-passed", conclusion: "success", steps: [{ name: "Something else", conclusion: "success" }] }];
  assert.match(evaluateRun(run(), noStep).join("\n"), /no step "Every job that was due passed"/);
});
