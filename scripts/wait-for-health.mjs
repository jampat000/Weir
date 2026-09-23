#!/usr/bin/env node
// Waits for a Weir server to answer /health, then prints the answer. CI and the release start Docker candidates
// with this; when the server never answers, the container's own log is printed so the failure explains itself.
//
// Usage: node scripts/wait-for-health.mjs [--url http://127.0.0.1:9347/health] [--container <name>]
//                                         [--attempts 30] [--interval-seconds 2]
import { execFileSync } from "node:child_process";
import { parseArgs } from "node:util";

const { values } = parseArgs({
  options: {
    url: { type: "string", default: "http://127.0.0.1:9347/health" },
    container: { type: "string" },
    attempts: { type: "string", default: "30" },
    "interval-seconds": { type: "string", default: "2" },
  },
});
const attempts = Number(values.attempts);
const intervalMs = Number(values["interval-seconds"]) * 1000;

let lastProblem = "no attempt made";
for (let attempt = 1; attempt <= attempts; attempt += 1) {
  try {
    const response = await fetch(values.url, { signal: AbortSignal.timeout(5000) });
    const body = await response.text();
    if (response.ok) {
      console.log(body);
      process.exit(0);
    }
    lastProblem = `HTTP ${response.status}: ${body.slice(0, 200)}`;
  } catch (error) {
    lastProblem = error.cause?.code || error.message;
  }
  if (attempt < attempts) await new Promise((done) => setTimeout(done, intervalMs));
}

console.error(`::error::${values.url} did not answer after ${attempts} attempts (last: ${lastProblem}).`);
if (values.container) {
  try {
    execFileSync("docker", ["logs", values.container], { stdio: "inherit" });
  } catch (error) {
    console.error(`Could not read the logs of ${values.container}: ${error.message}`);
  }
}
process.exit(1);
