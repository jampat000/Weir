// node --test scripts/verify-ci-for-release.test.mjs
// The release gate's judgement of a CI run, on hand-built runs. The polling loop is exercised against
// the real API instead (see the script's --run-id mode).
import assert from "node:assert/strict";
import { test } from "node:test";

import { evaluateRun, REQUIRED_EVIDENCE } from "./verify-ci-for-release.mjs";

const evidence = REQUIRED_EVIDENCE;

function passingJobs() {
  return [
    ...evidence.map((want) => ({
      name: want.job,
      conclusion: "success",
      steps: want.steps.map((name) => ({ name, conclusion: "success" })),
    })),
    // Jobs the verdict already judged; the gate does not look at them itself.
    { name: "server-linux", conclusion: "skipped", steps: [] },
    { name: "server-windows", conclusion: "success", steps: [] },
  ];
}

const pushRun = { id: 1, event: "push", status: "completed", conclusion: "success", run_attempt: 1 };

test("the evidence is ci.yml's single verdict", () => {
  assert.deepEqual(evidence, [{ job: "ci-passed", steps: ["Every job that was due passed"] }]);
});

test("a completed push run whose verdict passed qualifies, whatever path filtering skipped", () => {
  assert.deepEqual(evaluateRun(pushRun, passingJobs(), evidence), []);
});

test("a manual (workflow_dispatch) run qualifies too", () => {
  assert.deepEqual(evaluateRun({ ...pushRun, event: "workflow_dispatch" }, passingJobs(), evidence), []);
});

test("a pull_request run never qualifies: it tested a merge, not this commit", () => {
  const problems = evaluateRun({ ...pushRun, event: "pull_request" }, passingJobs(), evidence);
  assert.equal(problems.length, 1);
  assert.match(problems[0], /pull_request/);
});

test("a run that passed on its second attempt qualifies (latest attempt is what counts)", () => {
  assert.deepEqual(evaluateRun({ ...pushRun, run_attempt: 2 }, passingJobs(), evidence), []);
});

test("a failed, cancelled or unfinished run does not qualify", () => {
  assert.notDeepEqual(evaluateRun({ ...pushRun, conclusion: "failure" }, passingJobs(), evidence), []);
  assert.notDeepEqual(evaluateRun({ ...pushRun, conclusion: "cancelled" }, passingJobs(), evidence), []);
  assert.match(evaluateRun({ ...pushRun, status: "in_progress", conclusion: null }, passingJobs(), evidence)[0], /in_progress/);
});

test("a verdict that failed, was skipped or is missing does not qualify", () => {
  const failed = passingJobs().map((job) => (job.name === "ci-passed" ? { ...job, conclusion: "failure" } : job));
  assert.match(evaluateRun(pushRun, failed, evidence).join("\n"), /"ci-passed" concluded failure/);

  const skippedStep = passingJobs();
  skippedStep.find((job) => job.name === "ci-passed").steps[0].conclusion = "skipped";
  assert.match(evaluateRun(pushRun, skippedStep, evidence).join("\n"), /"Every job that was due passed" concluded skipped/);

  const missing = passingJobs().filter((job) => job.name !== "ci-passed");
  assert.match(evaluateRun(pushRun, missing, evidence).join("\n"), /"ci-passed" is missing/);
});
