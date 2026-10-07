#!/usr/bin/env node
// Runs the browser tests that failed in a test project's first run, once more. Browser tests retry once on CI;
// backend tests never do (docs/ci-standard.md), so only the E2E smoke's job calls this.
//
//   node scripts/retry-failed-tests.mjs <test project folder> [title]
//
// The first run (`dotnet test <project> --no-build --logger trx`) left its results in <project>/TestResults. This
// reads the tests that failed there, runs only those again with `dotnet test --no-build` (results go to
// <project>/TestResults-retry), and writes a summary that lists every test that passed only on the retry under
// "Flaky (passed on retry)". It exits 0 only when each test that failed the first time passed the second time. A
// first run that failed without a failed test in its results (a build error, a crash) has nothing to retry and
// fails here.
//
// The text goes to $GITHUB_STEP_SUMMARY when that is set, and to the log either way.
import { spawnSync } from "node:child_process";
import { appendFileSync, existsSync, readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

function attributes(tag) {
  const result = {};
  for (const match of tag.matchAll(/([A-Za-z:]+)="([^"]*)"/g)) result[match[1]] = match[2];
  return result;
}

// One entry per test result in a TRX: the test method it belongs to ("Namespace.Class.Method", which is what
// `dotnet test --filter FullyQualifiedName=` matches, one method for all the rows of a theory) and its outcome.
export function readOutcomes(text) {
  const methods = new Map();
  for (const match of text.matchAll(/<UnitTest\b([^>]*)>[\s\S]*?<TestMethod\b([^>]*?)\/?>/g)) {
    const test = attributes(match[1]);
    const method = attributes(match[2]);
    if (test.id && method.className && method.name) methods.set(test.id, `${method.className}.${method.name}`);
  }
  const outcomes = [];
  for (const match of text.matchAll(/<UnitTestResult\b[^>]*>/g)) {
    const result = attributes(match[0]);
    const method = methods.get(result.testId);
    if (method) outcomes.push({ method, outcome: result.outcome });
  }
  return outcomes;
}

function readFolder(folder) {
  if (!existsSync(folder)) return [];
  return readdirSync(folder)
    .filter((name) => name.toLowerCase().endsWith(".trx"))
    .flatMap((name) => readOutcomes(readFileSync(path.join(folder, name), "utf8")));
}

const failedMethods = (outcomes) => [...new Set(outcomes.filter((entry) => entry.outcome === "Failed").map((entry) => entry.method))].sort();

// `dotnet test --filter` treats these characters as syntax.
const escapeFilterValue = (value) => value.replace(/([\()&|=!~])/g, "\$1");

export function filterFor(methods) {
  return methods.map((method) => `FullyQualifiedName=${escapeFilterValue(method)}`).join("|");
}

// `retried` is every outcome the retry run wrote. A test that failed the first time is flaky when it passed on the
// retry and stuck when it failed there or was not run at all.
export function judge(failedFirst, retried) {
  const failedAgain = new Set(failedMethods(retried));
  const passedAgain = new Set(retried.filter((entry) => entry.outcome === "Passed").map((entry) => entry.method));
  const flaky = failedFirst.filter((method) => passedAgain.has(method) && !failedAgain.has(method));
  return { flaky, failed: failedFirst.filter((method) => !flaky.includes(method)) };
}

export function report({ flaky, failed }, title) {
  const lines = [`### ${title}`, "", `${flaky.length + failed.length} test(s) failed the first attempt and were run once more: ${flaky.length} passed, ${failed.length} failed again.`];
  if (failed.length) {
    lines.push("", "Failed twice:", "");
    for (const method of failed) lines.push(`- ${method}`);
  }
  if (flaky.length) {
    lines.push("", "Flaky (passed on retry):", "");
    for (const method of flaky) lines.push(`- ${method}`);
  }
  return `${lines.join("\n")}\n`;
}

function main() {
  const [project, title = "Browser tests, retry"] = process.argv.slice(2);
  if (!project) {
    console.error("Usage: retry-failed-tests.mjs <test project folder> [title]");
    return 2;
  }
  const failedFirst = failedMethods(readFolder(path.join(project, "TestResults")));
  if (failedFirst.length === 0) {
    console.error(`::error::The first run left no failed test in ${project}/TestResults, so there is nothing to retry. Read its log above.`);
    return 1;
  }
  console.log(`Running ${failedFirst.length} failed test(s) once more:\n${failedFirst.map((method) => `  ${method}`).join("\n")}`);
  const retryFolder = path.join(project, "TestResults-retry");
  const run = spawnSync("dotnet", ["test", project, "--no-build", "--filter", filterFor(failedFirst), "--logger", "trx", "--results-directory", retryFolder], { stdio: "inherit" });
  const verdict = judge(failedFirst, readFolder(retryFolder));
  const text = report(verdict, title);
  console.log(text);
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${text}\n`);
  return verdict.failed.length === 0 && run.status === 0 ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exit(main());
}
