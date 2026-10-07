#!/usr/bin/env node
// Release gate: refuse to publish a tag unless the golden path has passed on the exact commit the tag points at.
// The golden path (docs/release.md, "Golden path before tagging") is a run of the real product on a clean
// machine, installed from that commit's own build. Whoever drives it records the result as a GitHub commit status
// on the commit:
//
//   gh api repos/<owner>/<repo>/statuses/<sha> -f state=success -f context=golden-path \
//     -f description="..." -f target_url="<link to the evidence>"
//
// The gate passes only when the newest status with the context `golden-path` on this commit is `success`. A later
// `failure`, `error` or `pending` for the same context withdraws an earlier `success`, so the record always says
// how the latest run went.
//
// Usage (in release.yml):  node scripts/verify-golden-path-for-release.mjs
//   env GH_TOKEN (statuses: read), GITHUB_REPOSITORY, GITHUB_SHA (else `git rev-parse HEAD`)
// Options: --sha <sha>
import { execFileSync } from "node:child_process";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

export const GOLDEN_PATH_CONTEXT = "golden-path";

export const NO_PASSING_RUN =
  "This commit has no passing golden-path run. Run the golden path on this exact build (see docs/release.md), then re-run the release.";

// The newest status for a context. `statuses` are commit statuses as the API returns them (id, context, state,
// created_at, ...). Ids rise with time, so they settle two statuses created in the same second.
export function latestStatus(statuses, context = GOLDEN_PATH_CONTEXT) {
  let latest = null;
  for (const status of statuses) {
    if (status.context !== context) continue;
    if (!latest || Date.parse(status.created_at) > Date.parse(latest.created_at)
      || (status.created_at === latest.created_at && status.id > latest.id)) {
      latest = status;
    }
  }
  return latest;
}

// Judges a commit's statuses. Returns the reasons the golden path does not count; empty means it passed.
export function evaluateStatuses(statuses, context = GOLDEN_PATH_CONTEXT) {
  const latest = latestStatus(statuses, context);
  if (!latest) return [`no status with the context "${context}" is recorded`];
  if (latest.state !== "success") {
    return [`the latest "${context}" status is ${latest.state}${latest.description ? ` (${latest.description})` : ""}`];
  }
  return [];
}

function listStatuses(repository, sha) {
  // One compact object per line, so a paginated listing needs no JSON array stitching.
  const output = execFileSync(
    "gh",
    [
      "api", "--paginate", `repos/${repository}/commits/${sha}/statuses?per_page=100`,
      "--jq", ".[] | {id, context, state, description, target_url, created_at, creator: .creator.login}",
    ],
    { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 },
  );
  return output.split("\n").filter((line) => line.trim()).map((line) => JSON.parse(line));
}

function main() {
  const argv = process.argv.slice(2);
  const option = (flag) => (argv.includes(flag) ? argv[argv.indexOf(flag) + 1] : undefined);
  const repository = process.env.GITHUB_REPOSITORY;
  if (!repository) throw new Error("GITHUB_REPOSITORY must be set.");
  const sha = option("--sha") || process.env.GITHUB_SHA || execFileSync("git", ["rev-parse", "HEAD^{commit}"], { encoding: "utf8" }).trim();
  if (!/^[0-9a-f]{40}$/.test(sha)) throw new Error(`Not a full commit SHA: ${sha}`);
  console.log(`Release gate: the golden path must already have passed on ${sha}.`);

  const statuses = listStatuses(repository, sha);
  const problems = evaluateStatuses(statuses);
  if (problems.length === 0) {
    const passed = latestStatus(statuses);
    console.log(`PASS: "${GOLDEN_PATH_CONTEXT}" is success on ${sha}, recorded by ${passed.creator} at ${passed.created_at}.`);
    if (passed.description) console.log(`  ${passed.description}`);
    if (passed.target_url) console.log(`  Evidence: ${passed.target_url}`);
    return;
  }

  console.error(`::error::${NO_PASSING_RUN}`);
  for (const problem of problems) console.error(`  - ${problem}`);
  process.exit(1);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    main();
  } catch (error) {
    console.error(`::error::${error.message}`);
    process.exit(1);
  }
}
