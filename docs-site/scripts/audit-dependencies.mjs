import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { auditWithRetry, RegistryUnavailableError } from "../../scripts/npm-audit-retry.mjs";

const siteRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const exceptions = JSON.parse(readFileSync(path.join(siteRoot, "dependency-audit-exceptions.json"), "utf8"));
const allowed = new Map(exceptions.exceptions.map((item) => [item.advisory, item]));

let report;
try {
  // Retries only a registry-side failure (an outage or dropped connection); a real advisory in the
  // report is judged below exactly as before, on the first attempt.
  report = await auditWithRetry({ cwd: siteRoot });
} catch (error) {
  if (error instanceof RegistryUnavailableError) {
    console.error(`[docs-audit] ${error.message}`);
    console.error("[docs-audit] This is the npm registry, not a Weir problem. Re-run the job once npm's audit endpoint recovers.");
    process.exit(1);
  }
  throw error;
}

const vulnerabilities = report.vulnerabilities ?? {};
const seenAdvisories = new Set();
const unexpected = new Set();
const expired = new Set();
const today = new Date().toISOString().slice(0, 10);

function inspect(name, entry, trail = new Set()) {
  if (!entry || trail.has(name)) return true;
  const nextTrail = new Set(trail).add(name);
  let safe = true;
  for (const via of entry.via ?? []) {
    if (typeof via === "string") {
      if (!inspect(via, vulnerabilities[via], nextTrail)) safe = false;
      continue;
    }
    const advisory = String(via.url || "").split("/").at(-1);
    if (!advisory) {
      safe = false;
      continue;
    }
    seenAdvisories.add(advisory);
    const exception = allowed.get(advisory);
    if (!exception) {
      unexpected.add(`${name}: ${advisory}`);
    } else if (exception.expires < today) {
      expired.add(advisory);
    }
  }
  return safe && !unexpected.has(name);
}

for (const [name, entry] of Object.entries(vulnerabilities)) inspect(name, entry);
for (const advisory of allowed.keys()) {
  if (seenAdvisories.has(advisory) && !allowed.get(advisory).mitigation) unexpected.add(`Missing mitigation metadata: ${advisory}`);
}

if (unexpected.size || expired.size) {
  console.error("[docs-audit] Dependency audit has an unapproved or expired finding:");
  for (const item of unexpected) console.error(`- ${item}`);
  for (const item of expired) console.error(`- Expired exception: ${item}`);
  process.exit(1);
}

console.log(`[docs-audit] ${seenAdvisories.size} advisory path(s) are covered by current exception metadata.`);
